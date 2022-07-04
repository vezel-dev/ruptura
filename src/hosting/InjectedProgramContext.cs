using Windows.Win32.System.Threading;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Hosting;

public sealed unsafe class InjectedProgramContext
{
    [StructLayout(LayoutKind.Sequential)]
    struct RupturaState
    {
        // Keep in sync with src/module/main.c.

        public nuint Size;

        public uint InjectorProcessId;

        public uint MainThreadId;

        public nint ModuleHandle;
    }

    public static InjectedProgramContext Instance { get; private set; } = new(null, 0, 0);

    public int? InjectorProcessId { get; }

    readonly uint _mainThreadId;

    // TODO: Do we need to expose any native functionality from the module?
    readonly nint _moduleHandle;

    InjectedProgramContext(int? injectorProcessId, uint mainThreadId, nint moduleHandle)
    {
        InjectorProcessId = injectorProcessId;
        _mainThreadId = mainThreadId;
        _moduleHandle = moduleHandle;
    }

    [SuppressMessage("", "IDE0051")]
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static uint Initialize(RupturaState* state)
    {
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
        _ = _mainThreadId != 0 ? true : throw new InvalidOperationException("This process was not created suspended.");

        using var thread = OpenThread_SafeHandle(THREAD_ACCESS_RIGHTS.THREAD_SUSPEND_RESUME, false, _mainThreadId);

        if (thread.IsInvalid)
            throw new Win32Exception();

        var ret = ResumeThread(thread);

        if (ret == uint.MaxValue)
            throw new Win32Exception();

        if (ret == 0)
            throw new InvalidOperationException("The process appears to have been resumed already.");
    }
}
