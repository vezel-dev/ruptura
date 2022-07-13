namespace Vezel.Ruptura.Memory;

public sealed unsafe class InjectedNativeModule
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

    public nint ModuleHandle => _moduleHandle;

    readonly delegate* unmanaged[Cdecl]<void*, out void*, out void*, out void*, void> _extractContext;

    volatile nint _moduleHandle;

    InjectedNativeModule(nint moduleHandle)
    {
        if (moduleHandle == 0)
        {
            moduleHandle = NativeLibrary.Load(
                $"ruptura-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}.dll");

            // TODO: Is there a better place we can do this?
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                var handle = _moduleHandle;

                _moduleHandle = 0;

                NativeLibrary.Free(handle);
            };
        }

        _moduleHandle = moduleHandle;

        nint GetExport(string name)
        {
            return NativeLibrary.GetExport(moduleHandle, name);
        }

        _extractContext =
            (delegate* unmanaged[Cdecl]<void*, out void*, out void*, out void*, void>)GetExport(
                "ruptura_helper_extract_context");
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static uint Initialize(void* parameter)
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

    internal void ExtractContext(void* context, out void* ip, out void* sp, out void* fp)
    {
        _extractContext(context, out ip, out sp, out fp);
    }
}
