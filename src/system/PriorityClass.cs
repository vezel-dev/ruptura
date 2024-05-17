// SPDX-License-Identifier: 0BSD

using Windows.Win32.System.Threading;

namespace Vezel.Ruptura.System;

[SuppressMessage("", "CA1008")]
[SuppressMessage("", "CA1027")]
public enum PriorityClass : uint
{
    Normal = PROCESS_CREATION_FLAGS.NORMAL_PRIORITY_CLASS,
    Idle = PROCESS_CREATION_FLAGS.IDLE_PRIORITY_CLASS,
    High = PROCESS_CREATION_FLAGS.HIGH_PRIORITY_CLASS,
    RealTime = PROCESS_CREATION_FLAGS.REALTIME_PRIORITY_CLASS,
    BelowNormal = PROCESS_CREATION_FLAGS.BELOW_NORMAL_PRIORITY_CLASS,
    AboveNormal = PROCESS_CREATION_FLAGS.ABOVE_NORMAL_PRIORITY_CLASS,
}
