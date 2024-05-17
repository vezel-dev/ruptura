// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Memory.Code;

internal sealed class NullCodeWriter : CodeWriter
{
    public nint Length { get; private set; }

    public override void WriteByte(byte value)
    {
        Length++;
    }
}
