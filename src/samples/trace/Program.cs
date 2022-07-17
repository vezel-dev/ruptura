static unsafe class Program
{
    static FunctionHook? _hook;

    static CallTrace? _trace;

    public static int Main()
    {
        var lib = NativeLibrary.Load("kernel32.dll");

        try
        {
            using var manager = new PageCodeManager();

            var func = (delegate* unmanaged[Stdcall]<nint, char*, int>)NativeLibrary.GetExport(
                lib, "SetThreadDescription");

            using (_hook = FunctionHook.Create(
                manager, func, (delegate* unmanaged[Stdcall]<nint, char*, int>)&SetThreadDescriptionHook))
            {
                _hook.IsActive = true;

                _ = func(-1, null);
            }
        }
        finally
        {
            NativeLibrary.Free(lib);
        }

        return _trace?.Frames is { Count: > 20 } && _trace.Frames[0].ManagedMethod?.Name == "SetThreadDescriptionHook"
            ? 0
            : 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static int SetThreadDescriptionHook(nint hThread, char* lpThreadDescription)
    {
        _trace = CallTrace.Capture();

        Console.WriteLine(_trace);

        return 0;
    }
}
