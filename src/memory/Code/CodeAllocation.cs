namespace Vezel.Ruptura.Memory.Code;

[SuppressMessage("", "CA1063")]
public abstract unsafe class CodeAllocation : IDisposable
{
    public CodeManager Manager { get; }

    public abstract void* Code { get; }

    public abstract nint Length { get; }

    protected CodeAllocation(CodeManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        Manager = manager;
    }

    public abstract void Dispose();

    public abstract void Commit();

    public abstract void Decommit();
}
