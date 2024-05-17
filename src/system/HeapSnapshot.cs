// SPDX-License-Identifier: 0BSD

using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

[SuppressMessage("", "CA1815")]
public readonly struct HeapSnapshot
{
    public int ProcessId { get; }

    public nint Id { get; }

    public HeapSnapshotFlags Flags { get; }

    internal HeapSnapshot(int processId, nint id, HeapSnapshotFlags flags)
    {
        ProcessId = processId;
        Id = id;
        Flags = flags;
    }

    public IEnumerable<HeapBlockSnapshot> EnumerateHeapBlocks()
    {
        var entry = new HEAPENTRY32
        {
            dwSize = (uint)Unsafe.SizeOf<HEAPENTRY32>(),
        };

        var result = Heap32First(ref entry, (uint)ProcessId, (nuint)Id);

        while (true)
        {
            if (!result)
            {
                if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    throw new Win32Exception();

                yield break;
            }

            static unsafe HeapBlockSnapshot CreateHeapBlock(in HEAPENTRY32 entry)
            {
                // Cannot use unsafe code in iterators...

                return new(
                    (int)entry.th32ProcessID,
                    (nint)entry.th32HeapID,
                    entry.hHandle,
                    (void*)entry.dwAddress,
                    (nint)entry.dwBlockSize,
                    (HeapBlockSnapshotFlags)entry.dwFlags);
            }

            yield return CreateHeapBlock(entry);

            result = Heap32Next(ref entry);
        }
    }
}
