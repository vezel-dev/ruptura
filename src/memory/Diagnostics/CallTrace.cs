using Vezel.Ruptura.Memory.Code;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Memory.Diagnostics;

public sealed unsafe class CallTrace
{
    public IReadOnlyList<CallFrame> Frames { get; }

    private static readonly object _lock = new();

    private static readonly nint _kernel32 = NativeLibrary.Load("kernel32.dll");

    private static readonly delegate* unmanaged[Stdcall]<void*, void> _rtlCaptureContext =
        (delegate* unmanaged[Stdcall]<void*, void>)NativeLibrary.GetExport(_kernel32, "RtlCaptureContext");

    private static readonly delegate* unmanaged[Stdcall]<ulong, ulong*, void*, void*> _rtlLookupFunctionEntry =
        (delegate* unmanaged[Stdcall]<ulong, ulong*, void*, void*>)NativeLibrary.GetExport(
            _kernel32, "RtlLookupFunctionEntry");

    // This is an internal API that we are exploiting. It may or may not be present.
    private static readonly Func<nint, MethodBase?>? _getMethodFromNativeIP =
        typeof(StackFrame)
            .GetMethod(
                "GetMethodFromNativeIP",
                BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?.CreateDelegate<Func<nint, MethodBase?>>();

    private static readonly ImageMachine _machine;

    private static readonly delegate* unmanaged[Stdcall]<HANDLE, ulong, void*> _functionTableAccess;

    private static readonly delegate* unmanaged[Stdcall]<HANDLE, ulong, ulong> _getModuleBase;

    private static readonly int _error;

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

            _ = SymSetOptions(SymGetOptions() | SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_LOAD_LINES);

            var processHandle = ProcessObject.Current.SafeHandle;

            if (!SymInitializeW(processHandle, UserSearchPath: null, fInvadeProcess: true))
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
                    _ = SymCleanup(processHandle);

                    NativeLibrary.Free(_kernel32);
                }
            };
        }
    }

    private CallTrace(IReadOnlyList<CallFrame> frames)
    {
        Frames = frames;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CallTrace Capture()
    {
        return CaptureCore(
        [
            ManagedCallFrameSymbolicator.Instance,
            NativeCallFrameSymbolicator.Instance,
        ]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CallTrace Capture(params CallFrameSymbolicator[] symbolicators)
    {
        return CaptureCore(symbolicators);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CallTrace CaptureCore(IEnumerable<CallFrameSymbolicator> symbolicators)
    {
        Check.Null(symbolicators);
        Check.All(symbolicators, static sym => sym != null);

        if (_error != 0)
            throw new Win32Exception(_error);

        lock (_lock)
        {
            using (FunctionHookGate.NormalizeStack())
            {
                var context = stackalloc byte[2048]; // Way more than enough.

                _rtlCaptureContext(context);

                void* ip;
                void* sp;
                void* fp;

                // Hardcoded CONTEXT offsets to avoid needing platform-specific CONTEXT structs.
                if (Environment.Is64BitProcess)
                {
                    ip = context + 248; // RIP
                    sp = context + 152; // RSP
                    fp = context + 160; // RBP
                }
                else
                {
                    ip = context + 184; // EIP
                    sp = context + 196; // ESP
                    fp = context + 180; // EBP
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

                while (StackWalkEx(
                    (uint)_machine,
                    processHandle,
                    threadHandle,
                    ref frame,
                    context,
                    ReadMemoryRoutine: null,
                    _functionTableAccess,
                    _getModuleBase,
                    TranslateAddress: null,
                    SYM_STKWALK_DEFAULT))
                {
                    var pc = frame.AddrPC.Offset;
                    var method = _getMethodFromNativeIP?.Invoke((nint)pc);

                    // Managed code does not have an associated module, so avoid wasting time.
                    var cf = new CallFrame(
                        frame,
                        method == null ? (nint)_getModuleBase((HANDLE)processHandle.DangerousGetHandle(), pc) : 0,
                        method);

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

                // The first 3 frames are always RtlCaptureContext(), CallTrace.CaptureCore(), and CallTrace.Capture().
                cfs.RemoveRange(0, 3);

                return new([.. cfs]);
            }
        }
    }

    // Use RtlLookupFunctionEntry on 64-bit since it can pick up JIT'd code.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* FunctionTableAccess32(HANDLE process, ulong address)
    {
        return SymFunctionTableAccess64(process, address);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* FunctionTableAccess64(HANDLE process, ulong address)
    {
        ulong unused;

        return _rtlLookupFunctionEntry(address, &unused, null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static ulong GetModuleBase32(HANDLE process, ulong address)
    {
        return SymGetModuleBase64(process, address);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static ulong GetModuleBase64(HANDLE process, ulong address)
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
