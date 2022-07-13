sealed class InjectedProgram : IInjectedProgram
{
    static readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    public static async Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args)
    {
        if (context.InjectorProcessId != null)
            return 42;

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

            var code = await injector.WaitForCompletionAsync();

            Console.WriteLine("Returned code {0}.", code);

            return code == 42 ? 0 : 1;
        }
        finally
        {
            proc.Kill(true);

            await proc.WaitForExitAsync();
        }
    }
}
