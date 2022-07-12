namespace Vezel.Ruptura.Memory.Code;

sealed class NullCodeWriter : CodeWriter
{
    public nint Length { get; private set; }

    public override void WriteByte(byte value)
    {
        Length++;
    }
}
