using Vezel.Ruptura.Injection.IO;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.Threading;
using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Injection;

public sealed unsafe class TargetProcess : IDisposable
{
    // TODO: Move more of the code here to Vezel.Ruptura.System.

    public int Id { get; }

    public ProcessObject Object => !_object.IsDisposed ? _object : throw new ObjectDisposedException(GetType().Name);

    public Architecture Architecture { get; }

    internal int? MainThreadId { get; }

    internal bool IsSupported => Architecture == Architecture.X64;

    readonly ProcessObject _object;

    TargetProcess(int id, ProcessObject @object, int? mainThreadId)
    {
        Id = id;
        _object = @object;
        MainThreadId = mainThreadId;

        IMAGE_FILE_MACHINE os;

        if (!Win32.IsWow64Process2(@object.SafeHandle, out var proc, &os))
            throw new Win32Exception();

        Architecture = (os, proc) switch
        {
            (IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_I386, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_UNKNOWN) or
            (_, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_I386) =>
                Architecture.X86,
            (IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_AMD64, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_UNKNOWN) or
            (_, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_AMD64) =>
                Architecture.X64,
            (IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_ARM, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_UNKNOWN) or
            (_, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_ARM) =>
                Architecture.Arm,
            (IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_ARM64, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_UNKNOWN) or
            (_, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_ARM64) =>
                Architecture.Arm64,
            _ => throw new UnreachableException(),
        };
    }

    ~TargetProcess()
    {
        DisposeCore();
    }

    public static TargetProcess Create(string fileName, string arguments, string? workingDirectory, bool suspended)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        // TODO: Support redirecting standard I/O handles?
        var startupInfo = new STARTUPINFOW
        {
            cb = (uint)sizeof(STARTUPINFOW),
        };

        // CreateProcess can modify the command line arguments, so create a mutable array.
        var args = $"\"{fileName}\" {arguments}\0".ToCharArray().AsSpan();

        if (!Win32.CreateProcessW(
            null,
            ref args,
            null,
            null,
            false,
            suspended ? PROCESS_CREATION_FLAGS.CREATE_SUSPENDED : 0,
            null,
            workingDirectory,
            startupInfo,
            out var info))
            throw new Win32Exception();

        try
        {
            return new(
                (int)info.dwProcessId,
                ProcessObject.OpenHandle(info.hProcess),
                suspended ? (int)info.dwThreadId : null);
        }
        catch (Exception)
        {
            // Not much can be done if this fails.
            _ = Win32.CloseHandle(info.hProcess);

            throw;
        }
        finally
        {
            // Ditto.
            _ = Win32.CloseHandle(info.hThread);
        }
    }

    public static TargetProcess Open(int id)
    {
        // I am not sure why we can get away with not using PROCESS_CREATE_THREAD (CreateRemoteThread) and
        // PROCESS_QUERY_LIMITED_INFORMATION (IsWow64Process2), but apparently we can. The below rights are the absolute
        // minimum needed for successful injection (tested on Windows 11 22H2).
        using var handle = Win32.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION |
            PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ |
            PROCESS_ACCESS_RIGHTS.PROCESS_VM_WRITE,
            false,
            (uint)id);

        var obj = ProcessObject.OpenHandle(handle.DangerousGetHandle());

        // Transfer handle ownership to the process object.
        handle.SetHandleAsInvalid();

        return new(id, obj, null);
    }

    public void Dispose()
    {
        DisposeCore();

        GC.SuppressFinalize(this);
    }

    void DisposeCore()
    {
        _object.Dispose();
    }

    internal (nuint Address, nint Length)? GetModule(string name)
    {
        SnapshotObject snapshot;

        while (true)
        {
            try
            {
                snapshot = SnapshotObject.Create(SnapshotFlags.Modules, Id);
            }
            catch (Win32Exception ex) when (ex.ErrorCode == (int)WIN32_ERROR.ERROR_BAD_LENGTH)
            {
                // We may get ERROR_BAD_LENGTH for processes that have not finished initializing or if the process loads
                // or unloads a module while we are capturing the snapshot. For a process that was created suspended,
                // this could get us into an infinite loop, but we handle that with CreateRemoteThread before accessing
                // modules.
                continue;
            }

            break;
        }

        using (snapshot)
        {
            foreach (var mod in snapshot.EnumerateModules())
            {
                using var handle = new SafeFileHandle(mod.Handle, false);

                var arr = new char[Win32.MAX_PATH];

                uint len;

                fixed (char* p = arr)
                    while ((len = Win32.K32GetModuleBaseNameW(
                        _object.SafeHandle, handle, p, (uint)arr.Length)) >= arr.Length)
                        Array.Resize(ref arr, (int)len);

                var baseName = arr.AsSpan(0, (int)len).ToString();

                if (baseName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return ((nuint)mod.Address, mod.Length);
            }
        }

        return null;
    }

    internal nuint AllocateMemory(nint length, MemoryAccess access)
    {
        return (nuint)_object.AllocateMemory(null, length, access);
    }

    internal void FreeMemory(nuint address)
    {
        _object.FreeMemory((void*)address);
    }

    internal nuint CreateFunction(Action<Assembler> action)
    {
        var asm = new Assembler(Architecture switch
        {
            Architecture.X86 => 32,
            Architecture.X64 => 64,
            _ => throw new UnreachableException(),
        });

        action(asm);

        using var tempStream = new MemoryStream();

        // Do an initial assembly pass to estimate how much memory we will need.
        _ = asm.Assemble(new StreamCodeWriter(tempStream), 0);

        var len = (nint)tempStream.Length * 2; // Usually way too much, but safe.
        var code = AllocateMemory(len, MemoryAccess.ReadWrite);

        try
        {
            using var stream = new ProcessMemoryStream(_object, code, len);

            // Now assemble into the process for real with a known RIP value.
            _ = asm.Assemble(new StreamCodeWriter(stream), code);

            _ = _object.ProtectMemory((void*)code, len, MemoryAccess.ExecuteRead);
        }
        catch (Exception)
        {
            FreeMemory(code);

            throw;
        }

        return code;
    }

    internal ThreadObject CreateThread(nuint address, nuint parameter)
    {
        using var handle = Win32.CreateRemoteThreadEx(
            _object.SafeHandle,
            null,
            0,
            (delegate* unmanaged[Stdcall]<void*, uint>)address,
            (void*)parameter,
            0,
            (LPPROC_THREAD_ATTRIBUTE_LIST)null,
            null);

        if (handle.IsInvalid)
            throw new Win32Exception();

        var obj = ThreadObject.OpenHandle(handle.DangerousGetHandle());

        // Transfer handle ownership to the thread object.
        handle.SetHandleAsInvalid();

        return obj;
    }
}
