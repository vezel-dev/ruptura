using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

[Flags]
[SuppressMessage("", "CA1711")]
public enum HeapSnapshotFlags : uint
{
    Default = Win32.HF32_DEFAULT,
    Shared = Win32.HF32_SHARED,
}
