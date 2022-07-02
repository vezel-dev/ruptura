# Home

**Ruptura** provides a set of libraries that make it easy to inject a managed
.NET assembly into arbitrary Windows processes for the purposes of function
hooking and memory manipulation.

**Ruptura** injects a bundled native module into the target process, which then
locates the .NET runtime in either framework-dependent or self-contained mode
and initializes it, after which a user-specified managed assembly is executed -
all without the user writing a single line of native code. Additionally, a
library facilitating common function hooking and memory manipulation scenarios
is available for use by the injected assembly.
