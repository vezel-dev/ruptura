namespace Vezel.Ruptura.Diagnostics;

internal static class Check
{
    public static class Always
    {
        public static void Assert(
            [DoesNotReturnIf(false)] bool condition, [CallerArgumentExpression("condition")] string? expression = null)
        {
            if (!condition)
                throw new UnreachableException($"Hard assertion '{expression}' failed.");
        }
    }

    public static class Debug
    {
        [Conditional("DEBUG")]
        public static void Assert(
            [DoesNotReturnIf(false)] bool condition, [CallerArgumentExpression("condition")] string? expression = null)
        {
            if (!condition)
                throw new UnreachableException($"Debug assertion '{expression}' failed.");
        }
    }

    public static class Release
    {
        [Conditional("RELEASE")]
        public static void Assert(
            [DoesNotReturnIf(false)] bool condition, [CallerArgumentExpression("condition")] string? expression = null)
        {
            if (!condition)
                throw new UnreachableException($"Release assertion '{expression}' failed.");
        }
    }

    public static void Argument([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
            throw new ArgumentException(null);
    }

    // TODO: https://github.com/dotnet/csharplang/issues/1148
    public static void Argument([DoesNotReturnIf(false)] bool condition, string name)
    {
        if (!condition)
            throw new ArgumentException(null, name);
    }

    public static void Argument<T>(
        [DoesNotReturnIf(false)] bool condition,
        scoped in T value,
        [CallerArgumentExpression("value")] string? name = null)
    {
        _ = value;

        Argument(condition, name!);
    }

    public static void Null([NotNull] object? value, [CallerArgumentExpression("value")] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(value, name);
    }

    public static unsafe void Null(void* value, [CallerArgumentExpression("value")] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(value, name);
    }

    public static void Range<T>(
        [DoesNotReturnIf(false)] bool condition,
        scoped in T value,
        [CallerArgumentExpression("value")] string? name = null)
    {
        _ = value;

        if (!condition)
            throw new ArgumentOutOfRangeException(name);
    }

    public static void Operation([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
            throw new InvalidOperationException();
    }

    public static void Operation([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void OperationSupported([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
            throw new NotSupportedException();
    }

    public static void PlatformSupported([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
            throw new PlatformNotSupportedException();
    }

    public static void Usable([DoesNotReturnIf(false)] bool condition, object instance)
    {
        ObjectDisposedException.ThrowIf(!condition, instance);
    }

    public static void ForEach<T>(IEnumerable<T> collection, Action<T> action)
    {
        foreach (var item in collection)
            action(item);
    }
}
