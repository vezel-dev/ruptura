// SPDX-License-Identifier: 0BSD

using Windows.Win32.System.Memory;

namespace Vezel.Ruptura.System;

[SuppressMessage("", "CA1027")]
public enum MemoryAccess : uint
{
    None = 0x0,
    Read = PAGE_PROTECTION_FLAGS.PAGE_READONLY,
    ReadWrite = PAGE_PROTECTION_FLAGS.PAGE_READWRITE,
    ReadCopyOnWrite = PAGE_PROTECTION_FLAGS.PAGE_WRITECOPY,
    Execute = PAGE_PROTECTION_FLAGS.PAGE_EXECUTE,
    ExecuteRead = PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READ,
    ExecuteReadWrite = PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE,
    ExecuteReadCopyOnWrite = PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_WRITECOPY,
}
