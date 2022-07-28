namespace Vezel.Ruptura.Memory.Diagnostics;

public sealed class ManagedCallFrameSymbolicator : CallFrameSymbolicator
{
    public static ManagedCallFrameSymbolicator Instance { get; } = new();

    // These are internal APIs that we are exploiting. They may or may not be present.

    private static readonly Func<DynamicMethod, RuntimeMethodHandle>? _getMethodDescriptor =
        typeof(DynamicMethod)
            .GetMethod(
                "GetMethodDescriptor",
                BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic)
            ?.CreateDelegate<Func<DynamicMethod, RuntimeMethodHandle>>();

    private static readonly FieldInfo? _owner =
        typeof(DynamicMethod)
            .GetNestedType("RTDynamicMethod", BindingFlags.DeclaredOnly | BindingFlags.NonPublic)
            ?.GetField("m_owner", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic);

    private ManagedCallFrameSymbolicator()
    {
    }

    protected internal override unsafe CallFrameSymbol? Symbolicate(CallFrame frame)
    {
        if (frame.ManagedMethod is not MethodBase method)
            return null;

        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        // TODO: We could do a better job stringifying generic types.

        if (method is MethodInfo info)
        {
            _ = sb.Append(culture, $"{info.ReturnType.Name} ");

            if (method.DeclaringType is Type decl)
                _ = sb.Append(culture, $"{decl.FullName}.");
        }
        else
        {
            // It must be a constructor. That means it has a declaring type, but no return type. For static
            // constructors, we give it a void return type, while instance constructors get the declaring type. This is
            // probably the most intuitive approach for the average C# programmer.
            var decl = method.DeclaringType!;

            _ = sb.Append(culture, $"{(method.IsStatic ? typeof(void) : decl).FullName} {decl.FullName}.");
        }

        _ = sb.Append(culture, $"{method.Name}(");

        var parms = method.GetParameters();

        for (var i = 0; i < parms.Length; i++)
        {
            var param = parms[i];

            _ = sb.Append(culture, $"{param.ParameterType.Name} {param.Name}");

            if (i != parms.Length - 1)
                _ = sb.Append(", ");
        }

        _ = sb.Append(')');

        RuntimeMethodHandle? rmh;

        try
        {
            rmh = method.MethodHandle;
        }
        catch (InvalidOperationException)
        {
            // This gets thrown if we are dealing with a DynamicMethod. The trick, though, is that method is actually an
            // instance of RTDynamicMethod, an internal class nested in DynamicMethod and deriving from MethodInfo. Do
            // our best to get at its method handle, but if we cannot, we just use the frame IP below.

            rmh = (_owner, _getMethodDescriptor) is (not null, not null)
                ? _getMethodDescriptor((DynamicMethod)_owner.GetValue(method)!)
                : null;
        }

        // TODO: Figure out a way to get source location information.
        return new((void*)(rmh?.GetFunctionPointer() ?? (nint)frame.IP), sb.ToString(), null, 0, 0);
    }
}
