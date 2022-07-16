namespace Vezel.Ruptura.Memory.Code;

public abstract unsafe class CodeManager : IDisposable
{
    public abstract void Dispose();

    public abstract CodeAllocation Allocate(nint length, CodePlacement placement);
}
