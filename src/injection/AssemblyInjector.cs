using Vezel.Ruptura.Injection.IO;
using Vezel.Ruptura.Injection.Threading;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using static Iced.Intel.AssemblerRegisters;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Injection;

public sealed class AssemblyInjector : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    unsafe struct RupturaParameters
    {
        public nuint Size;

        public char** ArgumentVector;

        public uint ArgumentCount;

        public uint InjectorProcessId;

        public uint MainThreadId;
    }

    const string NativeEntryPoint = "ruptura_main";

    readonly TargetProcess _process;

    readonly AssemblyInjectorOptions _options;

    bool _injecting;

    nuint _loadLibraryW;

    nuint _getProcAddress;

    nuint _getLastError;

    SafeHandle? _threadHandle;

    public AssemblyInjector(TargetProcess process, AssemblyInjectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(options);
        _ = process.IsSupported ? true : throw new PlatformNotSupportedException();

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

    void DisposeCore()
    {
        _threadHandle?.Dispose();
    }

    string GetModulePath()
    {
        var path = Path.Combine(
            _options.ModuleDirectory, $"ruptura-{_process.Architecture.ToString().ToLowerInvariant()}.dll");

        return File.Exists(path) ? path : throw new InjectionException("Could not locate the Ruptura native module.");
    }

    void PopulateMemoryArea(nuint area, Action<MemoryStream, InjectionBinaryWriter> action)
    {
        using var stream = new MemoryStream();

        using (var writer = new InjectionBinaryWriter(stream, true))
            action(stream, writer);

        _process.WriteMemory(area, stream.ToArray());
    }

    void ForceLoaderInitialization()
    {
        var initializeShell = _process.CreateFunction(asm =>
        {
            if (asm.Bitness == 32)
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
            using var threadHandle = _process.CreateThread(initializeShell, 0);

            switch ((WIN32_ERROR)WaitForSingleObjectEx(
                threadHandle, (uint)(long)_options.InjectionTimeout.TotalMilliseconds, false))
            {
                case WIN32_ERROR.WAIT_OBJECT_0:
                    break;
                case WIN32_ERROR.WAIT_TIMEOUT:
                    throw new TimeoutException();
                default:
                    throw new Win32Exception();
            }

            if (!GetExitCodeThread(threadHandle, out var code))
                throw new Win32Exception();

            if (code != 0)
                throw new InjectionException($"Failed to initialize the target process: 0x{code:x}");
        }
        finally
        {
            _process.FreeMemory(initializeShell);
        }
    }

    void RetrieveKernel32Exports()
    {
        if (_process.GetModule("kernel32.dll") is not (var k32Addr, var k32Size))
            throw new InjectionException("Could not locate 'kernel32.dll' in the target process.");

        using var stream = new ProcessMemoryReadStream(_process, k32Addr, k32Size);

        var exports = new PeFile(stream).ExportedFunctions;

        nuint GetExport(string name)
        {
            return exports?.SingleOrDefault(f => f.Name == name)?.Address is uint offset
                ? k32Addr + offset
                : throw new InjectionException($"Could not locate '{name}' in the target process.");
        }

        _loadLibraryW = GetExport("LoadLibraryW");
        _getProcAddress = GetExport("GetProcAddress");
        _getLastError = GetExport("GetLastError");
    }

    unsafe nuint CreateParameterArea()
    {
        // Keep in sync with src/module/main.h.

        var size = sizeof(RupturaParameters);

        size += sizeof(nuint) * (_options.Arguments.Count + 1);

        foreach (var arg in _options.Arguments.Prepend(_options.FileName))
            size += Encoding.Unicode.GetByteCount(arg) + sizeof(char);

        return _process.AllocMemory((nuint)size, PAGE_PROTECTION_FLAGS.PAGE_READWRITE);
    }

    unsafe void PopulateParameterArea(nuint parameterArea)
    {
        // Keep in sync with src/module/main.h.

        PopulateMemoryArea(parameterArea, (stream, writer) =>
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
            writer.WritePointer(parameterArea + argvOff);
            writer.Write((uint)argOffs.Length);
            writer.Write(Environment.ProcessId);
            writer.Write((uint)(_process.MainThreadId ?? 0));
            writer.Write(0); // Padding.

            foreach (var argOff in argOffs)
                writer.WritePointer(parameterArea + argOff);
        });
    }

    async Task InjectModuleAsync(string modulePath, nuint parameterArea, MemoryMappedViewAccessor accessor)
    {
        var size = Encoding.Unicode.GetByteCount(modulePath) + sizeof(char) +
            Encoding.ASCII.GetByteCount(NativeEntryPoint) + sizeof(byte);
        var nameArea = _process.AllocMemory((uint)size, PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

        try
        {
            nuint modulePathPtr = 0;
            nuint entryPointPtr = 0;

            PopulateMemoryArea(nameArea, (stream, writer) =>
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

                if (asm.Bitness == 32)
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

                    asm.push(__dword_ptr[esp + 4]);
                    asm.call(eax);
                    asm.jmp(done);

                    asm.Label(ref failure);
                    asm.mov(eax, (uint)_getLastError);
                    asm.call(eax);

                    asm.Label(ref done);
                    asm.ret(4);
                }
                else
                {
                    asm.push(rbx);
                    asm.sub(rsp, 32);

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
                    asm.mov(rax, _getLastError);
                    asm.call(rax);

                    asm.Label(ref done);
                    asm.add(rsp, 32);
                    asm.pop(rbx);
                    asm.ret();
                }
            });

            try
            {
                var threadHandle = _process.CreateThread(injectShell, parameterArea);

                try
                {
                    var sw = Stopwatch.StartNew();
                    var timeout = _options.InjectionTimeout;

                    while (true)
                    {
                        // Did injection complete successfully?
                        if (accessor.ReadBoolean(0))
                        {
                            _threadHandle = threadHandle;

                            break;
                        }

                        // Did the thread exit with an error?
                        switch ((WIN32_ERROR)WaitForSingleObjectEx(threadHandle, 0, false))
                        {
                            case WIN32_ERROR.WAIT_OBJECT_0:
                                if (!GetExitCodeThread(threadHandle, out var code))
                                    throw new Win32Exception();

                                throw new InjectionException(
                                    $"Failed to inject the native module into the target process: 0x{code:x}");
                            case WIN32_ERROR.WAIT_TIMEOUT:
                                break;
                            default:
                                throw new Win32Exception();
                        }

                        await Task.Delay(100);

                        if ((long)timeout.TotalMilliseconds != Timeout.Infinite && sw.Elapsed >= timeout)
                            throw new TimeoutException();
                    }
                }
                catch (Exception)
                {
                    threadHandle.Dispose();

                    throw;
                }

                _threadHandle = threadHandle;
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
        _ = !_injecting ? true : throw new InvalidOperationException();

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

                var paramsArea = CreateParameterArea();

                try
                {
                    PopulateParameterArea(paramsArea);

                    await InjectModuleAsync(modulePath, paramsArea, accessor).ConfigureAwait(false);
                }
                finally
                {
                    _process.FreeMemory(paramsArea);
                }
            }
            catch (Exception ex) when (ex is not TimeoutException)
            {
                throw new InjectionException(null, ex);
            }
        });
    }

    public Task<int> WaitForCompletionAsync()
    {
        _ = _threadHandle is not null and { IsInvalid: false } ? true : throw new InvalidOperationException();

        return Task.Run(async () =>
        {
            using var waitHandle = new ThreadWaitHandle(new(_threadHandle.DangerousGetHandle(), true));

            // Transfer ownership of the native handle from _threadHandle to waitHandle so that it stays alive until the
            // injected assembly returns, allowing us to retrieve the exit code.
            _threadHandle.SetHandleAsInvalid();

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = ThreadPool.UnsafeRegisterWaitForSingleObject(
                waitHandle,
                (_, timeout) =>
                {
                    var ex = default(Exception);

                    if (timeout)
                        ex = new TimeoutException();
                    else if (!GetExitCodeThread(waitHandle.SafeWaitHandle, out var code))
                        ex = new Win32Exception();
                    else
                        tcs.SetResult((int)code);

                    if (ex != null)
                        tcs.SetException(ExceptionDispatchInfo.SetCurrentStackTrace(ex));
                },
                null,
                _options.CompletionTimeout,
                true);

            try
            {
                return await tcs.Task;
            }
            finally
            {
                _ = registration.Unregister(null);
            }
        });
    }
}
