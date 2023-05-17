using Vezel.Ruptura.System.SafeHandles;
using Windows.Win32.Foundation;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

public abstract class SynchronizationObject : KernelObject
{
    private protected SynchronizationObject(nint handle)
        : base(handle)
    {
    }

    private static WAIT_EVENT WaitMultiple(
        scoped ReadOnlySpan<SynchronizationObject> objects, bool all, TimeSpan timeout, bool alertable)
    {
        Check.Argument(objects.Length is > 0 and <= (int)MAXIMUM_WAIT_OBJECTS, nameof(objects));

        var count = objects.Length;
        var safeHandles = new SafeKernelHandle[count];
        var unsafeHandles = (stackalloc HANDLE[count]);

        try
        {
            for (var i = 0; i < count; i++)
            {
                var handle = objects[i]?.SafeHandle;

                Check.Argument(handle != null, nameof(objects));

                var unused = false;

                // This may throw ObjectDisposedException if the handle is closed, so do not add it to the list of
                // handles to be cleaned up until we know the reference count was incremented successfully.
                handle.DangerousAddRef(ref unused);

                safeHandles[i] = handle;
                unsafeHandles[i] = (HANDLE)handle.DangerousGetHandle();
            }

            return WaitForMultipleObjectsEx(
                unsafeHandles, all, (uint)timeout.TotalMilliseconds, alertable) switch
            {
                WAIT_EVENT.WAIT_FAILED => throw new Win32Exception(),
                var result => result,
            };
        }
        finally
        {
            // In case of an early failure, some of the handles may be null.
            foreach (var handle in safeHandles)
                handle?.DangerousRelease();
        }
    }

    public static (WaitResult Result, int? Index) WaitAny(
        scoped ReadOnlySpan<SynchronizationObject> objects, TimeSpan timeout, bool alertable)
    {
        return WaitMultiple(objects, false, timeout, alertable) switch
        {
            WAIT_EVENT.WAIT_TIMEOUT => (WaitResult.TimedOut, null),
            WAIT_EVENT.WAIT_IO_COMPLETION => (WaitResult.Alerted, null),
            >= WAIT_EVENT.WAIT_ABANDONED_0 and var result =>
                (WaitResult.Abandoned, (int)result - (int)WAIT_EVENT.WAIT_ABANDONED_0),
            >= WAIT_EVENT.WAIT_OBJECT_0 and var result =>
                (WaitResult.Signaled, (int)result - (int)WAIT_EVENT.WAIT_OBJECT_0),
        };
    }

    public static WaitResult WaitAll(
        scoped ReadOnlySpan<SynchronizationObject> objects, TimeSpan timeout, bool alertable)
    {
        // Note that when waiting for all objects, the index that is returned is meaningless, even in the case of an
        // abandoned mutex (or several).
        return WaitMultiple(objects, true, timeout, alertable) switch
        {
            WAIT_EVENT.WAIT_TIMEOUT => WaitResult.TimedOut,
            WAIT_EVENT.WAIT_IO_COMPLETION => WaitResult.Alerted,
            >= WAIT_EVENT.WAIT_ABANDONED_0 => WaitResult.Abandoned,
            >= WAIT_EVENT.WAIT_OBJECT_0 => WaitResult.Signaled,
        };
    }

    public WaitResult Wait(TimeSpan timeout, bool alertable)
    {
        return WaitForSingleObjectEx(SafeHandle, (uint)timeout.TotalMilliseconds, alertable) switch
        {
            WAIT_EVENT.WAIT_TIMEOUT => WaitResult.TimedOut,
            WAIT_EVENT.WAIT_IO_COMPLETION => WaitResult.Alerted,
            WAIT_EVENT.WAIT_OBJECT_0 => WaitResult.Signaled,
            WAIT_EVENT.WAIT_ABANDONED_0 => WaitResult.Abandoned,
            _ => throw new Win32Exception(),
        };
    }
}
