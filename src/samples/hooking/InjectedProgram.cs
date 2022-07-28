[SuppressMessage("", "CA1812")]
internal sealed class InjectedProgram : IInjectedProgram
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    private static int _counter;

    [SuppressMessage("", "CA1031")]
    public static async Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args)
    {
        if (context.InjectorProcessId != null)
        {
            try
            {
                return TestHooking();
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(args.Span[0], ex.ToString());

                return 42;
            }
        }

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

            var tempFile = Path.GetTempFileName();

            using var target = TargetProcess.Open(proc.Id);
            using var injector = new AssemblyInjector(
                target,
                new AssemblyInjectorOptions(typeof(InjectedProgram).Assembly.Location)
                    .WithArguments(new[] { tempFile })
                    .WithInjectionTimeout(_timeout)
                    .WithCompletionTimeout(_timeout));

            await injector.InjectAssemblyAsync();

            Console.WriteLine("Injected. Waiting...");

            var counter = await injector.WaitForCompletionAsync();

            Console.WriteLine("Returned counter {0}.", counter);

            if (new FileInfo(tempFile).Length != 0)
                Console.WriteLine(await File.ReadAllTextAsync(tempFile));

            return counter == 1 ? 0 : 1;
        }
        finally
        {
            proc.Kill(true);

            await proc.WaitForExitAsync();
        }
    }

    private static unsafe int TestHooking()
    {
        var lib = NativeLibrary.Load("kernel32.dll");

        try
        {
            var func = (delegate* unmanaged[Stdcall]<void>)NativeLibrary.GetExport(lib, "FlushProcessWriteBuffers");

            using var manager = new PageCodeManager();

            using var hook = FunctionHook.Create(
                manager, func, (delegate* unmanaged[Stdcall]<void>)&FlushProcessWriteBuffersHook);

            func();

            hook.IsActive = true;

            func();

            hook.IsActive = false;

            func();

            return _counter;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe void FlushProcessWriteBuffersHook()
    {
        _counter++;

        ((delegate* unmanaged[Stdcall]<void>)FunctionHook.Current.OriginalCode)();
    }
}
