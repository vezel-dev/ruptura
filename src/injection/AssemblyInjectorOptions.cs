namespace Vezel.Ruptura.Injection;

public sealed class AssemblyInjectorOptions
{
    public string FileName { get; private set; } = null!;

    public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();

    // TODO: https://github.com/dotnet/Nerdbank.GitVersioning/issues/555
#pragma warning disable CS0436
    [UnconditionalSuppressMessage("", "IL3000")]
    public string ModuleDirectory { get; private set; } =
        Path.GetDirectoryName(
            typeof(ThisAssembly).Assembly.Location is var location and not ""
                ? location
                : Environment.ProcessPath) ?? Environment.CurrentDirectory;
#pragma warning restore CS0436

    public TimeSpan InjectionTimeout { get; private set; } = Timeout.InfiniteTimeSpan;

    public TimeSpan CompletionTimeout { get; private set; } = Timeout.InfiniteTimeSpan;

    private AssemblyInjectorOptions()
    {
    }

    public AssemblyInjectorOptions(string fileName)
    {
        Check.Null(fileName);

        FileName = fileName;
    }

    private AssemblyInjectorOptions Clone()
    {
        return new()
        {
            FileName = FileName,
            Arguments = Arguments.ToArray(),
            ModuleDirectory = ModuleDirectory,
            InjectionTimeout = InjectionTimeout,
            CompletionTimeout = CompletionTimeout,
        };
    }

    public AssemblyInjectorOptions WithFileName(string fileName)
    {
        Check.Null(fileName);

        var options = Clone();

        options.FileName = fileName;

        return options;
    }

    [SuppressMessage("", "CA1851")]
    public AssemblyInjectorOptions WithArguments(IEnumerable<string> arguments)
    {
        Check.Null(arguments);
        Check.All(arguments, static arg => arg != null);

        var options = Clone();

        options.Arguments = arguments.ToArray();

        return options;
    }

    public AssemblyInjectorOptions WithModuleDirectory(string moduleDirectory)
    {
        Check.Null(moduleDirectory);

        var options = Clone();

        options.ModuleDirectory = moduleDirectory;

        return options;
    }

    public AssemblyInjectorOptions WithInjectionTimeout(TimeSpan timeout)
    {
        Check.Range((long)timeout.TotalMilliseconds is >= -1 and <= int.MaxValue, timeout);

        var options = Clone();

        options.InjectionTimeout = timeout;

        return options;
    }

    public AssemblyInjectorOptions WithCompletionTimeout(TimeSpan timeout)
    {
        Check.Range((long)timeout.TotalMilliseconds is >= -1 and <= int.MaxValue, timeout);

        var options = Clone();

        options.CompletionTimeout = timeout;

        return options;
    }
}
