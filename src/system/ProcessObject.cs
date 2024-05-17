// SPDX-License-Identifier: 0BSD

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.Threading;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

public sealed unsafe class ProcessObject : SynchronizationObject
{
    public static ProcessObject Current { get; } = OpenCurrent();

    public static int CurrentId => (int)GetCurrentProcessId();

    [SuppressMessage("", "CA1065")]
    public int Id => GetProcessId(SafeHandle) is var id and not 0 ? (int)id : throw new Win32Exception();

    public PriorityClass PriorityClass
    {
        [SuppressMessage("", "CA1065")]
        get => GetPriorityClass(SafeHandle) is var cls and not 0 ? (PriorityClass)cls : throw new Win32Exception();
        set
        {
            if (!SetPriorityClass(SafeHandle, (PROCESS_CREATION_FLAGS)value))
                throw new Win32Exception();
        }
    }

    public bool PriorityBoostEnabled
    {
        [SuppressMessage("", "CA1065")]
        get => GetProcessPriorityBoost(SafeHandle, out var state) ? !state : throw new Win32Exception();
        set
        {
            if (!SetProcessPriorityBoost(SafeHandle, !value))
                throw new Win32Exception();
        }
    }

    private ProcessObject(nint handle)
        : base(handle)
    {
    }

    public static ProcessObject OpenHandle(nint handle)
    {
        uint unused;

        return GetHandleInformation((HANDLE)handle, &unused) ? new(handle) : throw new Win32Exception();
    }

    public static ProcessObject OpenId(int id, ProcessAccess? access)
    {
        return OpenProcess(
            access is ProcessAccess acc ? (PROCESS_ACCESS_RIGHTS)acc : PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS,
            bInheritHandle: false,
            (uint)id) is { IsNull: false } handle
            ? new(handle)
            : throw new Win32Exception();
    }

    public static ProcessObject OpenCurrent()
    {
        return OpenId(CurrentId, access: null);
    }

    public static void FlushWriteBuffers()
    {
        FlushProcessWriteBuffers();
    }

    public static void Exit(int code)
    {
        ExitProcess((uint)code);
    }

    public (ImageMachine System, ImageMachine Process) GetWow64Mode()
    {
        IMAGE_FILE_MACHINE system;

        return IsWow64Process2(SafeHandle, out var process, &system)
            ? ((ImageMachine)system, (ImageMachine)process)
            : throw new Win32Exception();
    }

    public void GetTimes(
        out DateTime creationTime, out DateTime exitTime, out TimeSpan kernelTime, out TimeSpan userTime)
    {
        if (!GetProcessTimes(SafeHandle, out var creation, out var exit, out var kernel, out var user))
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

    public void* AllocateMemory(void* address, nint length, MemoryAccess access)
    {
        return VirtualAlloc2(
            SafeHandle,
            address,
            (nuint)length,
            VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
            (uint)(access == MemoryAccess.None ? PAGE_PROTECTION_FLAGS.PAGE_NOACCESS : (PAGE_PROTECTION_FLAGS)access),
            []) is var ptr and not null
            ? ptr
            : throw new Win32Exception();
    }

    public void* AllocateMemoryInRange(void* low, void* high, nint length, MemoryAccess access)
    {
        var req = new MEM_ADDRESS_REQUIREMENTS
        {
            LowestStartingAddress = low,
            HighestEndingAddress = high,
        };
        var param = new MEM_EXTENDED_PARAMETER
        {
            Anonymous1 =
            {
                _bitfield = (ulong)MEM_EXTENDED_PARAMETER_TYPE.MemExtendedParameterAddressRequirements,
            },
            Anonymous2 =
            {
                Pointer = &req,
            },
        };

        return VirtualAlloc2(
            SafeHandle,
            BaseAddress: null,
            (nuint)length,
            VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
            (uint)(access == MemoryAccess.None ? PAGE_PROTECTION_FLAGS.PAGE_NOACCESS : (PAGE_PROTECTION_FLAGS)access),
            new Span<MEM_EXTENDED_PARAMETER>(ref param)) is var ptr and not null
            ? ptr
            : throw new Win32Exception();
    }

    public void FreeMemory(void* address)
    {
        if (!VirtualFreeEx(SafeHandle, address, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE))
            throw new Win32Exception();
    }

    public MemoryAccess ProtectMemory(void* address, nint length, MemoryAccess access)
    {
        return VirtualProtectEx(
            SafeHandle,
            address,
            (nuint)length,
            access == MemoryAccess.None ? PAGE_PROTECTION_FLAGS.PAGE_NOACCESS : (PAGE_PROTECTION_FLAGS)access,
            out var flags)
            ? flags == PAGE_PROTECTION_FLAGS.PAGE_NOACCESS ? MemoryAccess.None : (MemoryAccess)((uint)flags & 0xff)
            : throw new Win32Exception();
    }

    public void ReadMemory(void* source, void* destination, nint length)
    {
        if (!ReadProcessMemory(SafeHandle, source, destination, (nuint)length, lpNumberOfBytesRead: null))
            throw new Win32Exception();
    }

    public void WriteMemory(void* destination, void* source, nint length)
    {
        if (!WriteProcessMemory(SafeHandle, destination, source, (nuint)length, lpNumberOfBytesWritten: null))
            throw new Win32Exception();
    }

    public void FlushInstructionCache(void* address, nint length)
    {
        if (!WindowsPInvoke.FlushInstructionCache(SafeHandle, address, (nuint)length))
            throw new Win32Exception();
    }

    public void BeginBackgroundMode()
    {
        if (!SetPriorityClass(SafeHandle, PROCESS_CREATION_FLAGS.PROCESS_MODE_BACKGROUND_BEGIN))
            throw new Win32Exception();
    }

    public void EndBackgroundMode()
    {
        if (!SetPriorityClass(SafeHandle, PROCESS_CREATION_FLAGS.PROCESS_MODE_BACKGROUND_END))
            throw new Win32Exception();
    }

    public void Terminate(int code)
    {
        if (!TerminateProcess(SafeHandle, (uint)code))
            throw new Win32Exception();
    }

    public int GetExitCode()
    {
        return GetExitCodeProcess(SafeHandle, out var code) ? (int)code : throw new Win32Exception();
    }
}
