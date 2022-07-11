namespace Vezel.Ruptura.Memory;

public sealed class InjectedNativeModule
{
    [StructLayout(LayoutKind.Sequential)]
    struct RupturaModuleParameters
    {
        // Keep in sync with src/module/main.c.

        public nuint Size;

        public uint InjectorProcessId;

        public nint ModuleHandle;
    }

    public static InjectedNativeModule Instance { get; private set; } = new(0);

    public nint ModuleHandle { get; }

    InjectedNativeModule(nint moduleHandle)
    {
        ModuleHandle = moduleHandle;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe uint Initialize(void* parameter)
    {
        // Only meant to be called by the native module.

        var state = (RupturaModuleParameters*)parameter;

        Debug.Assert(
            state->Size == (uint)sizeof(RupturaModuleParameters),
            "Managed/unmanaged ruptura_module_parameters size mismatch.");

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(
                $"ruptura-{state->InjectorProcessId}-{Environment.ProcessId}");
            using var accessor = mmf.CreateViewAccessor(0, sizeof(bool) * 2, MemoryMappedFileAccess.Write);

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
