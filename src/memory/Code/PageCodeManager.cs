using Windows.Win32.System.SystemInformation;
using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Memory.Code;

public sealed unsafe class PageCodeManager : CodeManager
{
    private readonly struct AllocationInfo
    {
        public void* Address { get; }

        public nint PageCount { get; }

        public void* NextAddress => (byte*)Address + _info.dwPageSize * PageCount;

        public AllocationInfo(void* address, nint pageCount)
        {
            Address = address;
            PageCount = pageCount;
        }
    }

    private sealed class CodeRegion : IDisposable
    {
        public void* Address { get; }

        public LinkedListNode<CodeRegion>? Node { get; set; }

        private readonly PageCodeManager _manager;

        private readonly LinkedList<PageCodeAllocation> _allocationList = new();

        private readonly LinkedList<AllocationInfo> _freeList = new();

        public CodeRegion(PageCodeManager manager, void* address, nint pageCount)
        {
            _manager = manager;
            Address = address;

            _ = _freeList.AddFirst(new AllocationInfo(address, pageCount));
        }

        public void Dispose()
        {
            foreach (var alloc in _allocationList.ToArray())
                alloc.Dispose();
        }

        public PageCodeAllocation? Allocate(nint length, CodePlacement placement)
        {
            // Does this region's starting address satisfy the code placement? If not, there is no point in proceeding.
            if (!placement.Contains(Address))
                return null;

            var pageCount = (nint)((length + _info.dwPageSize - 1) / _info.dwPageSize);

            for (var node = _freeList.First; node != null; node = node.Next)
            {
                ref var info = ref node.ValueRef;

                if (pageCount > info.PageCount || !placement.Contains(info.Address))
                    continue;

                _freeList.Remove(node);

                // If we will only use part of the recycled allocation, put the rest back in the free list.
                if (info.PageCount - pageCount is var diff and not 0)
                {
                    _ = _freeList.AddLast(new AllocationInfo((byte*)info.Address + _info.dwPageSize * pageCount, diff));

                    Coalesce();
                }

                var alloc = new PageCodeAllocation(_manager, this, new(info.Address, pageCount), length);

                alloc.Node = _allocationList.AddLast(alloc);

                return alloc;
            }

            return null;
        }

        public void Deallocate(PageCodeAllocation allocation)
        {
            allocation.Decommit();

            _ = _allocationList.Remove(allocation);

            allocation.Node = null;

            _ = _freeList.AddLast(allocation.Info);

            Coalesce();
        }

        private void Coalesce()
        {
            bool changed;

            // Try to merge adjacent allocations in the free list. This makes future allocations more likely to succeed
            // without fragmenting the address space. It obviously comes at a cost here, but we are more concerned about
            // fragmentation than performance.
            do
            {
                changed = false;

                for (var current = _freeList.First; current != null; current = current.Next)
                {
                    ref var currentInfo = ref current.ValueRef;

                    var candidate = _freeList.First;

                    while (candidate != null)
                    {
                        ref readonly var candidateInfo = ref candidate.ValueRef;

                        // Removing the candidate node from the list below will set its Next property to null.
                        var next = candidate.Next;

                        if (candidateInfo.NextAddress == currentInfo.Address)
                        {
                            // The candidate node sits right before the current node.
                            currentInfo = new(candidateInfo.Address, candidateInfo.PageCount + currentInfo.PageCount);
                            changed = true;
                        }
                        else if (currentInfo.NextAddress == candidateInfo.Address)
                        {
                            // The candidate node sits right after the current node.
                            currentInfo = new(currentInfo.Address, currentInfo.PageCount + candidateInfo.PageCount);
                            changed = true;
                        }

                        if (changed)
                            _freeList.Remove(candidate);

                        candidate = next;
                    }
                }
            }
            while (changed);
        }
    }

    private sealed class PageCodeAllocation : CodeAllocation
    {
        public CodeRegion Region { get; }

        public AllocationInfo Info { get; }

        public override void* Code
        {
            get
            {
                ObjectDisposedException.ThrowIf(_node == null, this);

                return Info.Address;
            }
        }

        public override nint Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_node == null, this);

