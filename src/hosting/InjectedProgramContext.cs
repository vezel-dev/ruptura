using Vezel.Ruptura.System;

namespace Vezel.Ruptura.Hosting;

public sealed class InjectedProgramContext
{
    [StructLayout(LayoutKind.Sequential)]
    struct RupturaContextParameters
    {
        // Keep in sync with src/module/main.c.

        public nuint Size;

        public uint InjectorProcessId;

        public uint MainThreadId;
    }

    public static InjectedProgramContext Instance { get; private set; } = new(null, 0);

    public int? InjectorProcessId { get; }

    readonly uint _mainThreadId;

    InjectedProgramContext(int? injectorProcessId, uint mainThreadId)
    {
        InjectorProcessId = injectorProcessId;
        _mainThreadId = mainThreadId;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe uint Initialize(void* parameter)
    {
        // Only meant to be called by the native module.

        var state = (RupturaContextParameters*)parameter;

        Debug.Assert(
            state->Size == (uint)sizeof(RupturaContextParameters),
            "Managed/unmanaged ruptura_context_parameters size mismatch.");

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(
                $"ruptura-{state->InjectorProcessId}-{Environment.ProcessId}");
            using var accessor = mmf.CreateViewAccessor(0, sizeof(bool) * 2, MemoryMappedFileAccess.Write);

            accessor.Write(1, true);

            Instance = new((int)state->InjectorProcessId, state->MainThreadId);
        }
        catch (Exception ex)
        {
            return (uint)ex.HResult;
        }

        return 0;
    }

    public void WakeUp()
    {
        _ = _mainThreadId != 0 ? true : throw new InvalidOperationException("This process was not created suspended.");

        using var thread = ThreadObject.OpenId((int)_mainThreadId);

        if (thread.Resume() == 0)
            throw new InvalidOperationException("The process appears to have been resumed already.");
    }
}
