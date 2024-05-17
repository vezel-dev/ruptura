// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Memory.Diagnostics;

public sealed unsafe class CallFrameSymbol
{
    public void* Address { get; }

    public string Name { get; }

    public string? FileName { get; }

    public int Line { get; }

    public int Column { get; }

    public CallFrameSymbol(void* address, string name, string? fileName, int line, int column)
    {
        Check.Null(address);
        Check.Null(name);
        Check.Range(line >= 0, line);
        Check.Range(column >= 0, column);

        Address = address;
        Name = name;
        FileName = fileName;
        Line = line;
        Column = column;
    }
}
