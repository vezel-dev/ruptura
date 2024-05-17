// SPDX-License-Identifier: 0BSD

using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

public sealed unsafe class ThreadObject : SynchronizationObject
{
    public static int CurrentId => (int)GetCurrentThreadId();

    [SuppressMessage("", "CA1065")]
    public int ProcessId => GetProcessIdOfThread(SafeHandle) is var id and not 0 ? (int)id : throw new Win32Exception();

    [SuppressMessage("", "CA1065")]
    public int Id => GetThreadId(SafeHandle) is var id and not 0 ? (int)id : throw new Win32Exception();

    public string Description
    {
        get
        {
            _ = GetThreadDescription(SafeHandle, out var desc).ThrowOnFailure();

            try
            {
                return desc.ToString();
            }
            finally
            {
                _ = LocalFree((HLOCAL)(nint)(char*)desc);
            }
        }
        set => SetThreadDescription(SafeHandle, value).ThrowOnFailure();
    }

    public PriorityLevel PriorityLevel
    {
        [SuppressMessage("", "CA1065")]
        get =>
            GetThreadPriority(SafeHandle) is var prio and not (int)THREAD_PRIORITY_ERROR_RETURN
                ? (PriorityLevel)prio
                : throw new Win32Exception();
        set
        {
            if (!SetThreadPriority(SafeHandle, (THREAD_PRIORITY)value))
                throw new Win32Exception();
        }
    }

    public bool PriorityBoostEnabled
    {
        [SuppressMessage("", "CA1065")]
        get => GetThreadPriorityBoost(SafeHandle, out var state) ? !state : throw new Win32Exception();
        set
        {
            if (!SetThreadPriorityBoost(SafeHandle, !value))
                throw new Win32Exception();
        }
    }

    [SuppressMessage("", "CA1065")]
    public bool IsBlockingIO => GetThreadIOPendingFlag(SafeHandle, out var flag) ? flag : throw new Win32Exception();

    private ThreadObject(nint handle)
        : base(handle)
    {
    }

    public static ThreadObject OpenHandle(nint handle)
    {
        uint unused;

        return GetHandleInformation((HANDLE)handle, &unused) ? new(handle) : throw new Win32Exception();
    }

    public static ThreadObject OpenId(int id, ThreadAccess? access)
    {
        return OpenThread(
            access is ThreadAccess acc ? (THREAD_ACCESS_RIGHTS)acc : THREAD_ACCESS_RIGHTS.THREAD_ALL_ACCESS,
            bInheritHandle: false,
            (uint)id) is { IsNull: false } handle
            ? new(handle)
            : throw new Win32Exception();
    }

    public static ThreadObject OpenCurrent()
    {
        return OpenId(CurrentId, access: null);
    }

    public static void GetStackBounds(out void* low, out void* high)
    {
        GetCurrentThreadStackLimits(out var lowLimit, out var highLimit);

        low = (void*)lowLimit;
        high = (void*)highLimit;
    }

    public static bool Yield()
    {
        return SwitchToThread();
    }

    public static bool Sleep(TimeSpan duration, bool alertable)
    {
        return SleepEx((uint)duration.TotalMilliseconds, alertable) == 0;
    }

    public static void Exit(int code)
    {
        ExitThread((uint)code);
    }

    public void GetTimes(
        out DateTime creationTime, out DateTime exitTime, out TimeSpan kernelTime, out TimeSpan userTime)
    {
        if (!GetThreadTimes(SafeHandle, out var creation, out var exit, out var kernel, out var user))
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
        if (!SetThreadPriority(SafeHandle, THREAD_PRIORITY.THREAD_MODE_BACKGROUND_BEGIN))
            throw new Win32Exception();
    }

    public void EndBackgroundMode()
    {
        if (!SetThreadPriority(SafeHandle, THREAD_PRIORITY.THREAD_MODE_BACKGROUND_END))
            throw new Win32Exception();
    }

    public bool CancelSynchronousIO()
    {
        return CancelSynchronousIo(SafeHandle) ||
            (Marshal.GetLastPInvokeError() == (int)WIN32_ERROR.ERROR_NOT_FOUND ? false : throw new Win32Exception());
    }

    public int Suspend()
    {
        return SuspendThread(SafeHandle) is var ret and not uint.MaxValue ? (int)ret : throw new Win32Exception();
    }

    public int SuspendWow64()
    {
        return Wow64SuspendThread(SafeHandle) is var ret and not uint.MaxValue ? (int)ret : throw new Win32Exception();
    }

    public int Resume()
    {
        return ResumeThread(SafeHandle) is var ret and not uint.MaxValue ? (int)ret : throw new Win32Exception();
    }

    public void Terminate(int code)
    {
        if (!TerminateThread(SafeHandle, (uint)code))
            throw new Win32Exception();
    }

    public int GetExitCode()
    {
        return GetExitCodeThread(SafeHandle, out var code) ? (int)code : throw new Win32Exception();
    }
}
