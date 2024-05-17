// SPDX-License-Identifier: 0BSD

using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;
using static Windows.Win32.WindowsPInvoke;

namespace Vezel.Ruptura.Memory.Diagnostics;

public sealed class NativeCallFrameSymbolicator : CallFrameSymbolicator
{
    public static NativeCallFrameSymbolicator Instance { get; } = new();

    private static readonly ProcessObject _process = ProcessObject.Current;

    private NativeCallFrameSymbolicator()
    {
    }

    protected internal override unsafe CallFrameSymbol? Symbolicate(CallFrame frame)
    {
        var ip = (nuint)frame.IP;
        var context = frame.Frame.InlineFrameContext;

        var symBuffer = stackalloc byte[sizeof(SYMBOL_INFOW) + sizeof(char) * ((int)MAX_SYM_NAME + 1)];

        var symInfo = (SYMBOL_INFOW*)symBuffer;

        symInfo->SizeOfStruct = (uint)sizeof(SYMBOL_INFOW);
        symInfo->MaxNameLen = MAX_SYM_NAME;

        ulong disp;

        if (!SymFromInlineContextW(_process.SafeHandle, ip, context, &disp, symInfo))
            return null;

        var lineInfo = new IMAGEHLP_LINEW64
        {
            SizeOfStruct = (uint)sizeof(IMAGEHLP_LINEW64),
        };
        var column = 0u;

        _ = SymGetLineFromInlineContextW(
            (HANDLE)_process.SafeHandle.DangerousGetHandle(), ip, context, 0, &column, &lineInfo);

        fixed (char* p = &symInfo->Name[0])
            return new(
                (void*)symInfo->Address, new(p), lineInfo.FileName.ToString(), (int)lineInfo.LineNumber, (int)column);
    }
}
