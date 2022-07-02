namespace Vezel.Ruptura.Injection.Threading;

sealed class ThreadWaitHandle : WaitHandle
{
    public ThreadWaitHandle(SafeWaitHandle handle)
    {
        this.SetSafeWaitHandle(handle);
    }
}
