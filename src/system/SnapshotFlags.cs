// SPDX-License-Identifier: 0BSD

using Windows.Win32.System.Diagnostics.ToolHelp;

namespace Vezel.Ruptura.System;

[Flags]
[SuppressMessage("", "CA1711")]
public enum SnapshotFlags : uint
{
    Heaps = CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPHEAPLIST,
    Processes = CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS,
    Threads = CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPTHREAD,
    Modules = CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPMODULE,
    Modules32 = CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPMODULE32,
}
