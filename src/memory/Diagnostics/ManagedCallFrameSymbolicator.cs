namespace Vezel.Ruptura.Memory.Diagnostics;

public sealed class ManagedCallFrameSymbolicator : CallFrameSymbolicator
{
    public static ManagedCallFrameSymbolicator Instance { get; } = new();

    ManagedCallFrameSymbolicator()
    {
    }

    public override unsafe CallFrameSymbol? Symbolicate(CallFrame frame)
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

        // TODO: Figure out a way to get source location information.
        return new((void*)method.MethodHandle.GetFunctionPointer(), sb.ToString(), null, 0, 0);
    }
}
