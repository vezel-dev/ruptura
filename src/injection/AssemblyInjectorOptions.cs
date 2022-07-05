namespace Vezel.Ruptura.Injection;

public sealed class AssemblyInjectorOptions
{
    public string FileName { get; private set; } = null!;

    public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();

    public string ModuleDirectory { get; private set; } =
        Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location is var location and not ""
                ? location
                : Environment.ProcessPath)
            ?? Environment.CurrentDirectory;

    public TimeSpan InjectionTimeout { get; private set; } = Timeout.InfiniteTimeSpan;

    public TimeSpan CompletionTimeout { get; private set; } = Timeout.InfiniteTimeSpan;

    AssemblyInjectorOptions()
    {
    }

    public AssemblyInjectorOptions(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        FileName = fileName;
    }

    AssemblyInjectorOptions Clone()
    {
        return new()
        {
            FileName = FileName,
            Arguments = Arguments,
            ModuleDirectory = ModuleDirectory,
            InjectionTimeout = InjectionTimeout,
            CompletionTimeout = CompletionTimeout,
        };
    }

    public AssemblyInjectorOptions WithFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var options = Clone();

        options.FileName = fileName;

        return options;
    }

    public AssemblyInjectorOptions WithArguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        _ = arguments.All(s => s != null) ? true : throw new ArgumentException(null, nameof(arguments));

        var options = Clone();

        options.Arguments = arguments.ToArray();

        return options;
    }

    public AssemblyInjectorOptions WithModuleDirectory(string moduleDirectory)
    {
        ArgumentNullException.ThrowIfNull(moduleDirectory);

        var options = Clone();

        options.ModuleDirectory = moduleDirectory;

        return options;
    }

    public AssemblyInjectorOptions WithInjectionTimeout(TimeSpan timeout)
    {
        _ = (long)timeout.TotalMilliseconds is >= -1 and <= int.MaxValue ?
            true : throw new ArgumentOutOfRangeException(nameof(timeout));

        var options = Clone();

        options.InjectionTimeout = timeout;

        return options;
    }

    public AssemblyInjectorOptions WithCompletionTimeout(TimeSpan timeout)
    {
        _ = (long)timeout.TotalMilliseconds is >= -1 and <= int.MaxValue ?
            true : throw new ArgumentOutOfRangeException(nameof(timeout));

        var options = Clone();

        options.CompletionTimeout = timeout;

        return options;
    }
}
