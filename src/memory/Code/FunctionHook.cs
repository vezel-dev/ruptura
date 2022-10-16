using static Iced.Intel.AssemblerRegisters;

namespace Vezel.Ruptura.Memory.Code;

public sealed unsafe class FunctionHook : IDisposable
{
    // Hijacks the prologue of a target function by replacing it with a relative jump to a trampoline. The trampoline
    // consists of two parts: The first is a hook gate that, among other things, guards against some common reentrancy
    // and deadlock problems and makes the hook's state object (if any) available, after which it calls into the hook
    // function. The second is a section of code that contains the destroyed prologue instructions from the target
    // function and an absolute jump to the remainder of the target function - this can be used to call the original
    // target function unhooked. The hook can be atomically enabled/disabled without entirely removing it. It is the
    // caller's responsibility to ensure that the target function can accommodate the relative jump to the hook gate,
    // which requires 5 bytes.

    [StructLayout(LayoutKind.Sequential)]
    private struct HookTrampoline
    {
        public fixed byte CallGate[256];

        public fixed byte CallOriginal[64];
    }

    private const int JumpInstructionSize = 5;

    public static FunctionHook Current =>
        FunctionHookGate.Hook ?? throw new InvalidOperationException("Not currently executing a function hook.");

    public void* TargetCode
    {
        get
        {
            Check.Usable(_allocation != null, this);

            return _target;
        }
    }

    public void* HookCode
    {
        get
        {
            Check.Usable(_allocation != null, this);

            return _hook;
        }
    }

    public void* OriginalCode
    {
        get
        {
            Check.Usable(_allocation != null, this);

            return ((HookTrampoline*)_allocation.Code)->CallOriginal;
        }
    }

    public object State =>
        _state ?? throw new InvalidOperationException("No state was associated with this function hook.");

    public bool IsActive
    {
        get
        {
            Check.Usable(_allocation != null, this);

            return _active;
        }

        set
        {
            Check.Usable(_allocation != null, this);

            _active = value;
        }
    }

    private static readonly ProcessObject _process = ProcessObject.Current;

    private readonly GCHandle _handle;

    private readonly void* _target;

    private readonly void* _hook;

    private readonly object? _state;

    private readonly ReadOnlyMemory<byte> _prologue;

    private CodeAllocation? _allocation;

    private bool _active;

    private FunctionHook(
        void* target,
        void* hook,
        object? state,
        ReadOnlySpan<byte> prologue,
        CodeAllocation allocation)
    {
        _handle = GCHandle.Alloc(this);
        _target = target;
        _hook = hook;
        _state = state;
        _prologue = prologue.ToArray();
        _allocation = allocation;
    }