                return _length;
            }
        }

        public LinkedListNode<PageCodeAllocation>? Node
        {
            get => _node;
            set => _node = value;
        }

        private readonly nint _length;

        private volatile LinkedListNode<PageCodeAllocation>? _node;

        public PageCodeAllocation(
            PageCodeManager manager, CodeRegion region, AllocationInfo info, nint length)
            : base(manager)
        {
            Region = region;
            Info = info;
            _length = length;
        }

        public override void Dispose()
        {
            if (_node != null)
                Unsafe.As<PageCodeManager>(Manager).Deallocate(this);
        }

        public override void Commit()
        {
            ObjectDisposedException.ThrowIf(_node == null, this);

            _process.FlushInstructionCache(Info.Address, _length);

            _ = _process.ProtectMemory(Info.Address, _length, MemoryAccess.ExecuteRead);
        }

        public override void Decommit()
        {
            ObjectDisposedException.ThrowIf(_node == null, this);

            _ = _process.ProtectMemory(Info.Address, _length, MemoryAccess.ReadWrite);
        }
    }

    private static readonly ProcessObject _process = ProcessObject.Current;

    private static readonly SYSTEM_INFO _info;

    private readonly object _lock = new();

    private LinkedList<CodeRegion>? _regions = new();

    static PageCodeManager()
    {
        Win32.GetSystemInfo(out _info);
    }

    public override void Dispose()
    {
        lock (_lock)
        {
            if (_regions != null)
                foreach (var region in _regions.ToArray())
                    region.Dispose();

            _regions = null;
        }
    }

    public override CodeAllocation Allocate(nint length, CodePlacement placement)
    {
        _ = length > 0 ? true : throw new ArgumentOutOfRangeException(nameof(length));
        ObjectDisposedException.ThrowIf(_regions == null, this);

        // Most allocation requests will fit into an existing region.
        lock (_lock)
            foreach (var region in _regions)
                if (region.Allocate(length, placement) is PageCodeAllocation alloc)
                    return alloc;

        var granularity = _info.dwAllocationGranularity;
        var fullLength = (nuint)length;

        // Align region length to the allocation granularity to reduce fragmentation.
        if (fullLength % granularity is var allocRem and not 0)
            fullLength += granularity - allocRem;

        void* ptr;

        if (placement.IsRange)
        {
            var low = (nuint)placement.LowestAddress;
            var min = (nuint)_info.lpMinimumApplicationAddress;

            // VirtualAlloc2 requires that LowestStartingAddress is aligned to the allocation granularity (i.e. it is
            // the first accessible byte in the range). Additionally, it must not exceed lpMinimumApplicationAddress
            // which itself satisfies the former requirement.
            if (low < min)
                low = min;
            else if (low % granularity is var lowRem and not 0)
                low += granularity - lowRem;

            var high = (nuint)placement.HighestAddress;
            var max = (nuint)_info.lpMaximumApplicationAddress;

            // VirtualAlloc2 requires that HighestEndingAddress is aligned to the allocation granularity minus one (i.e.
            // it is the last accessible byte in the range). Additionally, it must not exceed
            // lpMaximumApplicationAddress which itself satisfies the former requirement.
            if (high > max)
                high = max;
            else
                high -= high % granularity + 1;

            ptr = _process.AllocateMemoryInRange((void*)low, (void*)high, (nint)fullLength, MemoryAccess.ReadWrite);
        }
        else
            ptr = _process.AllocateMemory(placement.LowestAddress, (nint)fullLength, MemoryAccess.ReadWrite);

        lock (_lock)
        {
            CodeRegion region;

            try
            {
                region = new(this, ptr, (nint)(fullLength / _info.dwPageSize));

                region.Node = _regions.AddLast(region);
            }
            catch (Exception)
            {
                _process.FreeMemory(ptr);

                throw;
            }

            // We know this will succeed since VirtualAlloc2 respected the parameters we set up above.
            return region.Allocate(length, placement)!;
        }
    }

    private void Deallocate(PageCodeAllocation allocation)
    {
        lock (_lock)
            allocation.Region.Deallocate(allocation);
    }
}
