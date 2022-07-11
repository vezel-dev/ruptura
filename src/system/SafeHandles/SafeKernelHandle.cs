using Windows.Win32.Foundation;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System.SafeHandles;

public sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeKernelHandle(bool ownsHandle)
        : base(ownsHandle)
    {
    }

    public SafeKernelHandle(nint existingHandle, bool ownsHandle)
        : this(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    protected override bool ReleaseHandle()
    {
        return CloseHandle((HANDLE)handle);
    }
}
