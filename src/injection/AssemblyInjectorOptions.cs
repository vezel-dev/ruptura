namespace Vezel.Ruptura.Injection;

public sealed class AssemblyInjectorOptions
{
    public string FileName { get; private set; } = null!;

    public ImmutableArray<string> Arguments { get; private set; } = ImmutableArray<string>.Empty;

    public string ModuleDirectory { get; private set; } = _defaultDirectory;

    public TimeSpan InjectionTimeout { get; private set; } = Timeout.InfiniteTimeSpan;

    public TimeSpan CompletionTimeout { get; private set; } = Timeout.InfiniteTimeSpan;

    private static readonly string _defaultDirectory;

    [SuppressMessage("", "CA1810")]
    [UnconditionalSuppressMessage("", "IL3000")]
    static AssemblyInjectorOptions()
    {
        // TODO: https://github.com/dotnet/Nerdbank.GitVersioning/issues/555
#pragma warning disable CS0436
        var location = typeof(ThisAssembly).Assembly.Location;
#pragma warning restore CS0436

        _defaultDirectory = Path.GetDirectoryName(location.Length != 0 ? location : Environment.ProcessPath) ??
            Environment.CurrentDirectory;
    }

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
            Arguments = Arguments,
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

    public AssemblyInjectorOptions WithArguments(params string[] arguments)
    {
        return WithArguments(arguments.AsEnumerable());
    }

    public AssemblyInjectorOptions WithArguments(IEnumerable<string> arguments)
    {
        Check.Null(arguments);
        Check.All(arguments, static arg => arg != null);

        var builder = Clone();

        builder.Arguments = arguments.ToImmutableArray();

        return builder;
    }

    public AssemblyInjectorOptions AddArgument(string argument)
    {
        Check.Null(argument);

        var builder = Clone();

        builder.Arguments = Arguments.Add(argument);

        return builder;
    }

    public AssemblyInjectorOptions AddArguments(params string[] arguments)
    {
        return AddArguments(arguments.AsEnumerable());
    }

    public AssemblyInjectorOptions AddArguments(IEnumerable<string> arguments)
    {
        Check.Null(arguments);
        Check.All(arguments, static arg => arg != null);

        var builder = Clone();

        builder.Arguments = Arguments.AddRange(arguments);

        return builder;
    }

    public AssemblyInjectorOptions RemoveArgument(int index)
    {
        var builder = Clone();

        builder.Arguments = Arguments.RemoveAt(index);

        return builder;
    }

    public AssemblyInjectorOptions RemoveArguments(int index, int count)
    {
        var builder = Clone();

        builder.Arguments = Arguments.RemoveRange(index, count);

        return builder;
    }

    public AssemblyInjectorOptions ClearArguments()
    {
        return WithArguments();
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
