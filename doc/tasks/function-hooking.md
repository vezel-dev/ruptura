# Function Hooking

The **Vezel.Ruptura.Memory** package provides an easy-to-use `FunctionHook`
class that takes care of some common requirements such as placing the trampoline
near the target function, atomically toggling an installed hook, associating
state with the hook function, and avoiding certain cases of deadlock or stack
overflow.

Usage looks like this:

```csharp
var kernel32 = NativeLibrary.Load("kernel32.dll");

try
{
    using var manager = new PageCodeManager();

    var setThreadDescription = (delegate* unmanaged[Stdcall]<nint, char*, int>)NativeLibrary.GetExport(
        kernel32, "SetThreadDescription");
    var setThreadDescriptionHook = (delegate* unmanaged[Stdcall]<nint, char*, int>)&SetThreadDescriptionHook;

    using var hook = FunctionHook.Create(manager, setThreadDescription, setThreadDescriptionHook, "bar");

    hook.IsActive = true;

    fixed (char* ptr = "foo")
        _ = setThreadDescription(-1, ptr);
}
finally
{
    NativeLibrary.Free(kernel32);
}

[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
static int SetThreadDescriptionHook(nint hThread, char* lpThreadDescription)
{
    Console.WriteLine(new string(lpThreadDescription) + FunctionHook.Current.State);

    return 0;
}
```

This example will print `foobar` to the console. There is a lot going on here,
so let us go over each part:

1. The
   [`NativeLibrary`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.nativelibrary)
   API is used to retrieve a function pointer to the
   [`SetThreadDescription`](https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreaddescription)
   function.
2. A `PageCodeManager` instance is created to manage code allocations. This is
   used by `FunctionHook` to allocate its internal trampoline near the target
   function.
3. `FunctionHook.Create()` is called to hook `SetThreadDescription` with the
   `SetThreadDescriptionHook` method. The string object `"bar"` is passed as the
   hook's associated state so that it can be accessed in the hook method.
4. A created hook is fully functional with all code emission and patching done,
   but calls will not actually be intercepted until the `IsActive` property is
   set to `true`, so that is then done.
5. The function pointer for the target function is invoked with some dummy
   arguments. Since the hook is active, the `SetThreadDescriptionHook` method
   will be called.
6. `SetThreadDescriptionHook` accesses the `FunctionHook.Current` property and
   retrieves its `State` property (i.e. the `"bar"` string passed earlier). It
   concatenates it with the string passed in as `lpThreadDescription` and prints
   the result (`"foobar"`) to the console.

This example is fairly contrived, but you can hook almost any function, whether
it comes from the operating system, a normal library, or an executable. Of
course, you can also associate more useful state objects with the hook so that
you can access your application's state.

It is important that the hook method matches the target function exactly in
calling convention (`cdecl`, `stdcall`, etc), return type, and parameter types.
Mismatches can result in unpredictable bugs and crashes. Also, managed
exceptions thrown from within a hook method *must* be handled; native call
frames cannot be correctly unwound by CoreCLR.

## Hook Gate

Internally, `FunctionHook` uses a so-called hook gate as part of its trampoline
to guard calls to the hook method. The most user-visible effects of this are:

* The `FunctionHook` instance for the most recent hook method in the call stack
  can be accessed through the `Current` property.
* Hook recursion does not invoke the hook method a second time, preventing
  common issues like deadlocks and stack overflows when calling arbitrary code
  in a hook.
* Hooks are not invoked while the Windows loader lock is held since managed code
  is virtually impossible to run reliably when that is the case.
