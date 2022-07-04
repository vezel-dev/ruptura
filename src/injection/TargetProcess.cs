using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Memory;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.Threading;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Injection;

public sealed unsafe class TargetProcess : IDisposable
{
    public int Id { get; }

    public SafeHandle Handle { get; }

    internal int? MainThreadId { get; }

    internal bool IsCompatible { get; }

    TargetProcess(int id, SafeHandle handle, int? mainThreadId)
    {
        Id = id;
        Handle = handle;
        MainThreadId = mainThreadId;

        IMAGE_FILE_MACHINE os;

        if (!IsWow64Process2(Handle, out var emu, &os))
            throw new Win32Exception();

        // x64 process on an x64 machine or x64 process on an Arm64 machine.
        if ((os, emu) is
            (IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_ARM64, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_AMD64) or
            (IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_AMD64, IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_UNKNOWN))
            IsCompatible = true;
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

        if (!CreateProcess(
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
            _ = CloseHandle(info.hProcess);

            throw;
        }
        finally
        {
            // Ditto.
            _ = CloseHandle(info.hThread);
        }
    }

    public static TargetProcess Open(int id)
    {
        // TODO: Can we reduce the level of access rights we demand here?
        var handle = OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, false, (uint)id);

        return !handle.IsInvalid ? new(id, handle, null) : throw new Win32Exception();
    }

    public void Dispose()
    {
        DisposeCore();

        GC.SuppressFinalize(this);
    }

    void DisposeCore()
    {
        Handle.Dispose();
    }

    internal (nuint Address, nuint Length)? GetModule(string name)
    {
        var pid = (uint)Id;

        SafeFileHandle snap;

        while ((snap = CreateToolhelp32Snapshot_SafeHandle(
            CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPMODULE, pid)).IsInvalid)
        {
            // We may get ERROR_BAD_LENGTH for processes that have not finished initializing or if the process loads
            // or unloads a module while we are capturing the snapshot. For a process that was created suspended, this
            // could get us into an infinite loop, but we handle that with CreateRemoteThread before accessing modules.
            if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_BAD_LENGTH)
                throw new Win32Exception();
        }

        using (snap)
        {
            // TODO: https://github.com/microsoft/CsWin32/issues/597
            var modSize = 568;
            var modSpace = stackalloc byte[modSize];
            var mod = (MODULEENTRY32*)modSpace;

            mod->dwSize = (uint)modSize;

            if (!Module32First(snap, ref *mod))
                throw new Win32Exception();

            do
            {
                if (mod->dwSize != modSize)
                    continue;

                using var modHandle = new SafeFileHandle(mod->hModule, false);

                var arr = new char[MAX_PATH];

                uint len;

                fixed (char* p = arr)
                    while ((len = K32GetModuleBaseName(Handle, modHandle, p, (uint)arr.Length)) >= arr.Length)
                        Array.Resize(ref arr, (int)len);

                var baseName = arr.AsSpan(0, (int)len).ToString();

                if (baseName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return ((nuint)mod->modBaseAddr, mod->modBaseSize);
            }
            while (Module32Next(snap, ref *mod));
        }

        return null;
    }

    internal nuint AllocMemory(nuint length, PAGE_PROTECTION_FLAGS flags)
    {
        return VirtualAllocEx(
            Handle,
            null,
            length,
            VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
            flags) is var ptr && ptr != null
            ? (nuint)ptr
            : throw new Win32Exception();
    }

    internal void FreeMemory(nuint address)
    {
        if (!VirtualFreeEx(Handle, (void*)address, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE))
            throw new Win32Exception();
    }

    internal void ProtectMemory(nuint address, nuint length, PAGE_PROTECTION_FLAGS flags)
    {
        if (!VirtualProtectEx(Handle, (void*)address, length, flags, out _))
            throw new Win32Exception();
    }

    internal void ReadMemory(nuint address, Span<byte> buffer)
    {
        fixed (byte* p = buffer)
            if (!ReadProcessMemory(Handle, (void*)address, p, (nuint)buffer.Length, null))
                throw new Win32Exception();
    }

    internal void WriteMemory(nuint address, ReadOnlySpan<byte> buffer)
    {
        fixed (byte* p = buffer)
            if (!WriteProcessMemory(Handle, (void*)address, p, (nuint)buffer.Length, null))
                throw new Win32Exception();
    }

    internal void FlushCache(nuint address, nuint length)
    {
        if (!FlushInstructionCache(Handle, (void*)address, length))
            throw new Win32Exception();
    }

    internal nuint CreateFunction(Action<Assembler> action)
    {
        var asm = new Assembler(64);

        action(asm);

        using var stream = new MemoryStream();
        var writer = new StreamCodeWriter(stream);

        // Do an initial assembly pass to estimate how much memory we will need.
        _ = asm.Assemble(writer, 0);

        var len = (nuint)stream.Length * 2; // Usually way too much, but safe.
        var code = AllocMemory(len, PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

        try
        {
            stream.Position = 0;

            // Now assemble for real with a known RIP value.
            _ = asm.Assemble(writer, code);

            WriteMemory(code, stream.ToArray());
            ProtectMemory(code, len, PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READ);
            FlushCache(code, len);
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
        var handle = CreateRemoteThreadEx(
            Handle,
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
