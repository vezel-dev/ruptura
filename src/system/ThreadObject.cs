using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

public sealed unsafe class ThreadObject : SynchronizationObject
{
    public static int CurrentId => (int)Win32.GetCurrentThreadId();

    public int ProcessId =>
        Win32.GetProcessIdOfThread(SafeHandle) is var id and not 0
            ? (int)id
            : throw new Win32Exception();

    public int Id => Win32.GetThreadId(SafeHandle) is var id and not 0 ? (int)id : throw new Win32Exception();

    public string Description
    {
        get
        {
            _ = Win32.GetThreadDescription(SafeHandle, out var desc).ThrowOnFailure();

            try
            {
                return desc.ToString();
            }
            finally
            {
                _ = Win32.LocalFree((nint)(char*)desc);
            }
        }
        set => Win32.SetThreadDescription(SafeHandle, value).ThrowOnFailure();
    }

    public PriorityLevel PriorityLevel
    {
        get =>
            Win32.GetThreadPriority(SafeHandle) is var prio and not (int)Win32.THREAD_PRIORITY_ERROR_RETURN
                ? (PriorityLevel)prio
                : throw new Win32Exception();
        set
        {
            if (!Win32.SetThreadPriority(SafeHandle, (THREAD_PRIORITY)value))
                throw new Win32Exception();
        }
    }

    public bool PriorityBoostEnabled
    {
        get => Win32.GetThreadPriorityBoost(SafeHandle, out var state) ? !state : throw new Win32Exception();
        set
        {
            if (!Win32.SetThreadPriorityBoost(SafeHandle, !value))
                throw new Win32Exception();
        }
    }

    public bool IsBlockingIO =>
        Win32.GetThreadIOPendingFlag(SafeHandle, out var flag)
            ? flag
            : throw new Win32Exception();

    ThreadObject(nint handle)
        : base(handle)
    {
    }

    public static ThreadObject OpenHandle(nint handle)
    {
        uint unused;

        return Win32.GetHandleInformation((HANDLE)handle, &unused) ? new(handle) : throw new Win32Exception();
    }

    public static ThreadObject OpenId(int id, ThreadAccess? access)
    {
        return Win32.OpenThread(
            access is ThreadAccess acc ? (THREAD_ACCESS_RIGHTS)acc : THREAD_ACCESS_RIGHTS.THREAD_ALL_ACCESS,
            false,
            (uint)id) is { IsNull: false } handle
            ? new(handle)
            : throw new Win32Exception();
    }

    public static ThreadObject OpenCurrent()
    {
        return OpenId(CurrentId, null);
    }

    public static void GetStackBounds(out void* low, out void* high)
    {
        Win32.GetCurrentThreadStackLimits(out var lowLimit, out var highLimit);

        low = (void*)lowLimit;
        high = (void*)highLimit;
    }

    public static bool Yield()
    {
        return Win32.SwitchToThread();
    }

    public static bool Sleep(TimeSpan duration, bool alertable)
    {
        return Win32.SleepEx((uint)duration.TotalMilliseconds, alertable) == 0;
    }

    public static void Exit(int code)
    {
        Win32.ExitThread((uint)code);
    }

    public void GetTimes(
        out DateTime creationTime, out DateTime exitTime, out TimeSpan kernelTime, out TimeSpan userTime)
    {
        if (!Win32.GetThreadTimes(SafeHandle, out var creation, out var exit, out var kernel, out var user))
            throw new Win32Exception();

        static long ToTicks(FILETIME time)
        {
            return (long)((ulong)time.dwHighDateTime << 32 | (uint)time.dwLowDateTime);
        }

        creationTime = DateTime.FromFileTimeUtc(ToTicks(creation));
        exitTime = DateTime.FromFileTimeUtc(ToTicks(exit));
        kernelTime = new(ToTicks(kernel));
        userTime = new(ToTicks(user));
    }

    public void BeginBackgroundMode()
    {
        if (!Win32.SetThreadPriority(SafeHandle, THREAD_PRIORITY.THREAD_MODE_BACKGROUND_BEGIN))
            throw new Win32Exception();
    }

    public void EndBackgroundMode()
    {
        if (!Win32.SetThreadPriority(SafeHandle, THREAD_PRIORITY.THREAD_MODE_BACKGROUND_END))
            throw new Win32Exception();
    }

    public bool CancelSynchronousIO()
    {
        return Win32.CancelSynchronousIo(SafeHandle) ||
            (Marshal.GetLastPInvokeError() == (int)WIN32_ERROR.ERROR_NOT_FOUND ? false : throw new Win32Exception());
    }

    public int Suspend()
    {
        return Win32.SuspendThread(SafeHandle) is var ret and not uint.MaxValue ? (int)ret : throw new Win32Exception();
    }

    public int SuspendWow64()
    {
        return Win32.Wow64SuspendThread(SafeHandle) is var ret and not uint.MaxValue
            ? (int)ret
            : throw new Win32Exception();
    }

    public int Resume()
    {
        return Win32.ResumeThread(SafeHandle) is var ret and not uint.MaxValue ? (int)ret : throw new Win32Exception();
    }

    public void Terminate(int code)
    {
        if (!Win32.TerminateThread(SafeHandle, (uint)code))
            throw new Win32Exception();
    }

    public int GetExitCode()
    {
        return Win32.GetExitCodeThread(SafeHandle, out var code) ? (int)code : throw new Win32Exception();
    }
}
