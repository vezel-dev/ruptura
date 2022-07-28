namespace Vezel.Ruptura.System;

[SuppressMessage("", "CA1815")]
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
