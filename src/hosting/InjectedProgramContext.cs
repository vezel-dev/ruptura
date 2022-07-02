namespace Vezel.Ruptura.Hosting;

public sealed class InjectedProgramContext
{
    [StructLayout(LayoutKind.Sequential)]
    struct RupturaState
    {
        public nint ModuleHandle;

        public uint InjectorProcessId;
    }

    public static InjectedProgramContext Instance { get; private set; } = new(0);

    public bool IsInjected => _moduleHandle != 0;

    // TODO: Do we need to expose any native functionality from the module?
    readonly nint _moduleHandle;

    InjectedProgramContext(nint moduleHandle)
    {
        _moduleHandle = moduleHandle;
    }

    [SuppressMessage("", "IDE0051")]
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static unsafe uint Initialize(RupturaState* state)
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(
                $"ruptura-{state->InjectorProcessId}-{Environment.ProcessId}");
            using var accessor = mmf.CreateViewAccessor(0, sizeof(bool), MemoryMappedFileAccess.Write);

            accessor.Write(0, true);

            Instance = new(state->ModuleHandle);
        }
        catch (Exception ex)
        {
            return (uint)ex.HResult;
        }

        return 0;
    }
}
