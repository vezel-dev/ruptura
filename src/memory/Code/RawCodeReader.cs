namespace Vezel.Ruptura.Memory.Code;

sealed unsafe class RawCodeReader : CodeReader
{
    byte* _address;

    public RawCodeReader(byte* address)
    {
        _address = address;
    }

    public override int ReadByte()
    {
        return *_address++;
    }
}
