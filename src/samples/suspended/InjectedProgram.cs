sealed class InjectedProgram : IInjectedProgram
{
    static readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    public static async Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args)
    {
        if (context.IsInjected)
            return 42;

        Console.WriteLine("Starting conhost.exe suspended...");

        using var target = TargetProcess.Create("conhost.exe", string.Empty, null, true);
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
