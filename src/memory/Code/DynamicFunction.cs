// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Memory.Code;

public sealed unsafe class DynamicFunction : IDisposable
{
    public void* Code
    {
        get
        {
            Check.Usable(_allocation != null, this);

            return _allocation.Code;
        }
    }

    private CodeAllocation? _allocation;

    private DynamicFunction(CodeAllocation allocation)
    {
        _allocation = allocation;
    }

    public static DynamicFunction Create(
        CodeManager manager, Action<Assembler> assembler, CodePlacement? placement = null)
    {
        Check.Null(manager);
        Check.Null(assembler);

        var asm = new CodeAssembler();

        assembler(asm);

        var nullWriter = new NullCodeWriter();

        // Do an initial assembly pass to estimate how much memory we will need.
        asm.Assemble(nullWriter, 0);

        var len = nullWriter.Length * 2; // Usually way too much, but safe.
        var alloc = manager.Allocate(len, placement ?? CodePlacement.Anywhere);

        try
        {
            // Now assemble for real with a known RIP value.
            asm.Assemble(new RawCodeWriter((byte*)alloc.Code), (nuint)alloc.Code);

            // Flush the instruction cache and mark the code executable.
            alloc.Commit();

            return new(alloc);
        }
        catch (Exception)
        {
            alloc.Dispose();

            throw;
        }
    }

    public void Dispose()
    {
        _allocation?.Dispose();

        _allocation = null;
    }
}
