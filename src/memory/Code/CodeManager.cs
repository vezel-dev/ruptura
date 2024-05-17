// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Memory.Code;

[SuppressMessage("", "CA1063")]
public abstract unsafe class CodeManager : IDisposable
{
    public abstract void Dispose();

    public abstract CodeAllocation Allocate(nint length, CodePlacement placement);
}
