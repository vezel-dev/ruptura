// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Memory.Code;

internal sealed unsafe class RawCodeReader : CodeReader
{
    private byte* _address;

    public RawCodeReader(byte* address)
    {
        _address = address;
    }

    public override int ReadByte()
    {
        return *_address++;
    }
}
