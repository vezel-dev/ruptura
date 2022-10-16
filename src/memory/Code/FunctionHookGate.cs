namespace Vezel.Ruptura.Memory.Code;

internal static unsafe class FunctionHookGate
{
    private sealed class GateContext
    {
        public readonly ref struct HookGuard
        {
            public bool IsOwned => _context != null;

            private readonly GateContext? _context;

            public HookGuard(GateContext context)
            {
                if (context._guarded)
                    return;

                _context = context;

                context._guarded = true;
            }

            public void Dispose()
            {
                if (_context != null)
                    _context._guarded = false;
            }
        }

        private sealed class StackNormalizer : IDisposable
        {
            // This is used by CallTrace to restore the stack to a walkable state. This is done by setting the return
            // address slot for each gate frame to the real return address that the hook function will eventually return
            // to (disregarding the exit detour through the gate). Once the stack has been walked, the Dispose method
            // restores the stack to the (unwalkable) state where hook functions will exit through the gate.

            private readonly (GateFrame, nuint)[] _frames;

            public StackNormalizer(GateContext context)
            {
                _frames = context._frames
                    .Select(f =>
                    {
                        var addr = *f.StackAddress;

                        *f.StackAddress = f.ReturnAddress;

                        return (f, (nuint)addr);
                    })
                    .ToArray();
            }

            public void Dispose()
            {
                foreach (var (frame, addr) in _frames)
                    *frame.StackAddress = (void*)addr;
            }
        }

        public GateFrame? Current => _frames.TryPeek(out var frame) ? frame : null;

        private readonly Stack<GateFrame> _frames = new(5);

        private bool _guarded;

        public HookGuard AcquireGuard()
        {
            return new(this);
        }

        public bool IsExecuting(FunctionHook hook)
        {
            // Having multiple frames should be very uncommon in practice, and when it does happen, they should be very
            // small in number. So simply iterating over the frames like this should be fine. Avoid LINQ here since it
            // would allocate in a hot path.
            foreach (var frame in _frames)
                if (frame.Hook == hook)
                    return true;

            return false;
        }

        public void Push(FunctionHook hook, void** stackAddress)
        {
            _frames.Push(new(hook, stackAddress));
        }

        public GateFrame Pop()
        {
            return _frames.Pop();
        }

        public IDisposable NormalizeStack()
        {
            return new StackNormalizer(this);
        }
    }

    private readonly struct GateFrame
    {
        public FunctionHook Hook { get; }

        public void** StackAddress { get; }

        public void* ReturnAddress { get; }

        public GateFrame(FunctionHook hook, void** stackAddress)
        {
            Hook = hook;
            StackAddress = stackAddress;
            ReturnAddress = *stackAddress;
        }
    }

    public static FunctionHook? Hook => _context?.Current?.Hook;

    [ThreadStatic]
    private static GateContext? _context;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void* Enter(nint handle, void** rsp)
    {
        _context ??= new();

        using var guard = _context.AcquireGuard();

        // This means a hook is called while we are holding the guard to set up or tear down a gate frame.
        if (!guard.IsOwned)
            return null;

        var hook = Unsafe.As<FunctionHook>(((GCHandle)handle).Target!);

        if (!hook.IsActive)
            return null;

        // Prevent hook reentrancy. This is almost always a bug that manifests as a stack overflow or deadlock.
        if (_context.IsExecuting(hook))
            return null;

        // Push the gate frame containing the hook and the real return address. Exit will return this return address
        // later on so that we can properly return to whatever code called the target function.
        _context.Push(hook, rsp);

        return hook.HookCode;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void* Exit()
    {
        _context ??= new();

        // Acquiring the guard should always succeed here. It will keep further hooks from entering, which necessarily
        // means they will not exit either.
        using var guard = _context.AcquireGuard();

        return _context.Pop().ReturnAddress;
    }

    public static IDisposable NormalizeStack()
    {
        _context ??= new();

        return _context.NormalizeStack();
    }
}
