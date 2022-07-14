using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Memory.Code;

public sealed unsafe class SimpleCodeManager : CodeManager
{
    // This is a super simple code manager that just uses VirtualAlloc2. Due to the huge granularity of VirtualAlloc2,
    // this is not great for use cases involving lots of code allocations. W^X is enforced.

    sealed class SimpleCodeAllocation : CodeAllocation
    {
        public override void* Code => Node != null ? _address : throw new ObjectDisposedException(GetType().Name);

        public override nint Length => Node != null ? _length : throw new ObjectDisposedException(GetType().Name);

        public LinkedListNode<SimpleCodeAllocation>? Node
        {
            get => _node;
            set => _node = value;
        }

        readonly void* _address;

        readonly nint _length;

        volatile LinkedListNode<SimpleCodeAllocation>? _node;

        public SimpleCodeAllocation(SimpleCodeManager manager, void* address, nint length)
            : base(manager)
        {
            _address = address;
            _length = length;
        }

        public override void Dispose()
        {
            if (Node != null)
                Unsafe.As<SimpleCodeManager>(Manager).Deallocate(this, _address);
        }

        public override void Commit()
        {
            _ = Node != null ? true : throw new ObjectDisposedException(GetType().Name);

            _process.FlushInstructionCache(_address, _length);
            _process.ProtectMemory(_address, _length, MemoryAccess.ReadExecute);
        }

        public override void Decommit()
        {
            _ = Node != null ? true : throw new ObjectDisposedException(GetType().Name);

            _process.ProtectMemory(_address, _length, MemoryAccess.ReadWrite);
        }
    }

    static readonly ProcessObject _process = ProcessObject.Current;

    static readonly uint _alignment;

    readonly object _lock = new();

    LinkedList<SimpleCodeAllocation>? _allocations = new();

    static SimpleCodeManager()
    {
        Win32.GetSystemInfo(out var info);

        Console.WriteLine("Min app address: {0:x}", (nuint)info.lpMinimumApplicationAddress);
        Console.WriteLine("Max app address: {0:x}", (nuint)info.lpMaximumApplicationAddress);

        _alignment = info.dwAllocationGranularity;
    }

    public override void Dispose()
    {
        lock (_lock)
        {
            if (_allocations != null)
                foreach (var alloc in _allocations.ToArray())
                    alloc.Dispose();

            _allocations = null;
        }
    }

    public override CodeAllocation Allocate(nint length, CodeRequirements requirements = default)
    {
        _ = length > 0 ? true : throw new ArgumentOutOfRangeException(nameof(length));
        _ = _allocations != null ? true : throw new ObjectDisposedException(GetType().Name);

        var low = (nuint)requirements.LowestAddress;

        if (low % _alignment is var rem and not 0)
            low += _alignment - rem;

        var high = (nuint)requirements.HighestAddress;

        if (high != 0)
        {
            high -= high % _alignment;
            high -= 1;
        }

        Console.WriteLine("Low: {0:x} High: {1:x}", low, high);

        var ptr = _process.AllocateMemoryInRange((void*)low, (void*)high, length, MemoryAccess.ReadWrite);

        try
        {
            var alloc = new SimpleCodeAllocation(this, (byte*)ptr, length);

            lock (_lock)
                alloc.Node = _allocations.AddLast(alloc);

            return alloc;
        }
        catch (Exception)
        {
            _process.FreeMemory(ptr);

            throw;
        }
    }

    void Deallocate(SimpleCodeAllocation allocation, void* address)
    {
        lock (_lock)
            _allocations!.Remove(allocation.Node!);

        allocation.Node = null;

        _process.FreeMemory(address);
    }
}
