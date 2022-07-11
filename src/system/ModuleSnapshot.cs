namespace Vezel.Ruptura.System;

public readonly unsafe struct ModuleSnapshot
{
    public int ProcessId { get; }

    public nint Handle { get; }

    public void* Address { get; }

    public int Length { get; }

    internal ModuleSnapshot(int processId, nint handle, void* address, int length)
    {
        ProcessId = processId;
        Handle = handle;
        Address = address;
        Length = length;
    }
}
