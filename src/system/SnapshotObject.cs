// SPDX-License-Identifier: 0BSD

using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

public sealed class SnapshotObject : KernelObject
{
    private SnapshotObject(nint handle)
        : base(handle)
    {
    }

    public static SnapshotObject Create(SnapshotFlags flags, int processId)
    {
        using var handle = CreateToolhelp32Snapshot_SafeHandle((CREATE_TOOLHELP_SNAPSHOT_FLAGS)flags, (uint)processId);

        if (handle.IsInvalid)
            throw new Win32Exception();

        var obj = new SnapshotObject(handle.DangerousGetHandle());

        // Transfer handle ownership to the snapshot object.
        handle.SetHandleAsInvalid();

        return obj;
    }

    public static unsafe SnapshotObject OpenHandle(nint handle)
    {
        uint unused;

        return GetHandleInformation((HANDLE)handle, &unused) ? new(handle) : throw new Win32Exception();
    }

    public IEnumerable<ProcessSnapshot> EnumerateProcesses()
    {
        var handle = SafeHandle;
        var entry = new PROCESSENTRY32W
        {
            dwSize = (uint)Unsafe.SizeOf<PROCESSENTRY32W>(),
        };

        var result = Process32FirstW(handle, ref entry);

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                break;
            }

            if (entry.dwSize == Unsafe.SizeOf<PROCESSENTRY32W>())
                yield return new((int)entry.th32ParentProcessID, (int)entry.th32ProcessID);

            result = Process32NextW(handle, ref entry);
        }
    }

    public IEnumerable<ModuleSnapshot> EnumerateModules()
    {
        var handle = SafeHandle;
        var entry = new MODULEENTRY32W
        {
            dwSize = (uint)Unsafe.SizeOf<MODULEENTRY32W>(),
        };

        var result = Module32FirstW(handle, ref entry);

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                break;
            }

            static unsafe ModuleSnapshot CreateModule(in MODULEENTRY32W entry)
            {
                // Cannot use unsafe code in iterators...

                return new(
                    (int)entry.th32ProcessID,
                    entry.szModule.ToString(),
                    entry.hModule,
                    entry.modBaseAddr,
                    (int)entry.modBaseSize);
            }

            if (entry.dwSize == Unsafe.SizeOf<MODULEENTRY32W>())
                yield return CreateModule(entry);

            result = Module32NextW(handle, ref entry);
        }
    }

    public IEnumerable<ThreadSnapshot> EnumerateThreads()
    {
        var handle = SafeHandle;
        var entry = new THREADENTRY32
        {
            dwSize = (uint)Unsafe.SizeOf<THREADENTRY32>(),
        };

        var result = Thread32First(handle, ref entry);

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                break;
            }

            if (entry.dwSize == Unsafe.SizeOf<THREADENTRY32>())
                yield return new((int)entry.th32ThreadID, (int)entry.th32OwnerProcessID);

            result = Thread32Next(handle, ref entry);
        }
    }

    public IEnumerable<HeapSnapshot> EnumerateHeaps()
    {
        var handle = SafeHandle;
        var entry = new HEAPLIST32
        {
            dwSize = (uint)Unsafe.SizeOf<HEAPLIST32>(),
        };

        var result = Heap32ListFirst(handle, ref entry);

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                break;
            }

            if (entry.dwSize == (uint)Unsafe.SizeOf<HEAPLIST32>())
                yield return new((int)entry.th32ProcessID, (nint)entry.th32HeapID, (HeapSnapshotFlags)entry.dwFlags);

            result = Heap32ListNext(handle, ref entry);
        }
    }
}
