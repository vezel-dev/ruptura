using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Threading;
using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.System;

public sealed unsafe class ProcessObject : SynchronizationObject
{
    public static int CurrentId => (int)Win32.GetCurrentProcessId();

    public int Id => Win32.GetProcessId(SafeHandle) is var id and not 0 ? (int)id : throw new Win32Exception();

    public PriorityClass PriorityClass
    {
        get =>
            Win32.GetPriorityClass(SafeHandle) is var cls and not 0 ? (PriorityClass)cls : throw new Win32Exception();
        set
        {
            if (!Win32.SetPriorityClass(SafeHandle, (PROCESS_CREATION_FLAGS)value))
                throw new Win32Exception();
        }
    }

    public bool PriorityBoostEnabled
    {
        get => Win32.GetProcessPriorityBoost(SafeHandle, out var state) ? !state : throw new Win32Exception();
        set
        {
            if (!Win32.SetProcessPriorityBoost(SafeHandle, !value))
                throw new Win32Exception();
        }
    }

    ProcessObject(nint handle)
        : base(handle)
    {
    }

    public static ProcessObject OpenHandle(nint handle)
    {
        uint unused;

        return Win32.GetHandleInformation((HANDLE)handle, &unused) ? new(handle) : throw new Win32Exception();
    }

    public static ProcessObject OpenId(int id)
    {
        return Win32.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS, false, (uint)id) is { IsNull: false } handle
            ? new(handle)
            : throw new Win32Exception();
    }

    public static ProcessObject OpenCurrent()
    {
        return OpenId(CurrentId);
    }

    public static void FlushWriteBuffers()
    {
        Win32.FlushProcessWriteBuffers();
    }

    public static void Exit(int code)
    {
        Win32.ExitProcess((uint)code);
    }

    public void GetTimes(
        out DateTime creationTime, out DateTime exitTime, out TimeSpan kernelTime, out TimeSpan userTime)
    {
        if (!Win32.GetProcessTimes(SafeHandle, out var creation, out var exit, out var kernel, out var user))
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
        return Win32.VirtualAlloc2(
            SafeHandle,
            address,
            (nuint)length,
            VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
            (uint)access,
            Span<MEM_EXTENDED_PARAMETER>.Empty) is var ptr and not null
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

        return Win32.VirtualAlloc2(
            SafeHandle,
            null,
            (nuint)length,
            VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
            (uint)access,
            new Span<MEM_EXTENDED_PARAMETER>(&param, 1)) is var ptr and not null
            ? ptr
            : throw new Win32Exception();
    }

    public void FreeMemory(void* address)
    {
        if (!Win32.VirtualFreeEx(SafeHandle, address, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE))
            throw new Win32Exception();
    }

    public void ProtectMemory(void* address, nint length, MemoryAccess access)
    {
        if (!Win32.VirtualProtectEx(SafeHandle, address, (nuint)length, (PAGE_PROTECTION_FLAGS)access, out _))
            throw new Win32Exception();
    }

    public void ReadMemory(void* source, void* destination, nint length)
    {
        if (!Win32.ReadProcessMemory(SafeHandle, source, destination, (nuint)length, null))
            throw new Win32Exception();
    }

    public void WriteMemory(void* destination, void* source, nint length)
    {
        if (!Win32.WriteProcessMemory(SafeHandle, destination, source, (nuint)length, null))
            throw new Win32Exception();
    }

    public void FlushInstructionCache(void* address, nint length)
    {
        if (!Win32.FlushInstructionCache(SafeHandle, address, (nuint)length))
            throw new Win32Exception();
    }

    public void BeginBackgroundMode()
    {
        if (!Win32.SetPriorityClass(SafeHandle, PROCESS_CREATION_FLAGS.PROCESS_MODE_BACKGROUND_BEGIN))
            throw new Win32Exception();
    }

    public void EndBackgroundMode()
    {
        if (!Win32.SetPriorityClass(SafeHandle, PROCESS_CREATION_FLAGS.PROCESS_MODE_BACKGROUND_END))
            throw new Win32Exception();
    }

    public void Terminate(int code)
    {
        if (!Win32.TerminateProcess(SafeHandle, (uint)code))
            throw new Win32Exception();
    }

    public int GetExitCode()
    {
        return Win32.GetExitCodeProcess(SafeHandle, out var code) ? (int)code : throw new Win32Exception();
    }
}
