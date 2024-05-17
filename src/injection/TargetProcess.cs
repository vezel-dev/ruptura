// SPDX-License-Identifier: 0BSD

using Vezel.Ruptura.Injection.IO;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Injection;

public sealed unsafe class TargetProcess : IDisposable
{
    // TODO: Move more of the code here to Vezel.Ruptura.System.

    public int Id { get; }

    public ProcessObject Object
    {
        get
        {
            Check.Usable(!_object.IsDisposed, this);

            return _object;
        }
    }

    public ImageMachine Machine { get; }

    internal int? MainThreadId { get; }

    internal bool IsSupported => Machine == ImageMachine.X64;

    private readonly ProcessObject _object;

    private TargetProcess(int id, ProcessObject @object, int? mainThreadId)
    {
        Id = id;
        _object = @object;
        MainThreadId = mainThreadId;
        Machine = @object.GetWow64Mode() switch
        {
            (ImageMachine.X86, ImageMachine.Unknown) or (_, ImageMachine.X86) => ImageMachine.X86,
            (ImageMachine.X64, ImageMachine.Unknown) or (_, ImageMachine.X64) => ImageMachine.X64,
            (ImageMachine.Arm, ImageMachine.Unknown) or (_, ImageMachine.Arm) => ImageMachine.Arm,
            (ImageMachine.Arm64, ImageMachine.Unknown) or (_, ImageMachine.Arm64) => ImageMachine.Arm64,
            _ => throw new UnreachableException(),
        };
    }

    ~TargetProcess()
    {
        DisposeCore();
    }

    [SuppressMessage("", "CA2000")]
    public static TargetProcess Create(string fileName, string arguments, string? workingDirectory, bool suspended)
    {
        Check.Null(fileName);
        Check.Null(arguments);

        // TODO: Support redirecting standard I/O handles?
        var startupInfo = new STARTUPINFOW
        {
            cb = (uint)sizeof(STARTUPINFOW),
        };

        // CreateProcess can modify the command line arguments, so create a mutable array.
        var args = $"\"{fileName}\" {arguments}\0".ToCharArray().AsSpan();

        if (!CreateProcessW(
            lpApplicationName: null,
            ref args,
            lpProcessAttributes: null,
            lpThreadAttributes: null,
            bInheritHandles: false,
            suspended ? PROCESS_CREATION_FLAGS.CREATE_SUSPENDED : 0,
            lpEnvironment: null,
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
        // I am not sure why we can get away with not using PROCESS_CREATE_THREAD (CreateRemoteThread) and
        // PROCESS_QUERY_LIMITED_INFORMATION (IsWow64Process2), but apparently we can. The below rights are the absolute
        // minimum needed for successful injection (tested on Windows 11 22H2).
        return new(
            id,
            ProcessObject.OpenId(
                id,
                ProcessAccess.CreateThread |
                ProcessAccess.OperateMemory |
                ProcessAccess.ReadMemory |
                ProcessAccess.WriteMemory |
                ProcessAccess.GetLimitedInfo),
            mainThreadId: null);
    }

    public void Dispose()
    {
        DisposeCore();

        GC.SuppressFinalize(this);
    }

    private void DisposeCore()
    {
        _object.Dispose();
    }

    internal ModuleSnapshot? GetModule(string name)
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
            return snapshot
                .EnumerateModules()
                .FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    internal nuint AllocateMemory(nint length, MemoryAccess access)
    {
        return (nuint)_object.AllocateMemory(address: null, length, access);
    }

    internal void FreeMemory(nuint address)
    {
        _object.FreeMemory((void*)address);
    }

    internal nuint CreateFunction(Action<Assembler> action)
    {
        var asm = new Assembler(Machine switch
        {
            ImageMachine.X86 => 32,
            ImageMachine.X64 => 64,
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
        // TODO: https://github.com/microsoft/CsWin32/issues/1180
        using var attributeList = new SafeFileHandle();
        using var handle = CreateRemoteThreadEx(
            _object.SafeHandle,
            lpThreadAttributes: null,
            dwStackSize: 0,
            (delegate* unmanaged[Stdcall]<void*, uint>)address,
            (void*)parameter,
            dwCreationFlags: 0,
            lpAttributeList: attributeList,
            lpThreadId: null);

        if (handle.IsInvalid)
            throw new Win32Exception();

        var obj = ThreadObject.OpenHandle(handle.DangerousGetHandle());

        // Transfer handle ownership to the thread object.
        handle.SetHandleAsInvalid();

        return obj;
    }
}
