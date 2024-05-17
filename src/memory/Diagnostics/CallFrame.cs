// SPDX-License-Identifier: 0BSD

using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Memory.Diagnostics;

public sealed unsafe class CallFrame
{
    public void* IP => (void*)_frame.AddrPC.Offset;

    public void* SP => (void*)_frame.AddrStack.Offset;

    public void* FP => (void*)_frame.AddrFrame.Offset;

    public nint ModuleHandle { get; }

    public MethodBase? ManagedMethod { get; }

    public CallFrameSymbol? Symbol { get; internal set; }

    internal ref STACKFRAME_EX Frame => ref _frame;

    private STACKFRAME_EX _frame;

    internal CallFrame(in STACKFRAME_EX frame, nint moduleHandle, MethodBase? managedMethod)
    {
        _frame = frame;
        ModuleHandle = moduleHandle;
        ManagedMethod = managedMethod;
    }

    [SuppressMessage("", "CA1308")]
    [UnconditionalSuppressMessage("", "IL3002")]
    public override string ToString()
    {
        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        _ = sb.Append(culture, $"0x{(nuint)IP:x}: ");

        if (Symbol != null)
        {
            _ = sb.Append(Symbol.Name);

            // TODO: To do this correctly for managed methods, we have to disassemble the prestub and follow the jump.
            if (ManagedMethod == null && (byte*)IP - (byte*)Symbol.Address is var offset and not 0)
                _ = sb.Append(culture, $"+0x{offset:x}");
        }
        else
            _ = sb.Append("<unknown>");

        if (ManagedMethod != null)
            _ = sb.Append(culture, $" in {ManagedMethod.Module.Name}");
        else if (ModuleHandle != 0)
        {
            var handle = (HMODULE)ModuleHandle;
            var length = MAX_PATH;
            var buffer = (char*)NativeMemory.Alloc(sizeof(char) * length);

            try
            {
                uint ret;

                while ((ret = GetModuleFileNameW(handle, buffer, length)) == length)
                {
                    length *= 2;

                    buffer = (char*)NativeMemory.Realloc(buffer, sizeof(char) * length);
                }

                if (ret != 0)
                {
                    _ = sb.Append(culture, $" in {Path.GetFileName(new string(buffer)).ToLowerInvariant()}");
                    _ = sb.Append(culture, $"+0x{(nuint)((byte*)IP - (byte*)ModuleHandle):x}");
                }
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }

        if (Symbol?.FileName is string file)
        {
            _ = sb.Append(culture, $" at {file}");

            if (Symbol.Line is var line and not 0)
                _ = sb.Append(culture, $":{line}");
        }

        return sb.ToString();
    }
}
