namespace Vezel.Ruptura.System;

public readonly struct ProcessSnapshot
{
    public int ParentId { get; }

    public int Id { get; }

    internal ProcessSnapshot(int parentId, int id)
    {
        ParentId = parentId;
        Id = id;
    }
}
