namespace Vezel.Ruptura.System;

public readonly unsafe struct HeapBlockSnapshot
{
    public int ProcessId { get; }

    public nint HeapId { get; }

    public nint Handle { get; }

    public void* Address { get; }

    public nint Length { get; }

    public HeapBlockSnapshotFlags Flags { get; }

    internal HeapBlockSnapshot(
        int processId, nint heapId, nint handle, void* address, nint length, HeapBlockSnapshotFlags flags)
    {
        ProcessId = processId;
        HeapId = heapId;
        Handle = handle;
        Address = address;
        Length = length;
        Flags = flags;
    }
}
