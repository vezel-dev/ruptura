// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Memory.Code;

internal sealed class CodeAssembler : Assembler
{
    public bool Is64Bit => Bitness == 64;

    public CodeAssembler()
        : base(Environment.Is64BitProcess ? 64 : 32)
    {
    }

    public void Assemble(CodeWriter writer, ulong rip)
    {
        _ = base.Assemble(writer, rip);
    }
}
