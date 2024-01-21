# Home

**Ruptura** provides a set of libraries that make it easy to inject a managed
.NET assembly into arbitrary Windows (and Wine) processes for the purposes of
function hooking and memory manipulation.

**Ruptura** injects a bundled native module into the target process, which then
locates the .NET runtime in either framework-dependent or self-contained mode
and initializes it, after which a user-specified managed assembly is executed -
all without the user writing a single line of native code. Additionally, a
library facilitating common function hooking and memory manipulation scenarios
is available for use by the injected assembly.

## Features

Here are some of the **Ruptura** highlights:

* **Easy injection and hosting:** Just point **Ruptura** to a target process and
  give it the name of a managed assembly to inject. All the complexity of
  injecting the CoreCLR runtime libraries and loading your assembly is handled
  for you.
* **Create or attach to processes:** You can inject into existing processes or
  create them yourself. When creating a target process, it can be started in a
  suspended state, giving your injected assembly full control over when and how
  the target process starts running.
* **Native function hooks:** Hooking APIs allow you to intercept calls to
  arbitrary native functions in a process. You can inspect or modify arguments
  and return values as needed, and it is up to you whether the original function
  is called at all.
* **Hook state access:** A state object can be associated with a hook, giving
  you easy access to your managed application state from within a hook function,
  where it is ordinarily difficult to access non-global state.
* **Hook error prevention:** A 'hook gate' prevents common programming errors
  (deadlocks, stack overflows, etc) by avoiding execution of hook functions when
  the Windows loader lock is held, or when the hook is already executing earlier
  in the call stack.
* **Call stack tracing:** Call stack traces can be collected at any point,
  giving you insight into how the target process behaves. These traces contain
  highly detailed information both for managed and unmanaged stack frames.
* **Operating system interop:** A set of convenient classes and functions for
  accessing Win32 APIs and kernel objects are provided, which **Ruptura** makes
  heavy use of for its own functionality.
* **Publishing modes:** You can publish your **Ruptura**-based application in
  either
  [framework-dependent](https://docs.microsoft.com/en-us/dotnet/core/deploying/#publish-framework-dependent)
  or
  [self-contained](https://docs.microsoft.com/en-us/dotnet/core/deploying/#publish-self-contained)
  mode. There is also partial support for
  [trimming](https://docs.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained).

All of these features make **Ruptura** a great choice for a wide variety of
tasks such as closed-source modding, interoperability research, advanced
diagnostics, security research, anti-tampering, etc.

## Limitations

There are some notable limitations and caveats to be aware of when using **Ruptura**:

* Only modern versions of Windows 10 and 11 are supported due to certain native
  APIs used for function hooking and call tracing.
* There is currently only support for injecting x64 processes. x86 support is
  [in the works](https://github.com/vezel-dev/ruptura/issues/5).
* The CoreCLR runtime is never unloaded from the target process due to
  limitations in the hosting APIs for .NET 7+. This means that you cannot inject
  into the same process more than once for the duration of its lifetime.
