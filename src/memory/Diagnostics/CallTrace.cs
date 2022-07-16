using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;
using Win32 = Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Memory.Diagnostics;

public sealed unsafe class CallTrace
{
    public IReadOnlyList<CallFrame> Frames { get; }

    static readonly object _lock = new();

    static readonly nint _kernel32 = NativeLibrary.Load("kernel32.dll");

    static readonly delegate* unmanaged[Stdcall]<void*, void> _rtlCaptureContext =
        (delegate* unmanaged[Stdcall]<void*, void>)NativeLibrary.GetExport(_kernel32, "RtlCaptureContext");

    static readonly delegate* unmanaged[Stdcall]<ulong, ulong*, void*, void*> _rtlLookupFunctionEntry =
        (delegate* unmanaged[Stdcall]<ulong, ulong*, void*, void*>)NativeLibrary.GetExport(
            _kernel32, "RtlLookupFunctionEntry");

    // This is an internal API that we are exploiting. It may or may not be present.
    static readonly Func<nint, MethodBase?>? _getMethodFromNativeIP =
        typeof(StackFrame)
            .GetMethod(
                "GetMethodFromNativeIP",
                BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?.CreateDelegate<Func<nint, MethodBase?>>();

    static readonly ImageMachine _machine;

    static readonly delegate* unmanaged[Stdcall]<HANDLE, ulong, void*> _functionTableAccess;

    static readonly delegate* unmanaged[Stdcall]<HANDLE, ulong, ulong> _getModuleBase;

    static readonly int _error;

    static CallTrace()
    {
        lock (_lock)
        {
            if (Environment.Is64BitProcess)
            {
                _machine = ImageMachine.X64;
                _functionTableAccess = &FunctionTableAccess64;
                _getModuleBase = &GetModuleBase64;
            }
            else
            {
                _machine = ImageMachine.X86;
                _functionTableAccess = &FunctionTableAccess32;
                _getModuleBase = &GetModuleBase32;
            }

            _ = Win32.SymSetOptions(
                Win32.SymGetOptions() | Win32.SYMOPT_UNDNAME | Win32.SYMOPT_DEFERRED_LOADS | Win32.SYMOPT_LOAD_LINES);

            var processHandle = ProcessObject.Current.SafeHandle;

            if (!Win32.SymInitializeW(processHandle, null, true))
            {
                _error = Marshal.GetLastPInvokeError();

                return;
            }

            // TODO: Is there a better place we can do this?
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                lock (_lock)
                {
                    // Nothing we can do if this fails.
                    _ = Win32.SymCleanup(processHandle);

                    NativeLibrary.Free(_kernel32);
                }
            };
        }
    }

    CallTrace(IReadOnlyList<CallFrame> frames)
    {
        Frames = frames;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CallTrace Capture()
    {
        return CaptureCore(new CallFrameSymbolicator[]
        {
            ManagedCallFrameSymbolicator.Instance,
            NativeCallFrameSymbolicator.Instance,
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CallTrace Capture(params CallFrameSymbolicator[] symbolicators)
    {
        return CaptureCore(symbolicators);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static CallTrace CaptureCore(IEnumerable<CallFrameSymbolicator> symbolicators)
    {
        ArgumentNullException.ThrowIfNull(symbolicators);
        _ = symbolicators.All(s => s != null) ? true : throw new ArgumentException(null, nameof(symbolicators));
        _ = _error == 0 ? true : throw new Win32Exception(_error);

        lock (_lock)
        {
            var context = stackalloc byte[2048]; // Way more than enough.

            _rtlCaptureContext(context);

            void* ip;
            void* sp;
            void* fp;

            // Hardcoded CONTEXT offsets to avoid needing platform-specific CONTEXT structs.
            if (Environment.Is64BitProcess)
            {
                ip = context + 248; // rip
                sp = context + 152; // rsp
                fp = context + 160; // rbp
            }
            else
            {
                ip = context + 184; // eip
                sp = context + 196; // esp
                fp = context + 180; // ebp
            }

            var frame = new STACKFRAME_EX
            {
                StackFrameSize = (uint)sizeof(STACKFRAME_EX),
                AddrPC =
                {
                    Offset = (nuint)ip,
                    Mode = ADDRESS_MODE.AddrModeFlat,
                },
                AddrStack =
                {
                    Offset = (nuint)sp,
                    Mode = ADDRESS_MODE.AddrModeFlat,
                },
                AddrFrame =
                {
                    Offset = (nuint)fp,
                    Mode = ADDRESS_MODE.AddrModeFlat,
                },
            };

            using var thread = ThreadObject.OpenCurrent();

            var processHandle = ProcessObject.Current.SafeHandle;
            var threadHandle = thread.SafeHandle;

            var cfs = new List<CallFrame>(64);

            while (Win32.StackWalkEx(
                (uint)_machine,
                processHandle,
                threadHandle,
                ref frame,
                context,
                null,
                _functionTableAccess,
                _getModuleBase,
                null,
                Win32.SYM_STKWALK_DEFAULT))
            {
                var pc = frame.AddrPC.Offset;
                var method = _getMethodFromNativeIP?.Invoke((nint)pc);

                // Managed code does not have an associated module, so avoid wasting time.
                var module = method == null
                    ? (nint)_getModuleBase((HANDLE)processHandle.DangerousGetHandle(), pc)
                    : 0;

                var cf = new CallFrame(frame, module, method);

                foreach (var symbolicator in symbolicators)
                {
                    if (symbolicator.Symbolicate(cf) is CallFrameSymbol sym)
                    {
                        cf.Symbol = sym;

                        break;
                    }
                }

                cfs.Add(cf);
            }

            // The first 3 frames are always RtlCaptureContext, CallTrace.CaptureCore, and CallTrace.Capture.
            cfs.RemoveRange(0, 3);

            return new(cfs.ToArray());
        }
    }

    // Use RtlLookupFunctionEntry on 64-bit since it can pick up JIT'd code.

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static void* FunctionTableAccess32(HANDLE process, ulong address)
    {
        return Win32.SymFunctionTableAccess64(process, address);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static void* FunctionTableAccess64(HANDLE process, ulong address)
    {
        ulong unused;

        return _rtlLookupFunctionEntry(address, &unused, null);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static ulong GetModuleBase32(HANDLE process, ulong address)
    {
        return Win32.SymGetModuleBase64(process, address);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static ulong GetModuleBase64(HANDLE process, ulong address)
    {
        ulong result;

        _ = _rtlLookupFunctionEntry(address, &result, null);

        return result;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        for (var i = 0; i < Frames.Count; i++)
        {
            _ = sb.Append(Frames[i]);

            if (i != Frames.Count - 1)
                _ = sb.AppendLine();
        }

        return sb.ToString();
    }
}
