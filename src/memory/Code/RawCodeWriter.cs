namespace Vezel.Ruptura.Memory.Code;

sealed unsafe class RawCodeWriter : CodeWriter
{
    byte* _address;

    public RawCodeWriter(byte* address)
    {
        _address = address;
    }

    public override void WriteByte(byte value)
    {
        *_address++ = value;
    }
}
