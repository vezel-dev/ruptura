# Ruptura

**Ruptura** provides a set of libraries that make it easy to inject a managed
.NET assembly into arbitrary Windows processes for the purposes of function
hooking and memory manipulation.

**Ruptura** injects a bundled native module into the target process, which then
locates the .NET runtime in either framework-dependent or self-contained mode
and initializes it, after which a user-specified managed assembly is executed -
all without the user writing a single line of native code. Additionally, a
library facilitating common function hooking and memory manipulation scenarios
is available for use by the injected assembly.

This project offers the following packages:

* [Vezel.Ruptura.Hosting](https://www.nuget.org/packages/Vezel.Ruptura.Hosting):
  Provides the hosting model for injected programs.
* [Vezel.Ruptura.Injection](https://www.nuget.org/packages/Vezel.Ruptura.Injection):
  Provides the infrastructure to inject the .NET runtime and assemblies into
  processes.
* [Vezel.Ruptura.Memory](https://www.nuget.org/packages/Vezel.Ruptura.Memory):
  Provides function hooking and memory manipulation utilities for use by
  injected programs.

For more information, please visit the
[project home page](https://docs.vezel.dev/ruptura).