    public static FunctionHook Create(CodeManager manager, void* target, void* hook, object? state = null)
    {
        Check.Null(manager);
        Check.Null(target);
        Check.Null(hook);

        var prologue = new List<Instruction>();
        var prologueSize = 0;

        {
            var disasm = new CodeDisassembler(new RawCodeReader((byte*)target), (nuint)target);

            // Disassemble until we have at least 5 bytes for the jump instruction. Note that the jump may end up
            // being smaller in the end since Iced optimizes it if possible.
            while (prologueSize < JumpInstructionSize)
            {
                disasm.Disassemble(out var insn);
                prologue.Add(insn);

                prologueSize += insn.Length;
            }
        }

        var placement = CodePlacement.Anywhere;

        // The trampoline is always reachable on 32-bit, so no requirements in that case.
        if (Environment.Is64BitProcess)
        {
            var baseAddr = (byte*)target + JumpInstructionSize;

            var low = baseAddr + int.MinValue;

            if (low > baseAddr)
                low = null; // Underflow, i.e. the bottom of the address space is reachable.

            var high = baseAddr + int.MaxValue;

            if (high < baseAddr)
                high = (byte*)null - 1; // Overflow, i.e. the top of the address space is reachable.

            placement = CodePlacement.Range(low, high);
        }

        var alloc = manager.Allocate(sizeof(HookTrampoline), placement);

        FunctionHook obj;

        try
        {
            obj = new FunctionHook(target, hook, state, new(target, prologueSize), alloc);
        }
        catch (Exception)
        {
            alloc.Dispose();

            throw;
        }

        // From this point on, all resources are owned by obj.

        try
        {
            var tramp = (HookTrampoline*)alloc.Code;

            {
                var asm = new CodeAssembler();

                var original = asm.CreateLabel("original");
                var exit = asm.CreateLabel("exit");
                var done = asm.CreateLabel("done");

                if (asm.Is64Bit)
                {
                    // TODO: Support vectorcall (X/YMM0 - X/YMM5 for arguments, X/YMM0 - X/YMM3 for results)?

                    // Alignment, shadow space, 4 saved GPRs, and 4 saved FPRs.
                    var frameSize = sizeof(ulong) * 9 + sizeof(Vector128<float>) * 4;

                    var gprs = sizeof(ulong) * 4; // Skip over shadow space.
                    var fprs = gprs + sizeof(ulong) * 4; // Skip over shadow space and 4 saved GPRs.

                    asm.sub(rsp, frameSize);

                    // RCX, RDX, R8, R9, XMM0, XMM1, XMM2, and XMM3 are argument registers in the x64 ABI. We have no
                    // idea how many parameters the target function has, so we have to just save all of them.
                    asm.mov(__qword_ptr[rsp + gprs + sizeof(ulong) * 3], rcx);
                    asm.mov(__qword_ptr[rsp + gprs + sizeof(ulong) * 2], rdx);
                    asm.mov(__qword_ptr[rsp + gprs + sizeof(ulong) * 1], r8);
                    asm.mov(__qword_ptr[rsp + gprs + sizeof(ulong) * 0], r9);
                    asm.movaps(__xmmword_ptr[rsp + fprs + sizeof(Vector128<float>) * 3], xmm0);
                    asm.movaps(__xmmword_ptr[rsp + fprs + sizeof(Vector128<float>) * 2], xmm1);
                    asm.movaps(__xmmword_ptr[rsp + fprs + sizeof(Vector128<float>) * 1], xmm2);
                    asm.movaps(__xmmword_ptr[rsp + fprs + sizeof(Vector128<float>) * 0], xmm3);

                    // Are we holding the loader lock? We absolutely should not execute managed code (including the hook
                    // gate code) in this case, so bail out early and call the original target function.
                    asm.mov(rax, __.gs[0x60]);              // TEB->PEB
                    asm.mov(rax, __qword_ptr[rax + 0x110]); // PEB->LoaderLock
                    asm.mov(rax, __qword_ptr[rax + 0x16]);  // LoaderLock->OwningThread
                    asm.cmp(rax, __.gs[0x48]);              // TEB->ClientId.UniqueThread
                    asm.je(original);

                    // Enter through the hook gate. It will return the address of the hook function if we are actually
                    // going to call it, or null if we are going to call the original target function.
                    asm.mov(rcx, (nint)obj._handle);
                    asm.lea(rdx, __qword_ptr[rsp + frameSize]);
                    asm.mov(rax, (nuint)(delegate* unmanaged[Cdecl]<nint, void**, void*>)&FunctionHookGate.Enter);
                    asm.call(rax);
                    asm.cmp(rax, 0);
                    asm.je(original);

                    // We now know that we are going to call the hook function. Hijack the current return address so
                    // that the hook function returns to the code below. This gives us the opportunity to exit through
                    // the gate. RAX contains the hook function address, so use RCX as scratch here.
                    asm.lea(rcx, __qword_ptr[exit]);
                    asm.mov(__qword_ptr[rsp + frameSize], rcx);
                    asm.jmp(done);

                    asm.Label(ref original);
                    {
                        asm.mov(rax, (nuint)tramp->CallOriginal);
                    }

                    asm.Label(ref done);
                    {
                        // We are about to tail call either the original target function or the hook function. Restore
                        // the (potential) arguments we saved earlier.
                        asm.mov(r9, __qword_ptr[rsp + gprs + sizeof(ulong) * 0]);
                        asm.mov(r8, __qword_ptr[rsp + gprs + sizeof(ulong) * 1]);
                        asm.mov(rdx, __qword_ptr[rsp + gprs + sizeof(ulong) * 2]);
                        asm.mov(rcx, __qword_ptr[rsp + gprs + sizeof(ulong) * 3]);
                        asm.movaps(xmm3, __xmmword_ptr[rsp + fprs + sizeof(Vector128<float>) * 0]);
                        asm.movaps(xmm2, __xmmword_ptr[rsp + fprs + sizeof(Vector128<float>) * 1]);
                        asm.movaps(xmm1, __xmmword_ptr[rsp + fprs + sizeof(Vector128<float>) * 2]);
                        asm.movaps(xmm0, __xmmword_ptr[rsp + fprs + sizeof(Vector128<float>) * 3]);

                        asm.add(rsp, frameSize);

                        // It should be noted that using a tail call here is semantically very important. While we can
                        // restore the argument registers as we do above, we simply have no way of knowing how many
                        // stack-based parameters the target function might have. So, here, we rely on the fact that any
                        // code that has called us does know that and has allocated sufficient stack space.
                        //
                        // Using a call instruction here would mean pushing a return address on the stack (in addition
                        // to the one that is already there), which would cause the target function to read its
                        // stack-based arguments from the wrong stack locations.
                        //
                        // A separate, future version of this API might require the user to supply a function prototype
                        // so that we can handle the calling convention precisely, and avoid the need for the tail call
                        // and return address hijacking...
                        asm.jmp(rax);
                    }

                    // This is where we end up when the hook function returns. Exit through the hook gate and return.
                    asm.Label(ref exit);
                    {
                        // Alignment, shadow space, saved RAX, and saved XMM0.
                        var exitFrameSize = sizeof(ulong) * 6 + sizeof(Vector128<float>);

                        var exitXmm0 = sizeof(ulong) * 4; // Skip over shadow space.
                        var exitRax = exitXmm0 + sizeof(ulong); // Skip over shadow space and saved XMM0.

                        asm.sub(rsp, exitFrameSize);

                        // RAX and XMM0 are return value registers in the x64 ABI. We need to save them here since the
                        // hook gate exit function might mess with them, and we have no idea whether the target function
                        // has a return value.
                        asm.movaps(__xmmword_ptr[rsp + exitXmm0], xmm0);
                        asm.mov(__qword_ptr[rsp + exitRax], rax);

                        // Exit through the hook gate. It will return the original return address that was captured
                        // when entering through the hook gate.
                        asm.mov(rax, (nuint)(delegate* unmanaged[Cdecl]<void*>)&FunctionHookGate.Exit);
                        asm.call(rax);
                        asm.mov(rcx, rax);

                        // Restore the (potential) return value we saved earlier.
                        asm.movaps(xmm0, __xmmword_ptr[rsp + exitXmm0]);
                        asm.mov(rax, __qword_ptr[rsp + exitRax]);

                        asm.add(rsp, exitFrameSize);

                        // We are finally done. Return to whatever code called the target function.
                        asm.jmp(rcx);
                    }
                }
                else
                    throw new NotImplementedException(); // TODO: Implement 32-bit hook gate.

                asm.Assemble(new RawCodeWriter(tramp->CallGate), (nuint)tramp->CallGate);
            }

            {
                var asm = new CodeAssembler();

                // It is possible that the original instructions contained one or more displacements. On 64-bit, there
                // is certainly a chance that those instructions will no longer assemble correctly here due to range
                // issues. This is technically a solvable problem, but it would be a huge amount of work. For now, we
                // just let a situation like this result in an exception when assembling.
                foreach (var insn in prologue)
                    asm.AddInstruction(insn);

                // We are now logically in the middle of the original code from the target function. We are extremely
                // limited in what we can do here; all registers must be preserved. Iced will expand this to a jump that
                // can reach the entire address space without dirtying registers, e.g. by embedding the jump target
                // after the instruction and using an indirect jump if necessary.
                asm.jmp((nuint)((byte*)target + prologueSize));

                asm.Assemble(new RawCodeWriter(tramp->CallOriginal), (nuint)tramp->CallOriginal);
            }

            alloc.Commit();

            var access = _process.ProtectMemory(target, JumpInstructionSize, MemoryAccess.ExecuteReadWrite);

            try
            {
                var asm = new CodeAssembler();

                // 32-bit relative jump. This is always encoded as 5 bytes. The immediate has to be computed
                // differently for 32-bit (simple 32-bit offset) and 64-bit (sign-extended to 64-bit offset), but
                // Iced takes care of that for us.
                asm.jmp((nuint)tramp->CallGate);

                asm.Assemble(new RawCodeWriter((byte*)target), (nuint)target);
            }
            finally
            {
                _ = _process.ProtectMemory(target, JumpInstructionSize, access);
            }
        }
        catch (Exception)
        {
            obj.Dispose();

            throw;
        }

        return obj;
    }

    public void Dispose()
    {
        if (_allocation == null)
            return;

        // Ensure that the code below cannot cause the hook to be invoked.
        _active = false;

        var access = _process.ProtectMemory((byte*)_target, JumpInstructionSize, MemoryAccess.ExecuteReadWrite);

        try
        {
            _prologue.Span.CopyTo(new(_target, _prologue.Length));
        }
        finally
        {
            _ = _process.ProtectMemory((byte*)_target, JumpInstructionSize, access);
        }

        _allocation.Dispose();
        _handle.Free();

        _allocation = null;
    }
}
