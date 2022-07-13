using Vezel.Ruptura.Injection.IO;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Memory;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.Threading;
using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Injection;

public sealed unsafe class TargetProcess : IDisposable
{
    public int Id { get; }

    public SafeHandle Handle => !_handle.IsClosed ? _handle : throw new ObjectDisposedException(GetType().Name);

    public Architecture Architecture { get; }

    internal int? MainThreadId { get; }

    internal bool IsSupported => Architecture == Architecture.X64;

    readonly SafeHandle _handle;

    TargetProcess(int id, SafeHandle handle, int? mainThreadId)
    {
        Id = id;
        _handle = handle;
        MainThreadId = mainThreadId;

        IMAGE_FILE_MACHINE os;

        if (!Win32.IsWow64Process2(handle, out var proc, &os))
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
                new SafeFileHandle(info.hProcess, true),
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
        // TODO: Can we reduce the level of access rights we demand here?
        var handle = Win32.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, false, (uint)id);

        return !handle.IsInvalid ? new(id, handle, null) : throw new Win32Exception();
    }

    public void Dispose()
    {
        DisposeCore();

        GC.SuppressFinalize(this);
    }

    void DisposeCore()
    {
        _handle.Dispose();
    }

    internal (nuint Address, nuint Length)? GetModule(string name)
    {
        SafeFileHandle snap;

        while ((snap = Win32.CreateToolhelp32Snapshot_SafeHandle(
            CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPMODULE, (uint)Id)).IsInvalid)
        {
            // We may get ERROR_BAD_LENGTH for processes that have not finished initializing or if the process loads
            // or unloads a module while we are capturing the snapshot. For a process that was created suspended, this
            // could get us into an infinite loop, but we handle that with CreateRemoteThread before accessing modules.
            if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_BAD_LENGTH)
                throw new Win32Exception();
        }

        using (snap)
        {
            var entry = new MODULEENTRY32W
            {
                dwSize = (uint)sizeof(MODULEENTRY32W),
            };

            var result = Win32.Module32FirstW(snap, ref entry);

            while (true)
            {
                if (!result)
                {
                    if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                        throw new Win32Exception();

                    break;
                }

                if (entry.dwSize != Unsafe.SizeOf<MODULEENTRY32W>())
                    continue;

                using var modHandle = new SafeFileHandle(entry.hModule, false);

                var arr = new char[Win32.MAX_PATH];

                uint len;

                fixed (char* p = arr)
                    while ((len = Win32.K32GetModuleBaseNameW(_handle, modHandle, p, (uint)arr.Length)) >= arr.Length)
                        Array.Resize(ref arr, (int)len);

                var baseName = arr.AsSpan(0, (int)len).ToString();

                if (baseName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return ((nuint)entry.modBaseAddr, entry.modBaseSize);

                result = Win32.Module32NextW(snap, ref entry);
            }
        }

        return null;
    }

    internal nuint AllocMemory(nuint length, PAGE_PROTECTION_FLAGS flags)
    {
        return Win32.VirtualAlloc2(
            _handle,
            null,
            length,
            VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
            (uint)flags,
            Span<MEM_EXTENDED_PARAMETER>.Empty) is var ptr and not null
            ? (nuint)ptr
            : throw new Win32Exception();
    }

    internal void FreeMemory(nuint address)
    {
        if (!Win32.VirtualFreeEx(_handle, (void*)address, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE))
            throw new Win32Exception();
    }

    internal void ProtectMemory(nuint address, nuint length, PAGE_PROTECTION_FLAGS flags)
    {
        if (!Win32.VirtualProtectEx(_handle, (void*)address, length, flags, out _))
            throw new Win32Exception();
    }

    internal void ReadMemory(nuint address, Span<byte> buffer)
    {
        fixed (byte* p = buffer)
            if (!Win32.ReadProcessMemory(_handle, (void*)address, p, (nuint)buffer.Length, null))
                throw new Win32Exception();
    }

    internal void WriteMemory(nuint address, ReadOnlySpan<byte> buffer)
    {
        fixed (byte* p = buffer)
            if (!Win32.WriteProcessMemory(_handle, (void*)address, p, (nuint)buffer.Length, null))
                throw new Win32Exception();
    }

    internal void FlushCache(nuint address, nuint length)
    {
        if (!Win32.FlushInstructionCache(_handle, (void*)address, length))
            throw new Win32Exception();
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

        var len = (nuint)tempStream.Length * 2; // Usually way too much, but safe.
        var code = AllocMemory(len, PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

        try
        {
            using var stream = new ProcessMemoryStream(this, code, len);

            // Now assemble into the process for real with a known RIP value.
            _ = asm.Assemble(new StreamCodeWriter(stream), code);

            ProtectMemory(code, len, PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READ);
        }
        catch (Exception)
        {
            FreeMemory(code);

            throw;
        }

        return code;
    }

    internal SafeHandle CreateThread(nuint address, nuint parameter)
    {
        var handle = Win32.CreateRemoteThreadEx(
            _handle,
            null,
            0,
            (delegate* unmanaged[Stdcall]<void*, uint>)address,
            (void*)parameter,
            0,
            (LPPROC_THREAD_ATTRIBUTE_LIST)null,
            null);

        return !handle.IsInvalid ? handle : throw new Win32Exception();
    }
}
