// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Injection.Threading;

internal sealed class ThreadWaitHandle : WaitHandle
{
    public ThreadWaitHandle(SafeWaitHandle handle)
    {
        this.SetSafeWaitHandle(handle);
    }
}
