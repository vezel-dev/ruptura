using Windows.Win32.System.Diagnostics.ToolHelp;

namespace Vezel.Ruptura.System;

[Flags]
public enum HeapBlockSnapshotFlags : uint
{
    Fixed = HEAPENTRY32_FLAGS.LF32_FIXED,
    Free = HEAPENTRY32_FLAGS.LF32_FREE,
    Movable = HEAPENTRY32_FLAGS.LF32_MOVEABLE,
}
