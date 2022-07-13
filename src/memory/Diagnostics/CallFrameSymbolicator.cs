namespace Vezel.Ruptura.Memory.Diagnostics;

public abstract class CallFrameSymbolicator
{
    protected internal abstract CallFrameSymbol? Symbolicate(CallFrame frame);
}
