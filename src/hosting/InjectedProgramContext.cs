namespace Vezel.Ruptura.Hosting;

public sealed class InjectedProgramContext
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RupturaState
    {
        // Keep in sync with src/module/main.c.

        public nuint Size;

        public uint InjectorProcessId;

        public uint MainThreadId;

        public nint ModuleHandle;
    }

    public static InjectedProgramContext Instance { get; private set; } =
        new(injectorProcessId: null, mainThreadId: 0, moduleHandle: 0);

    public int? InjectorProcessId { get; }

    public nint ModuleHandle { get; }

    private readonly uint _mainThreadId;

    private InjectedProgramContext(int? injectorProcessId, uint mainThreadId, nint moduleHandle)
    {
        InjectorProcessId = injectorProcessId;
        _mainThreadId = mainThreadId;
        ModuleHandle = moduleHandle;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [SuppressMessage("", "CA1031")]
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint Initialize(void* parameter)
    {
        // Only meant to be called by the native module.

        var state = (RupturaState*)parameter;

        Debug.Assert(state->Size == (uint)sizeof(RupturaState), "Managed/unmanaged ruptura_state size mismatch.");

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(
                $"ruptura-{state->InjectorProcessId}-{Environment.ProcessId}");
            using var accessor = mmf.CreateViewAccessor(0, sizeof(bool), MemoryMappedFileAccess.Write);

            accessor.Write(0, true);

            Instance = new((int)state->InjectorProcessId, state->MainThreadId, state->ModuleHandle);
        }
        catch (Exception ex)
        {
            return (uint)ex.HResult;
        }

        return 0;
    }

    public void WakeUp()
    {
        Check.Operation(_mainThreadId != 0, $"This process was not created suspended.");

        using var thread = ThreadObject.OpenId((int)_mainThreadId, access: null);

        Check.Operation(thread.Resume() != 0, $"The process appears to have been resumed already.");
    }
}
