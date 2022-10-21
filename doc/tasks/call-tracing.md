# Call Tracing

When trying to understand closed-source binaries or collecting diagnostic data,
it can be useful to capture a detailed call stack trace at any given point -
especially from a function hook. The **Vezel.Ruptura.Memory** package provides
the `CallTrace` API to do exactly that. For example:

```csharp
Console.WriteLine(CallTrace.Capture());
```

This will print something like:

```text
0x7ffe4cfa0b09: Void Program.<Main>$(String[] args) in tests.dll
0x7ffeacabe823: coreclr_shutdown_2+0x16683 in coreclr.dll+0x16e823
0x7ffeac99a133: <unknown> in coreclr.dll+0x4a133
0x7ffeaca2d710: <unknown> in coreclr.dll+0xdd710
0x7ffeaca2efb6: <unknown> in coreclr.dll+0xdefb6
0x7ffeaca2f749: <unknown> in coreclr.dll+0xdf749
0x7ffeaca2dcb9: <unknown> in coreclr.dll+0xddcb9
0x7ffeaca93bd1: coreclr_execute_assembly+0xe1 in coreclr.dll+0x143bd1
0x7fff31f69148: <unknown> in hostpolicy.dll+0x19148
0x7fff31f6941c: <unknown> in hostpolicy.dll+0x1941c
0x7fff31f69d17: corehost_main+0x107 in hostpolicy.dll+0x19d17
0x7fff3947b459: hostfxr_close+0xfb9 in hostfxr.dll+0xb459
0x7fff3947e4f6: hostfxr_close+0x4056 in hostfxr.dll+0xe4f6
0x7fff394807cf: hostfxr_close+0x632f in hostfxr.dll+0x107cf
0x7fff3947eb52: hostfxr_close+0x46b2 in hostfxr.dll+0xeb52
0x7fff394781cb: hostfxr_main_startupinfo+0xab in hostfxr.dll+0x81cb
0x7ff61aba255b: <unknown> in tests.exe+0x1255b
0x7ff61aba28cb: <unknown> in tests.exe+0x128cb
0x7ff61aba3d78: <unknown> in tests.exe+0x13d78
0x7fff79193fed: BaseThreadInitThunk+0x1d in kernel32.dll+0x13fed
0x7fff7a6142a8: RtlUserThreadStart+0x28 in ntdll.dll+0x42a8
```

Of course, the captured `CallTrace` object has plenty of details that you can
inspect programmatically. For example, you could print a bit more information
about the `RIP`, `RSP`, and `RBP` registers in each `CallFrame`:

```csharp
unsafe
{
    foreach (CallFrame frame in CallTrace.Capture().Frames)
    {
        Console.WriteLine(frame);
        Console.WriteLine($"  rip=0x{(nuint)frame.IP:x} rsp=0x{(nuint)frame.SP:x} rbp=0x{(nuint)frame.FP:x}");
    }
}
```

You will now get:

