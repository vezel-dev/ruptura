// SPDX-License-Identifier: 0BSD

namespace Vezel.Ruptura.Injection;

public class InjectionException : Exception
{
    public InjectionException()
        : this("An unknown injection error occurred.")
    {
    }

    public InjectionException(string? message)
        : base(message)
    {
    }

    public InjectionException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
