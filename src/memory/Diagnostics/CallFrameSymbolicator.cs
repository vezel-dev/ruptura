namespace Vezel.Ruptura.Memory.Diagnostics;

public abstract class CallFrameSymbolicator
{
    public abstract CallFrameSymbol? Symbolicate(CallFrame frame);
}
