// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Injection;

public sealed class AssemblyInjectorOptions
{
    public string FileName { get; private set; } = null!;

    public ImmutableArray<string> Arguments { get; private set; } = [];

    public string ModuleDirectory { get; private set; } = AppContext.BaseDirectory;

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

        var options = Clone();

        options.Arguments = [.. arguments];

        return options;
    }

    public AssemblyInjectorOptions SetArgument(int index, string argument)
    {
        Check.Null(argument);

        var options = Clone();

        options.Arguments = Arguments.SetItem(index, argument);

        return options;
    }

    public AssemblyInjectorOptions InsertArgument(int index, string argument)
    {
        Check.Null(argument);

        var options = Clone();

        options.Arguments = Arguments.Insert(index, argument);

        return options;
    }

    public AssemblyInjectorOptions InsertArguments(int index, params string[] arguments)
    {
        return InsertArguments(index, arguments.AsEnumerable());
    }

    public AssemblyInjectorOptions InsertArguments(int index, IEnumerable<string> arguments)
    {
        Check.Null(arguments);
        Check.All(arguments, static arg => arg != null);

        var options = Clone();

        options.Arguments = Arguments.InsertRange(index, arguments);

        return options;
    }

    public AssemblyInjectorOptions AddArgument(string argument)
    {
        Check.Null(argument);

        var options = Clone();

        options.Arguments = Arguments.Add(argument);

        return options;
    }

    public AssemblyInjectorOptions AddArguments(params string[] arguments)
    {
        return AddArguments(arguments.AsEnumerable());
    }

    public AssemblyInjectorOptions AddArguments(IEnumerable<string> arguments)
    {
        Check.Null(arguments);
        Check.All(arguments, static arg => arg != null);

        var options = Clone();

        options.Arguments = Arguments.AddRange(arguments);

        return options;
    }

    public AssemblyInjectorOptions RemoveArgument(int index)
    {
        var options = Clone();

        options.Arguments = Arguments.RemoveAt(index);

        return options;
    }

    public AssemblyInjectorOptions RemoveArguments(int index, int count)
    {
        var options = Clone();

        options.Arguments = Arguments.RemoveRange(index, count);

        return options;
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
