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
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(name);
        _ = line >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(line));
        _ = column >= 0 ? true : throw new ArgumentOutOfRangeException(nameof(column));

        Address = address;
        Name = name;
        FileName = fileName;
        Line = line;
        Column = column;
    }
}
