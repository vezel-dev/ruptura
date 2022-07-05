namespace Vezel.Ruptura.Injection.IO;

sealed class InjectionBinaryWriter : BinaryWriter
{
    readonly bool _is64Bit;

    public InjectionBinaryWriter(Stream stream, bool is64Bit)
        : base(stream)
    {
        _is64Bit = is64Bit;
    }

    public void WritePointer(nuint value)
    {
        if (_is64Bit)
            Write(value);
        else
            Write((uint)value);
    }

    public void WriteSize(nuint value)
    {
        WritePointer(value);
    }

    public void WriteAsciiString(string value)
    {
        foreach (var b in Encoding.ASCII.GetBytes(value))
            Write(b);

        Write((byte)0);
    }

    public void WriteUtf16String(string value)
    {
        foreach (var b in Encoding.Unicode.GetBytes(value))
            Write(b);

        Write((ushort)0);
    }
}
