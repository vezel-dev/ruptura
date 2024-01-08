[SuppressMessage("", "CA1812")] // TODO: https://github.com/dotnet/roslyn-analyzers/issues/6218
internal sealed class InjectedProgram : IInjectedProgram
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    public static async Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args)
    {
        if (context.InjectorProcessId != null)
        {
            // This should cause the WaitForInputIdle below to complete.
            context.WakeUp();

            return 42;
        }

        Console.WriteLine("Starting conhost.exe suspended...");

        using var target = TargetProcess.Create("conhost.exe", string.Empty, workingDirectory: null, suspended: true);
        using var proc = Process.GetProcessById(target.Id);
        using var injector = new AssemblyInjector(
                target,
                new AssemblyInjectorOptions(typeof(InjectedProgram).Assembly.Location)
                    .WithInjectionTimeout(_timeout)
                    .WithCompletionTimeout(_timeout));

        try
        {
            Console.WriteLine("Started as {0}. Injecting...", target.Id);

            await injector.InjectAssemblyAsync();

            Console.WriteLine("Injected. Waiting for idle...");

            // This should complete when the WakeUp call above happens.
            if (!proc.WaitForInputIdle(_timeout))
                throw new TimeoutException();

            Console.WriteLine("Now idle. Waiting...");

            var code = await injector.WaitForCompletionAsync();

            Console.WriteLine("Returned code {0}.", code);

            return code == 42 ? 0 : 1;
        }
        finally
        {
            proc.Kill(entireProcessTree: true);

            await proc.WaitForExitAsync();
        }
    }
}
