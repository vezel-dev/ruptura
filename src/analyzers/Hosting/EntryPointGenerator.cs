namespace Vezel.Ruptura.Analyzers.Hosting;

[Generator(LanguageNames.CSharp)]
public sealed class EntryPointGenerator : ISourceGenerator
{
    private sealed class InjectedProgramTypeSyntaxReceiver : ISyntaxContextReceiver
    {
        public ImmutableArray<INamedTypeSymbol> InjectedProgramSymbols { get; private set; } =
            ImmutableArray<INamedTypeSymbol>.Empty;

        private INamedTypeSymbol? _interface;

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            var sema = context.SemanticModel;

            _interface ??= sema.Compilation.GetTypeByMetadataName("Vezel.Ruptura.Hosting.IInjectedProgram");

            if (context.Node is TypeDeclarationSyntax type)
                if (sema.GetDeclaredSymbol(type) is INamedTypeSymbol sym)
                    if (sym.AllInterfaces.Any(sym => sym.Equals(_interface, SymbolEqualityComparer.Default)))
                        InjectedProgramSymbols = InjectedProgramSymbols.Add(sym);
        }
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new InjectedProgramTypeSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var syms = ((InjectedProgramTypeSyntaxReceiver)context.SyntaxContextReceiver!).InjectedProgramSymbols;

        if (syms.IsEmpty)
            return;

        if (syms.Length != 1)
            foreach (var program in syms)
                foreach (var loc in program.Locations)
                    context.ReportDiagnostic(
                        Diagnostic.Create(DiagnosticDescriptors.AvoidMultipleInjectedProgramTypes, loc));

        if (context.Compilation.GetEntryPoint(context.CancellationToken) is IMethodSymbol entry)
        {
            foreach (var loc in entry.Locations)
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.AvoidImplementingEntryPoint, loc, entry));

            return;
        }

        var name = syms[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        context.AddSource(
            "GeneratedProgram.g.cs",
            $$"""
            [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
            static class GeneratedProgram
            {
                static global::System.Threading.Tasks.Task<int> Main(string[] args)
                {
                    return global::Vezel.Ruptura.Hosting.InjectedProgramHost.RunAsync<{{name}}>(args);
                }
            }
            """);
    }
}
