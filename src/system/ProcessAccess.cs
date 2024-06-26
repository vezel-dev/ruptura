// SPDX-License-Identifier: 0BSD

using Windows.Win32.System.Threading;

namespace Vezel.Ruptura.System;

[Flags]
public enum ProcessAccess : uint
{
    Terminate = PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE,
    CreateThread = PROCESS_ACCESS_RIGHTS.PROCESS_CREATE_THREAD,
    OperateMemory = PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION,
    ReadMemory = PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ,
    WriteMemory = PROCESS_ACCESS_RIGHTS.PROCESS_VM_WRITE,
    DuplicateHandle = PROCESS_ACCESS_RIGHTS.PROCESS_DUP_HANDLE,
    CreateProcess = PROCESS_ACCESS_RIGHTS.PROCESS_CREATE_PROCESS,
    SetQuota = PROCESS_ACCESS_RIGHTS.PROCESS_SET_QUOTA,
    SetInfo = PROCESS_ACCESS_RIGHTS.PROCESS_SET_INFORMATION,
    GetInfo = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION,
    SuspendResume = PROCESS_ACCESS_RIGHTS.PROCESS_SUSPEND_RESUME,
    GetLimitedInfo = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
    SetLimitedInfo = PROCESS_ACCESS_RIGHTS.PROCESS_SET_LIMITED_INFORMATION,
    Synchronize = PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE,
}
