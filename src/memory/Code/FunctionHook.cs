using static Iced.Intel.AssemblerRegisters;

namespace Vezel.Ruptura.Memory.Code;

public sealed unsafe class FunctionHook : IDisposable
{
    // Hijacks the prologue of a target function by replacing it with a relative jump to a trampoline. The trampoline
    // performs an absolute jump to the hook function. Additionally, a separate section of the trampoline contains the
    // destroyed prologue instructions and a jump to the remainder of the original function - this section can be used
    // to call the original function unmodified. The hook can be atomically enabled/disabled without entirely removing
    // it. It is the caller's responsibility to ensure that the target function can accommodate the relative jump to
    // the trampoline, which requires 5 bytes.

    [StructLayout(LayoutKind.Sequential)]
    struct HookTrampoline
    {
        // This jumps to the address in _jumpTarget, which is either the target function or hook function.
        public fixed byte CallHook[32];

        // This always jumps to the target function (after running prologue instructions that were destroyed).
        public fixed byte CallTarget[32];
    }

    const int JumpInstructionSize = 5;

    public void* TargetCode => _allocation != null ? _target : throw new ObjectDisposedException(GetType().Name);

    public void* HookCode => _allocation != null ? _hook : throw new ObjectDisposedException(GetType().Name);

    public void* TrampolineCode =>
        _allocation != null
            ? ((HookTrampoline*)_allocation.Code)->CallTarget
            : throw new ObjectDisposedException(GetType().Name);

    public bool IsActive
    {
        get
        {
            _ = _allocation != null ? true : throw new ObjectDisposedException(GetType().Name);

            return Volatile.Read(ref *(nuint*)_jumpTarget) == (nuint)HookCode;
        }

        set
        {
            _ = _allocation != null ? true : throw new ObjectDisposedException(GetType().Name);

            Volatile.Write(ref *(nuint*)_jumpTarget, (nuint)(value ? HookCode : TrampolineCode));
        }
    }

    static readonly ProcessObject _process = ProcessObject.Current;

    readonly void* _target;

    readonly void* _hook;

    readonly ReadOnlyMemory<Instruction> _prologue;

    readonly void** _jumpTarget;

    CodeAllocation? _allocation;

    FunctionHook(
        void* target,
        void* hook,
        ReadOnlyMemory<Instruction> prologue,
        CodeAllocation allocation,
        void** jumpTarget)
    {
        _target = target;
        _hook = hook;
        _prologue = prologue;
        _allocation = allocation;
        _jumpTarget = jumpTarget;
    }

    public static FunctionHook Create(CodeManager manager, void* target, void* hook)
    {
        var alloc = default(CodeAllocation);
        var jumpTarget = default(void**);

        try
        {
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

            var prologue = new List<Instruction>();
            var prologueSize = 0;

            // Save the original prologue. We need this to construct the trampoline and when disposing.
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

            alloc = manager.Allocate(sizeof(HookTrampoline), placement);
            jumpTarget = (void**)NativeMemory.Alloc((nuint)sizeof(void**));

            var tramp = (HookTrampoline*)alloc.Code;

            // Generate a code stub that jumps to whatever location is at *jumpTarget, which is either going to be the
            // hook function or the stub for calling the original function (that we generate below).
            {
                var asm = new CodeAssembler();

                var jump = (nuint)jumpTarget;

                if (asm.Is64Bit)
                {
                    // Abuse the ret instruction to jump to an absolute address that we put on the stack. The push and
                    // xchg ensure that whatever was in rax is preserved.
                    asm.push(rax);
                    asm.mov(rax, jump);
                    asm.mov(rax, __qword_ptr[rax]);
                    asm.xchg(__qword_ptr[rsp], rax);
                    asm.ret();
                }
                else
                    asm.jmp(__dword_ptr[jump]);

                asm.Assemble(new RawCodeWriter(tramp->CallHook), (nuint)tramp->CallHook);
            }

            // Assemble a code stub that, effectively, calls the original, unmodified target function. We execute the
            // instructions that we are going to destroy in the prologue and then jump to the remainder of the target
            // function.
            {
                var asm = new CodeAssembler();

                // It is possible that the original instructions contained one or more displacements. On 64-bit, there
                // is certainly a chance that those instructions will no longer assemble correctly here due to range
                // issues. This is technically a solvable problem, but it would be a huge amount of work. For now, we
                // just let a situation like this result in an exception when assembling.
                foreach (var insn in prologue)
                    asm.AddInstruction(insn);

                var remainder = (nuint)((byte*)target + prologueSize);

                // We are now in the middle of the original code from the target function. We are extremely limited in
                // what we can do here; all registers must be preserved.
                if (asm.Is64Bit)
                {
                    // Same push/xchg/ret trick as above.
                    asm.push(rax);
                    asm.mov(rax, remainder);
                    asm.xchg(__qword_ptr[rsp], rax);
                    asm.ret();
                }
                else
                    asm.jmp(remainder);

                asm.Assemble(new RawCodeWriter(tramp->CallTarget), (nuint)tramp->CallTarget);
            }

            alloc.Commit();

            // Finally, patch the function we are hooking.
            {
                var access = _process.ProtectMemory(target, JumpInstructionSize, MemoryAccess.ExecuteReadWrite);

                try
                {
                    var asm = new CodeAssembler();

                    // 32-bit relative jump. This is always encoded as 5 bytes. The immediate has to be computed
                    // differently for 32-bit (simple 32-bit offset) and 64-bit (sign-extended to 64-bit offset), but
                    // Iced takes care of that for us.
                    asm.jmp((nuint)tramp->CallHook);

                    asm.Assemble(new RawCodeWriter((byte*)target), (nuint)target);
                }
                finally
                {
                    _ = _process.ProtectMemory(target, JumpInstructionSize, access);
                }
            }

            // TODO: It is technically possible for an OutOfMemoryException to happen here, however unlikely. We should
            // revert the patch to the target function in that case.
            return new(target, hook, prologue.ToArray(), alloc, jumpTarget)
            {
                IsActive = false, // Initializes *jumpTarget to tramp->CallTarget.
            };
        }
        catch (Exception)
        {
            NativeMemory.Free(jumpTarget);
            alloc?.Dispose();

            throw;
        }
    }

    public void Dispose()
    {
        if (_allocation == null)
            return;

        var access = _process.ProtectMemory((byte*)_target, JumpInstructionSize, MemoryAccess.ExecuteReadWrite);

        try
        {
            var asm = new CodeAssembler();

            foreach (var insn in _prologue.Span)
                asm.AddInstruction(insn);

            // Restore the original prologue.
            asm.Assemble(new RawCodeWriter((byte*)_target), (nuint)_target);
        }
        finally
        {
            _ = _process.ProtectMemory((byte*)_target, JumpInstructionSize, access);
        }

        _allocation?.Dispose();
        NativeMemory.Free(_jumpTarget);

        _allocation = null;
    }
}
