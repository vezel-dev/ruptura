using Vezel.Ruptura.Injection.IO;
using Vezel.Ruptura.Injection.Threading;
using static Iced.Intel.AssemblerRegisters;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Injection;

public sealed class AssemblyInjector : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct RupturaParameters
    {
        public nuint Size;

        public char** ArgumentVector;

        public uint ArgumentCount;

        public uint InjectorProcessId;

        public uint MainThreadId;
    }

    private const string NativeEntryPoint = "ruptura_main";

    private readonly TargetProcess _process;

    private readonly AssemblyInjectorOptions _options;

    private bool _injecting;

    private bool _waiting;

    private nuint _loadLibraryW;

    private nuint _getProcAddress;

    private nuint _getLastError;

    private ThreadObject? _thread;

    public AssemblyInjector(TargetProcess process, AssemblyInjectorOptions options)
    {
        Check.Null(process);
        Check.Null(options);
        Check.PlatformSupported(process.IsSupported);

        _process = process;
        _options = options;
    }

    ~AssemblyInjector()
    {
        DisposeCore();
    }

    public void Dispose()
    {
        DisposeCore();

        GC.SuppressFinalize(this);
    }

    private void DisposeCore()
    {
        _thread?.Dispose();
    }

    [SuppressMessage("", "CA1308")]
    private string GetModulePath()
    {
        var path = Path.Combine(
            _options.ModuleDirectory, $"ruptura-{_process.Machine.ToString().ToLowerInvariant()}.dll");

        return File.Exists(path) ? path : throw new InjectionException("Could not locate the Ruptura native module.");
    }

    private void PopulateMemoryArea(nuint area, nint length, Action<ProcessMemoryStream, InjectionBinaryWriter> action)
    {
        using var stream = new ProcessMemoryStream(_process.Object, area, length);
        using var writer = new InjectionBinaryWriter(stream, is64Bit: true);

        action(stream, writer);
    }

    private void ForceLoaderInitialization()
    {
        var initializeShell = _process.CreateFunction(asm =>
        {
            if (asm.Bitness == 64)
            {
                asm.mov(eax, 0);
                asm.ret();
            }
            else
            {
                asm.mov(eax, 0);
                asm.ret(4);
            }
        });

        try
        {
            // Spawning a live thread in a process that was created suspended forces the Windows image loader to finish
            // loading the image so that, among other things, we will be able to resolve kernel32.dll exports.
            using var thread = _process.CreateThread(initializeShell, 0);

            // TODO: Consider making this async with ThreadPool.UnsafeRegisterWaitForSingleObject().
            switch (thread.Wait(_options.InjectionTimeout, alertable: false))
            {
                case WaitResult.Signaled:
                    break;
                case WaitResult.TimedOut:
                    throw new TimeoutException();
                default:
                    throw new UnreachableException();
            }

            if (thread.GetExitCode() is var code and not 0)
                throw new InjectionException($"Failed to initialize the target process: 0x{code:x}");
        }
        finally
        {
            _process.FreeMemory(initializeShell);
        }
    }

    private unsafe void RetrieveKernel32Exports()
    {
        if (_process.GetModule("kernel32.dll") is not ModuleSnapshot k32)
            throw new InjectionException("Could not locate 'kernel32.dll' in the target process.");

        using var stream = new ProcessMemoryStream(_process.Object, (nuint)k32.Address, k32.Length);

        var exports = new PeFile(stream).ExportedFunctions;

        nuint GetExport(string name)
        {
            using var handle = new SafeFileHandle(k32.Handle, ownsHandle: false);

            // Try to resolve the export in the remote process first. If that fails (as it does under e.g. Wine), fall
            // back to the old-fashioned approach of resolving it in the current process and relying on the
            // implementation detail that kernel32.dll is mapped at the same address in all processes.
            fixed (char* p = name)
                return exports?.SingleOrDefault(f => f.Name == name)?.Address is uint offset
                    ? (nuint)k32.Address + offset
                    : GetProcAddress(handle, name) is var proc and { IsNull: false }
                        ? (nuint)(nint)proc
                        : throw new InjectionException($"Could not locate '{name}' in the target process.");
        }

        _loadLibraryW = GetExport("LoadLibraryW");
        _getProcAddress = GetExport("GetProcAddress");
        _getLastError = GetExport("GetLastError");
    }

    private unsafe (nuint Address, nint Length) CreateParameterArea()
    {
        // Keep in sync with src/module/main.h.

        var length = sizeof(RupturaParameters);

        length += sizeof(nuint) * (_options.Arguments.Length + 1);

        foreach (var arg in _options.Arguments.Prepend(_options.FileName))
            length += Encoding.Unicode.GetByteCount(arg) + sizeof(char);

        return (_process.AllocateMemory(length, MemoryAccess.ReadWrite), length);
    }

    private unsafe void PopulateParameterArea(nuint address, nint length)
    {
        // Keep in sync with src/module/main.h.

        PopulateMemoryArea(address, length, (stream, writer) =>
        {
            writer.WriteSize(0);
            writer.WritePointer(0);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0); // Padding.

            var args = _options.Arguments.Prepend(_options.FileName).ToArray();
            var argvOff = (nuint)stream.Position;

            for (var i = 0; i < args.Length; i++)
                writer.WritePointer(0);

            // Write the strings after the argument vector to ensure correct alignment.
            var argOffs = args
                .Select(str =>
                {
                    var argOff = (nuint)stream.Position;

                    writer.WriteUtf16String(str);

                    return argOff;
                })
                .ToArray();

            stream.Position = 0;

            writer.WriteSize((uint)sizeof(RupturaParameters));
            writer.WritePointer(address + argvOff);
            writer.Write((uint)argOffs.Length);
            writer.Write(Environment.ProcessId);
            writer.Write((uint)(_process.MainThreadId ?? 0));
            writer.Write(0); // Padding.

            foreach (var argOff in argOffs)
                writer.WritePointer(address + argOff);
        });
    }

    [SuppressMessage("", "CA2000")]
    private async Task InjectModuleAsync(string modulePath, nuint parameterArea, MemoryMappedViewAccessor accessor)
    {
        var nameAreaLength = Encoding.Unicode.GetByteCount(modulePath) + sizeof(char) +
            Encoding.ASCII.GetByteCount(NativeEntryPoint) + sizeof(byte);
        var nameArea = _process.AllocateMemory(nameAreaLength, MemoryAccess.ReadWrite);

        try
        {
            nuint modulePathPtr = 0;
            nuint entryPointPtr = 0;

            PopulateMemoryArea(nameArea, nameAreaLength, (stream, writer) =>
            {
                modulePathPtr = nameArea + (nuint)stream.Position;

                // Write the module path first to ensure correct alignment.
                writer.WriteUtf16String(modulePath);

                entryPointPtr = nameArea + (nuint)stream.Position;

                writer.WriteAsciiString(NativeEntryPoint);
            });

            var injectShell = _process.CreateFunction(asm =>
            {
                var done = asm.CreateLabel("done");
                var failure = asm.CreateLabel("failure");

                if (asm.Bitness == 64)
                {
                    var shadowSpace = sizeof(ulong) * 4;

                    asm.push(rbx);
                    asm.sub(rsp, shadowSpace);

                    asm.mov(rbx, rcx);
                    asm.mov(rcx, modulePathPtr);
                    asm.mov(rax, _loadLibraryW);
                    asm.call(rax);
                    asm.cmp(rax, 0);
                    asm.je(failure);

                    asm.mov(rcx, rax);
                    asm.mov(rdx, entryPointPtr);
                    asm.mov(rax, _getProcAddress);
                    asm.call(rax);
                    asm.cmp(rax, 0);
                    asm.je(failure);

                    asm.mov(rcx, rbx);
                    asm.call(rax);
                    asm.jmp(done);

                    asm.Label(ref failure);
                    {
                        asm.mov(rax, _getLastError);
                        asm.call(rax);
                    }

                    asm.Label(ref done);
                    {
                        asm.add(rsp, shadowSpace);
                        asm.pop(rbx);
                        asm.ret();
                    }
                }
                else
                {
                    asm.push((uint)modulePathPtr);
                    asm.mov(eax, (uint)_loadLibraryW);
                    asm.call(eax);
                    asm.cmp(eax, 0);
                    asm.je(failure);

                    asm.push((uint)entryPointPtr);
                    asm.push(eax);
                    asm.mov(eax, (uint)_getProcAddress);
                    asm.call(eax);
                    asm.cmp(eax, 0);
                    asm.je(failure);

                    asm.push(__dword_ptr[esp + sizeof(uint)]);
                    asm.call(eax);
                    asm.add(esp, sizeof(uint));
                    asm.jmp(done);

                    asm.Label(ref failure);
                    {
                        asm.mov(eax, (uint)_getLastError);
                        asm.call(eax);
                    }

                    asm.Label(ref done);
                    {
                        asm.ret(sizeof(uint));
                    }
                }
            });

            try
            {
                var thread = _process.CreateThread(injectShell, parameterArea);

                try
                {
                    var sw = Stopwatch.StartNew();
                    var timeout = _options.InjectionTimeout;

                    while (true)
                    {
                        // Did injection complete successfully?
                        if (accessor.ReadBoolean(0))
                            break;

                        // Did the thread exit with an error?
                        switch (thread.Wait(TimeSpan.Zero, alertable: false))
                        {
                            case WaitResult.Signaled:
                                throw new InjectionException(
                                    $"Failed to inject the native module into the target process: " +
                                    $"0x{thread.GetExitCode():x}");
                            case WaitResult.TimedOut:
                                break;
                            default:
                                throw new UnreachableException();
                        }

                        // TODO: Is this a reasonable polling interval?
                        await Task.Delay(100).ConfigureAwait(false);

                        if ((long)timeout.TotalMilliseconds != Timeout.Infinite && sw.Elapsed >= timeout)
                            throw new TimeoutException();
                    }
                }
                catch (Exception)
                {
                    thread.Dispose();

                    throw;
                }

                _thread = thread;
            }
            finally
            {
                _process.FreeMemory(injectShell);
            }
        }
        finally
        {
            _process.FreeMemory(nameArea);
        }
    }

    public Task InjectAssemblyAsync()
    {
        Check.Operation(!_injecting);

        _injecting = true;

        return Task.Run(async () =>
        {
            try
            {
                var modulePath = GetModulePath();

                using var mmf = MemoryMappedFile.CreateNew(
                    $"ruptura-{Environment.ProcessId}-{_process.Id}", sizeof(bool));
                using var accessor = mmf.CreateViewAccessor(0, sizeof(bool), MemoryMappedFileAccess.Read);

                ForceLoaderInitialization();
                RetrieveKernel32Exports();

                var (paramsArea, paramsAreaLength) = CreateParameterArea();

                try
                {
                    PopulateParameterArea(paramsArea, paramsAreaLength);

                    await InjectModuleAsync(modulePath, paramsArea, accessor).ConfigureAwait(false);
                }
                finally
                {
                    _process.FreeMemory(paramsArea);
                }
            }
            catch (Exception ex) when (ex is not TimeoutException)
            {
                throw new InjectionException("Assembly injection failed.", ex);
            }
        });
    }

    public Task<int> WaitForCompletionAsync()
    {
        Check.Operation(_thread != null && !_waiting);

        _waiting = true;

        return Task.Run(async () =>
        {
            // This is safe because the lambda below captures the thread object and keeps it alive.
            using var waitHandle = new ThreadWaitHandle(
                new(_thread.SafeHandle.DangerousGetHandle(), ownsHandle: false));

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = ThreadPool.UnsafeRegisterWaitForSingleObject(
                waitHandle,
                (_, timeout) =>
                {
                    if (timeout)
                    {
                        tcs.SetException(ExceptionDispatchInfo.SetCurrentStackTrace(new TimeoutException()));

                        return;
                    }

                    int code;

                    try
                    {
                        code = _thread.GetExitCode();
                    }
                    catch (Win32Exception ex)
                    {
                        tcs.SetException(ex);

                        return;
                    }

                    tcs.SetResult(code);
                },
                state: null,
                _options.CompletionTimeout,
                executeOnlyOnce: true);

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _ = registration.Unregister(waitObject: null);
            }
        });
    }
}
