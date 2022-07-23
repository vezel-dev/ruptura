# System Access

The **Vezel.Ruptura.System** library provides convenient managed wrappers for
native Win32 APIs and kernel objects. The focus is primarily on functionality
that is not readily available in .NET, but there is still some overlap.

For example, you could enumerate all threads in a process like so:

```csharp
using var snapshot = SnapshotObject.Create(SnapshotFlags.Threads, ProcessObject.CurrentId);

foreach (ThreadSnapshot threadSnapshot in snapshot.EnumerateThreads())
{
    using var thread = ThreadObject.OpenId(threadSnapshot.Id, ThreadAccess.GetLimitedInfo);

    Console.WriteLine($"{thread.Id}: {thread.Description}");
}
```

Instances of classes derived from `KernelObject` (such as the `SnapshotObject`
and `ThreadObject` instances in the above code) are thin wrappers around
[`SafeHandle`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle)
objects, with a few niceties on top, such as a settable `IsInheritable` property
and equality operators based on
[`CompareObjectHandles`](https://docs.microsoft.com/en-us/windows/win32/api/handleapi/nf-handleapi-compareobjecthandles).
`KernelObject` inherits from
[`CriticalFinalizerObject`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.constrainedexecution.criticalfinalizerobject),
so like `SafeHandle`, you can rely on `KernelObject` instances being usable in
finalizers. On top of that, they expose a bunch of relevant Win32 APIs for the
particular object type.

Some kernel objects are waitable. This is the case for `ProcessObject` and
`ThreadObject`, for example. They derive from `SynchronizationObject` (which
derives from `KernelObject`). You could wait for a thread to exit like this:

```csharp
using var thread = ThreadObject.OpenId(id, ThreadAccess.Synchronize);

if (thread.Wait(TimeSpan.FromSeconds(5), alertable: false) == WaitResult.TimedOut)
    throw new TimeoutException();
```

(`WaitResult.Alerted` can only occur if `alertable` is set to `true`. Also,
`WaitResult.Abandoned` is only relevant when waiting on a mutex.)
