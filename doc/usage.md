# Usage

Broadly speaking, Ruptura's packages can be split into three different
categories of functionality:

* [Vezel.Ruptura.Injection](https://www.nuget.org/packages/Vezel.Ruptura.Injection)
  and
  [Vezel.Ruptura.Hosting](https://www.nuget.org/packages/Vezel.Ruptura.Hosting)
  provide the ability to inject the CoreCLR runtime and managed assemblies into
  a target process.
* [Vezel.Ruptura.Memory](https://www.nuget.org/packages/Vezel.Ruptura.Memory)
  provides function hooking, memory manipulation, and call tracing capabilities.
* [Vezel.Ruptura.System](https://www.nuget.org/packages/Vezel.Ruptura.System)
  provides managed wrappers around operating system APIs and kernel objects.

You can pick and choose which of these packages you would like to use. For
example, if you already have a working solution for injection, you may want to
just use **Vezel.Ruptura.Memory** for its function hooking APIs, or
**Vezel.Ruptura.System** for convenient Win32 API access. You could also use
**Vezel.Ruptura.Injection** and **Vezel.Ruptura.Hosting** to inject your managed
assembly, but choose to implement your own function hooking method.

Either way, simply use `dotnet add package <name>` to add the relevant
package(s) to your project and start writing code. No further configuration is
required.
