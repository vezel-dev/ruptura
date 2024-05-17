// SPDX-License-Identifier: 0BSD

internal static unsafe class Program
{
    private static CallTrace? _trace;

    private static string? _result;

    public static int Main()
    {
        var lib = NativeLibrary.Load("kernel32.dll");

        try
        {
            using var manager = new PageCodeManager();

            var func = (delegate* unmanaged[Stdcall]<nint, char*, int>)NativeLibrary.GetExport(
                lib, "SetThreadDescription");

            using var hook = FunctionHook.Create(
                manager, func, (delegate* unmanaged[Stdcall]<nint, char*, int>)&SetThreadDescriptionHook, "foo");

            hook.IsActive = true;

            fixed (char* ptr = "bar")
                return func(-1, ptr) == 0 &&
                    _result == "foobar" &&
                    _trace?.Frames is { Count: > 20 } &&
                    _trace.Frames[0].ManagedMethod?.Name == "CaptureTrace" ? 0 : 1;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    [SuppressMessage("", "CA1031")]
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int SetThreadDescriptionHook(nint hThread, char* lpThreadDescription)
    {
        try
        {
            _result = FunctionHook.Current.State + new string(lpThreadDescription);

            // Ensure that we can capture a trace involving DynamicMethod frames.

            var method = new DynamicMethod("CaptureTraceWrapper", typeof(void), Type.EmptyTypes, typeof(Program));
            var cil = method.GetILGenerator();

            cil.EmitCall(
                OpCodes.Call,
                typeof(Program).GetMethod(
                    nameof(CaptureTrace),
                    BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.NonPublic)!,
                optionalParameterTypes: null);
            cil.Emit(OpCodes.Ret);

            _ = method.Invoke(obj: null, parameters: null);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

            return 1;
        }

        return 0;
    }

    private static void CaptureTrace()
    {
        _trace = CallTrace.Capture();

        Console.WriteLine(_trace);
    }
}
