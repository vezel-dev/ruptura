// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Hosting;

public interface IInjectedProgram
{
    public static abstract Task<int> RunAsync(InjectedProgramContext context, ReadOnlyMemory<string> args);
}
