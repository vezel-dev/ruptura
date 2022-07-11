namespace Vezel.Ruptura.System;

public readonly struct ThreadSnapshot
{
    public int ProcessId { get; }

    public int Id { get; }

    internal ThreadSnapshot(int processId, int id)
    {
        ProcessId = processId;
        Id = id;
    }
}