```text
0x7ffe4cf80b42: Void Program.<Main>$(String[] args) in tests.dll
  rip=0x7ffe4cf80b42 rsp=0xd61858e920 rbp=0xd61858e9f0
0x7ffeacabe823: coreclr_shutdown_2+0x16683 in coreclr.dll+0x16e823
  rip=0x7ffeacabe823 rsp=0xd61858ea00 rbp=0xd61858ea30
0x7ffeac99a133: <unknown> in coreclr.dll+0x4a133
  rip=0x7ffeac99a133 rsp=0xd61858ea40 rbp=0xd61858eb60
0x7ffeaca2d710: <unknown> in coreclr.dll+0xdd710
  rip=0x7ffeaca2d710 rsp=0xd61858eb70 rbp=0xd61858ec80
0x7ffeaca2efb6: <unknown> in coreclr.dll+0xdefb6
  rip=0x7ffeaca2efb6 rsp=0xd61858ec90 rbp=0xd61858ed30
0x7ffeaca2f749: <unknown> in coreclr.dll+0xdf749
  rip=0x7ffeaca2f749 rsp=0xd61858ed40 rbp=0xd61858f0c0
0x7ffeaca2dcb9: <unknown> in coreclr.dll+0xddcb9
  rip=0x7ffeaca2dcb9 rsp=0xd61858f0d0 rbp=0xd61858f200
0x7ffeaca93bd1: coreclr_execute_assembly+0xe1 in coreclr.dll+0x143bd1
  rip=0x7ffeaca93bd1 rsp=0xd61858f210 rbp=0xd61858f290
0x7fff31f69148: <unknown> in hostpolicy.dll+0x19148
  rip=0x7fff31f69148 rsp=0xd61858f2a0 rbp=0xd61858f420
0x7fff31f6941c: <unknown> in hostpolicy.dll+0x1941c
  rip=0x7fff31f6941c rsp=0xd61858f430 rbp=0xd61858f460
0x7fff31f69d17: corehost_main+0x107 in hostpolicy.dll+0x19d17
  rip=0x7fff31f69d17 rsp=0xd61858f470 rbp=0xd61858f610
0x7fff3947b459: hostfxr_close+0xfb9 in hostfxr.dll+0xb459
  rip=0x7fff3947b459 rsp=0xd61858f620 rbp=0xd61858f710
0x7fff3947e4f6: hostfxr_close+0x4056 in hostfxr.dll+0xe4f6
  rip=0x7fff3947e4f6 rsp=0xd61858f720 rbp=0xd61858f810
0x7fff394807cf: hostfxr_close+0x632f in hostfxr.dll+0x107cf
  rip=0x7fff394807cf rsp=0xd61858f820 rbp=0xd61858f8c0
0x7fff3947eb52: hostfxr_close+0x46b2 in hostfxr.dll+0xeb52
  rip=0x7fff3947eb52 rsp=0xd61858f8d0 rbp=0xd61858fa00
0x7fff394781cb: hostfxr_main_startupinfo+0xab in hostfxr.dll+0x81cb
  rip=0x7fff394781cb rsp=0xd61858fa10 rbp=0xd61858fb00
0x7ff7e62f255b: <unknown> in tests.exe+0x1255b
  rip=0x7ff7e62f255b rsp=0xd61858fb10 rbp=0xd61858fcd0
0x7ff7e62f28cb: <unknown> in tests.exe+0x128cb
  rip=0x7ff7e62f28cb rsp=0xd61858fce0 rbp=0xd61858fd00
0x7ff7e62f3d78: <unknown> in tests.exe+0x13d78
  rip=0x7ff7e62f3d78 rsp=0xd61858fd10 rbp=0xd61858fd40
0x7fff79193fed: BaseThreadInitThunk+0x1d in kernel32.dll+0x13fed
  rip=0x7fff79193fed rsp=0xd61858fd50 rbp=0xd61858fd70
0x7fff7a6142a8: RtlUserThreadStart+0x28 in ntdll.dll+0x42a8
  rip=0x7fff7a6142a8 rsp=0xd61858fd80 rbp=0xd61858fdf0
```

## Symbolication

