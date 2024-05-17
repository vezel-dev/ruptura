// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.System;

[SuppressMessage("", "CA1815")]
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
