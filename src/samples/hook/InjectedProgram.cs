sealed class InjectedProgram : IInjectedProgram
{
    static readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    static FunctionHook? _hook;

    static int _counter;

    public static async Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args)
    {
        if (context.InjectorProcessId != null)
            return TestHooking();

        Console.WriteLine("Starting conhost.exe...");

        using var proc = new Process
        {
            StartInfo = new()
            {
                FileName = "conhost.exe",
            },
        };

        _ = proc.Start();

        try
        {
            if (!proc.WaitForInputIdle(_timeout))
                throw new TimeoutException();

            Console.WriteLine("Started as {0}. Injecting...", proc.Id);

            using var target = TargetProcess.Open(proc.Id);
            using var injector = new AssemblyInjector(
                target,
                new AssemblyInjectorOptions(typeof(InjectedProgram).Assembly.Location)
                    .WithInjectionTimeout(_timeout)
                    .WithCompletionTimeout(_timeout));

            await injector.InjectAssemblyAsync();

            Console.WriteLine("Injected.");

            var counter = await injector.WaitForCompletionAsync();

            Console.WriteLine("Returned counter {0}.", counter);

            return counter == 1 ? 0 : 1;
        }
        finally
        {
            proc.Kill(true);

            await proc.WaitForExitAsync();
        }
    }

    static unsafe int TestHooking()
    {
        var lib = NativeLibrary.Load("kernel32.dll");

        try
        {
            var func = (delegate* unmanaged[Stdcall]<void>)NativeLibrary.GetExport(lib, "FlushProcessWriteBuffers");

            using var manager = new SimpleCodeManager();

            using (_hook = FunctionHook.Create(
                manager, func, (delegate* unmanaged[Stdcall]<void>)&FlushProcessWriteBuffersHook))
            {
                func();

                _hook.IsActive = true;

                func();

                _hook.IsActive = false;

                func();

                return _counter;
            }
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static unsafe void FlushProcessWriteBuffersHook()
    {
        _counter++;

        ((delegate* unmanaged[Stdcall]<void>)_hook!.TrampolineCode)();
    }
}
