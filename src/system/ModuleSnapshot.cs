namespace Vezel.Ruptura.System;

[SuppressMessage("", "CA1815")]
public readonly unsafe struct ModuleSnapshot
{
    public int ProcessId { get; }

    public string Name { get; }

    public nint Handle { get; }

    public void* Address { get; }

    public int Length { get; }

    internal ModuleSnapshot(int processId, string name, nint handle, void* address, int length)
    {
        ProcessId = processId;
        Name = name;
        Handle = handle;
        Address = address;
        Length = length;
    }
}
