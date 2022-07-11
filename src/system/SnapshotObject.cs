using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

public sealed unsafe class SnapshotObject : KernelObject
{
    SnapshotObject(nint handle)
        : base(handle)
    {
    }

    public static SnapshotObject Create(SnapshotFlags flags, int processId)
    {
        using var handle = Win32.CreateToolhelp32Snapshot_SafeHandle(
            (CREATE_TOOLHELP_SNAPSHOT_FLAGS)flags, (uint)processId);

        if (handle.IsInvalid)
            throw new Win32Exception();

        var obj = new SnapshotObject(handle.DangerousGetHandle());

        // Transfer handle ownership to the snapshot object.
        handle.SetHandleAsInvalid();

        return obj;
    }

    public static SnapshotObject OpenHandle(nint handle)
    {
        uint unused;

        return Win32.GetHandleInformation((HANDLE)handle, &unused) ? new(handle) : throw new Win32Exception();
    }

    public IEnumerable<ProcessSnapshot> EnumerateProcesses()
    {
        var handle = SafeHandle;

        // TODO: https://github.com/microsoft/CsWin32/issues/597
        var entrySize = 568;
        var entrySpace = new byte[entrySize];

        MemoryMarshal.AsRef<PROCESSENTRY32W>(entrySpace).dwSize = (uint)entrySize;

        var result = Win32.Process32FirstW(handle, ref MemoryMarshal.AsRef<PROCESSENTRY32W>(entrySpace));

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                yield break;
            }

            ProcessSnapshot? CreateProcess(ReadOnlySpan<byte> space)
            {
                ref readonly var entry = ref MemoryMarshal.AsRef<PROCESSENTRY32W>(space);

                return entry.dwSize == entrySize ? new((int)entry.th32ParentProcessID, (int)entry.th32ProcessID) : null;
            }

            if (CreateProcess(entrySpace) is ProcessSnapshot process)
                yield return process;

            result = Win32.Process32NextW(handle, ref MemoryMarshal.AsRef<PROCESSENTRY32W>(entrySpace));
        }
    }

    public IEnumerable<ModuleSnapshot> EnumerateModules()
    {
        var handle = SafeHandle;

        // TODO: https://github.com/microsoft/CsWin32/issues/597
        var entrySize = 1080;
        var entrySpace = new byte[entrySize];

        MemoryMarshal.AsRef<MODULEENTRY32W>(entrySpace).dwSize = (uint)entrySize;

        var result = Win32.Module32FirstW(handle, ref MemoryMarshal.AsRef<MODULEENTRY32W>(entrySpace));

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                yield break;
            }

            ModuleSnapshot? CreateModule(ReadOnlySpan<byte> space)
            {
                ref readonly var entry = ref MemoryMarshal.AsRef<MODULEENTRY32W>(space);

                return entry.dwSize == entrySize
                    ? new((int)entry.th32ProcessID, entry.hModule, entry.modBaseAddr, (int)entry.modBaseSize)
                    : null;
            }

            if (CreateModule(entrySpace) is ModuleSnapshot module)
                yield return module;

            result = Win32.Module32NextW(handle, ref MemoryMarshal.AsRef<MODULEENTRY32W>(entrySpace));
        }
    }

    public IEnumerable<ThreadSnapshot> EnumerateThreads()
    {
        var handle = SafeHandle;
        var entry = new THREADENTRY32
        {
            dwSize = (uint)Unsafe.SizeOf<THREADENTRY32>(),
        };

        var result = Win32.Thread32First(handle, ref entry);

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                yield break;
            }

            if (entry.dwSize == Unsafe.SizeOf<THREADENTRY32>())
                yield return new((int)entry.th32ThreadID, (int)entry.th32OwnerProcessID);

            result = Win32.Thread32Next(handle, ref entry);
        }
    }

    public IEnumerable<HeapSnapshot> EnumerateHeaps()
    {
        var handle = SafeHandle;
        var entry = new HEAPLIST32
        {
            dwSize = (uint)Unsafe.SizeOf<HEAPLIST32>(),
        };

        var result = Win32.Heap32ListFirst(handle, ref entry);

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                yield break;
            }

            if (entry.dwSize == (uint)Unsafe.SizeOf<HEAPLIST32>())
                yield return new((int)entry.th32ProcessID, (nint)entry.th32HeapID, (HeapSnapshotFlags)entry.dwFlags);

            result = Win32.Heap32ListNext(handle, ref entry);
        }
    }
}
