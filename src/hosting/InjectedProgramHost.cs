namespace Vezel.Ruptura.Hosting;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class InjectedProgramHost
{
    // This class is only meant to be used by generated code.

    public static Task<int> RunAsync<TProgram>(ReadOnlyMemory<string> args)
        where TProgram : IInjectedProgram
    {
        return TProgram.RunAsync(InjectedProgramContext.Instance, args);
    }
}
