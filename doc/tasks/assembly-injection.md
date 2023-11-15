# Assembly Injection

Before getting started, you must decide whether you wish to implement your
*injection* code and *injected* code in the same project or in two separate
projects. Using a single project is easiest, but separate projects may be
desirable if having the injection code in the injected assembly would lead to
too many assemblies being pointlessly loaded into the target process.

## Single-Project Setup

For a single-project setup, your project file should look something like:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <TargetFramework>net8.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Vezel.Ruptura.Hosting"
                          Version="x.y.z" />
        <PackageReference Include="Vezel.Ruptura.Injection"
                          Version="x.y.z" />
    </ItemGroup>
</Project>
```

(Replace `x.y.z` with the actual NuGet package version.)

Your entry point can then be implemented using the `IInjectedProgram` interface:

```csharp
sealed class InjectedProgram : IInjectedProgram
{
    public static async Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args)
    {
        if (context.InjectorProcessId != null)
            return 42;

        using var target = TargetProcess.Open(int.Parse(args.Span[0]));
        using var injector = new AssemblyInjector(
            target, new AssemblyInjectorOptions(typeof(InjectedProgram).Assembly.Location));

        await injector.InjectAssemblyAsync();

        return await injector.WaitForCompletionAsync() == 42 ? 0 : 1;
    }
}
```

(Note that, when using **Vezel.Ruptura.Hosting**, you should not implement a
regular `Main` entry point; the `IInjectedProgram` implementation will be used
instead.)

The above example injects itself into a given target process's ID. The injected
code just returns the exit code `42` immediately, which the injection code
verifies. In general, checking the `InjectedProgramContext.InjectorProcessId`
property for a non-`null` value is the way to know if you are running in the
target process.

## Multi-Project Setup

For a multi-project setup, the project file for the injector program should look
like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <TargetFramework>net8.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Vezel.Ruptura.Injection"
                          Version="x.y.z" />
    </ItemGroup>
</Project>
```

The injector entry point can be implemented normally:

```csharp
static class Program
{
    static async Task<int> Main(string[] args)
    {
        using var target = TargetProcess.Open(int.Parse(args[0]));
        using var injector = new AssemblyInjector(target, new AssemblyInjectorOptions(args[1]));

        await injector.InjectAssemblyAsync();

        return await injector.WaitForCompletionAsync() == 42 ? 0 : 1;
    }
}
```

The project file for the injected program should look like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <TargetFramework>net8.0</TargetFramework>
        <UseAppHost>false</UseAppHost>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Vezel.Ruptura.Hosting"
                          Version="x.y.z" />
    </ItemGroup>
</Project>
```

Note that the injected assembly must have `OutputType` set to `Exe` too, since
the .NET hosting APIs will look for an executable program entry point. Despite
this, a native executable is not needed for the injected assembly, so
`UseAppHost` can safely be set to `false` to cut down on build time and publish
size.

The entry point for the injected program is implemented with the
`IInjectedProgram` interface:

```csharp
sealed class InjectedProgram : IInjectedProgram
{
    public static Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args)
    {
        return Task.FromResult(42);
    }
}
```

This example does the same thing as the single-project example, but here, you
pass the file name of the injected program to the injection program in addition
to the target process's ID. There are ways that you can avoid that requirement,
e.g. by referencing the injected program project from the injection program
project and using `typeof(InjectedProgram).Assembly.Location`.

## Process Creation

In addition to injecting existing processes, you can also create new ones. This
is mainly useful because it lets you start a process in a suspended state:

```csharp
sealed class InjectedProgram : IInjectedProgram
{
    public static async Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args)
    {
        if (context.InjectorProcessId != null)
        {
            context.WakeUp();

            return 42;
        }

        using var target = TargetProcess.Create(args.Span[0], string.Empty, null, suspended: true);
        using var injector = new AssemblyInjector(
            target, new AssemblyInjectorOptions(typeof(InjectedProgram).Assembly.Location));

        await injector.InjectAssemblyAsync();

        return await injector.WaitForCompletionAsync() == 42 ? 0 : 1;
    }
}
```

The `InjectedProgramContext.WakeUp()` call resumes the process's main thread.
Having control over when the main thread starts executing gives you a window of
opportunity to perform setup work such as installing function hooks.

## Process Lifetime

It is worth noting that the target process's lifetime is completely unaffected
by the lifetime of your injected program. Even if you return from your
`IInjectedProgram.RunAsync()` implementation and you have no background work
running, the target process will still continue running. If you need the target
process to exit at the same time as your injected program, it is your
responsibility to make that happen.

Regardless of the above, the CoreCLR runtime will stay loaded in the process
until it exits, due to limitations in the .NET hosting APIs. This means that you
cannot inject into the same process more than once for the duration of its
lifetime. If you need to reload managed code in the target process, you can use
[`AssemblyLoadContext`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext)
to do so. The
[McMaster.NETCore.Plugins](https://github.com/natemcmaster/DotNetCorePlugins)
library makes this particularly easy.

## Interprocess Communication

Ruptura does not provide a mechanism for communication between the injector
process and the injected process. A rudimentary way of passing arguments to the
injected program exists in the form of the
`AssemblyInjectorOptions.WithArguments()` method, however.

One simple and type-safe way to achieve IPC between the two processes would be
to combine either
[anonymous pipes](https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-use-anonymous-pipes-for-local-interprocess-communication)
(one-way) or
[named pipes](https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication)
(one-way or two-way) with the
[StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc) library.
