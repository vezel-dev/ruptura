using Vezel.Ruptura.System.SafeHandles;
using Windows.Win32.Foundation;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

[SuppressMessage("", "CA1063")]
public abstract class KernelObject : CriticalFinalizerObject, IDisposable, IEquatable<KernelObject>
{
    public SafeKernelHandle SafeHandle
    {
        get
        {
            Check.Usable(!IsDisposed, this);

            return _safeHandle;
        }
    }

    public bool IsDisposed => _safeHandle.IsClosed;

    public bool IsInheritable
    {
        [SuppressMessage("", "CA1065")]
        get =>
            GetHandleInformation(SafeHandle, out var flags)
                ? ((HANDLE_FLAGS)flags).HasFlag(HANDLE_FLAGS.HANDLE_FLAG_INHERIT)
                : throw new Win32Exception();
        set
        {
            var flag = HANDLE_FLAGS.HANDLE_FLAG_INHERIT;

            if (!SetHandleInformation(SafeHandle, (uint)flag, value ? flag : 0))
                throw new Win32Exception();
        }
    }

    private readonly SafeKernelHandle _safeHandle;

    private protected KernelObject(nint handle)
    {
        _safeHandle = new SafeKernelHandle(handle, true);
    }

    ~KernelObject()
    {
        Close();
    }

    public void Dispose()
    {
        Close();

        GC.SuppressFinalize(this);
    }

    private void Close()
    {
        _safeHandle.Dispose();
    }

    public static bool operator ==(KernelObject? left, KernelObject? right) =>
        EqualityComparer<KernelObject>.Default.Equals(left, right);

    public static bool operator !=(KernelObject? left, KernelObject? right) => !(left == right);

    public bool Equals(KernelObject? other)
    {
        return other != null && CompareObjectHandles(SafeHandle, other.SafeHandle);
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return Equals(obj as KernelObject);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), SafeHandle.DangerousGetHandle());
    }

    public override string ToString()
    {
        return $"{{{GetType().Name}: {SafeHandle.DangerousGetHandle()}}}";
    }
}