`CallTrace` tries very hard internally to fill in as much information as it can.
For managed frames, information is pulled from CoreCLR internals, while for
unmanaged frames, the
[DbgHelp](https://docs.microsoft.com/en-us/windows/win32/debug/debug-help-library)
library will consult export names, symbol files, etc. These are implemented in
the `ManagedCallFrameSymbolicator` and `NativeCallFrameSymbolicator` singleton
classes, respectively, and both derive from the `CallFrameSymbolicator` class.

There is a `CallTrace.Capture()` overload that allows you to specify the
`CallFrameSymbolicator` instances you would like to use in a call trace instead
of or in addition to the aforementioned two. This allows you to implement your
own symbolicators that can pull on whatever data you would like. For instance,
you could symbolicate based on signature matching, or based on a symbol table
manually constructed from reverse engineering.

### Symbol Servers

For `NativeCallFrameSymbolicator`, it is worth noting that the DbgHelp library
shipped with Windows does not have symbol server support (`symsrv.dll`). That is
why the call traces above had rather poor, export-based symbolication for
`coreclr.dll` and related libraries.

If you obtain a
[standalone version](https://docs.microsoft.com/en-us/windows/win32/debug/dbghelp-versions)
of the DbgHelp library consisting of `dbghelp.dll` and `symsrv.dll`, you can
simply drop them into your application directory and Ruptura will pick them up.
Setting the `_NT_SYMBOL_PATH` environment variable to
`https://msdl.microsoft.com/download/symbols` or similar will then enable
`symsrv.dll` to actually download Microsoft symbol files. Doing that, you will
get a much better call trace:

```text
0x7ffe5a5e0b09: Void Program.<Main>$(String[] args) in tests.dll
0x7ffeba10e823: CallDescrWorkerInternal+0x83 in coreclr.dll+0x16e823
0x7ffeb9fea133: CallDescrWorkerWithHandler+0x56 in coreclr.dll+0x4a133 at D:\a\_work\1\s\src\coreclr\vm\callhelpers.cpp:67
0x7ffeb9fea133: MethodDescCallSite::CallTargetWorker+0x247 in coreclr.dll+0x4a133 at D:\a\_work\1\s\src\coreclr\vm\callhelpers.cpp:570
0x7ffeba07d710: MethodDescCallSite::Call+0xb in coreclr.dll+0xdd710 at D:\a\_work\1\s\src\coreclr\vm\callhelpers.h:458
0x7ffeba07d710: RunMainInternal+0x11c in coreclr.dll+0xdd710 at D:\a\_work\1\s\src\coreclr\vm\assembly.cpp:1354
0x7ffeba07efb6: RunMain+0xd2 in coreclr.dll+0xdefb6 at D:\a\_work\1\s\src\coreclr\vm\assembly.cpp:1425
0x7ffeba07f749: Assembly::ExecuteMainMethod+0x1f1 in coreclr.dll+0xdf749 at D:\a\_work\1\s\src\coreclr\vm\assembly.cpp:1543
0x7ffeba07dcb9: CorHost2::ExecuteAssembly+0x1d9 in coreclr.dll+0xddcb9 at D:\a\_work\1\s\src\coreclr\vm\corhost.cpp:360
0x7ffeba0e3bd1: coreclr_execute_assembly+0xe1 in coreclr.dll+0x143bd1 at D:\a\_work\1\s\src\coreclr\dlls\mscoree\exports.cpp:430
0x7ffebaa19148: coreclr_t::execute_assembly+0x2a in hostpolicy.dll+0x19148 at D:\a\_work\1\s\src\native\corehost\hostpolicy\coreclr.cpp:89
0x7ffebaa19148: run_app_for_context+0x4e8 in hostpolicy.dll+0x19148 at D:\a\_work\1\s\src\native\corehost\hostpolicy\hostpolicy.cpp:255
0x7ffebaa1941c: run_app+0x3c in hostpolicy.dll+0x1941c at D:\a\_work\1\s\src\native\corehost\hostpolicy\hostpolicy.cpp:284
0x7ffebaa19d17: corehost_main+0x107 in hostpolicy.dll+0x19d17 at D:\a\_work\1\s\src\native\corehost\hostpolicy\hostpolicy.cpp:430
0x7ffebaa7b459: execute_app+0x2e9 in hostfxr.dll+0xb459 at D:\a\_work\1\s\src\native\corehost\fxr\fx_muxer.cpp:146
0x7ffebaa7e4f6: `anonymous namespace'::read_config_and_execute+0xa6 in hostfxr.dll+0xe4f6 at D:\a\_work\1\s\src\native\corehost\fxr\fx_muxer.cpp:533
0x7ffebaa807cf: fx_muxer_t::handle_exec_host_command+0x15f in hostfxr.dll+0x107cf at D:\a\_work\1\s\src\native\corehost\fxr\fx_muxer.cpp:1018
0x7ffebaa7eb52: fx_muxer_t::execute+0x482 in hostfxr.dll+0xeb52 at D:\a\_work\1\s\src\native\corehost\fxr\fx_muxer.cpp:579
0x7ffebaa781cb: hostfxr_main_startupinfo+0xab in hostfxr.dll+0x81cb at D:\a\_work\1\s\src\native\corehost\fxr\hostfxr.cpp:61
0x7ff7e7cb255b: exe_start+0x8eb in tests.exe+0x1255b at D:\a\_work\1\s\src\native\corehost\corehost.cpp:251
0x7ff7e7cb28cb: wmain+0xab in tests.exe+0x128cb at D:\a\_work\1\s\src\native\corehost\corehost.cpp:322
0x7ff7e7cb3d78: invoke_main+0x22 in tests.exe+0x13d78 at D:\a\_work\1\s\src\vctools\crt\vcstartup\src\startup\exe_common.inl:90
0x7ff7e7cb3d78: __scrt_common_main_seh+0x10c in tests.exe+0x13d78 at D:\a\_work\1\s\src\vctools\crt\vcstartup\src\startup\exe_common.inl:288
0x7fff79193fed: BaseThreadInitThunk+0x1d in kernel32.dll+0x13fed
0x7fff7a6142a8: RtlUserThreadStart+0x28 in ntdll.dll+0x42a8
```
