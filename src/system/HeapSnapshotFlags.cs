// SPDX-License-Identifier: 0BSD

using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

[Flags]
[SuppressMessage("", "CA1711")]
public enum HeapSnapshotFlags : uint
{
    Default = HF32_DEFAULT,
    Shared = HF32_SHARED,
}
