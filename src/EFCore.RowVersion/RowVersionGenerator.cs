using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EFCore.RowVersion;

/// <summary>Model for a type decorated with <c>[Optimistic]</c>.</summary>
internal sealed class TypeModel
{
    public string Namespace { get; set; } = "";
    public string TypeName { get; set; } = "";
}

/// <summary>Source generator that adds <c>RowVersion</c> and <c>IOptimisticEntity</c> to types decorated with <c>[Optimistic]</c>.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class RowVersionGenerator : IIncrementalGenerator
{
    private const string AttributeFqn = "EFCore.RowVersion.OptimisticAttribute";

    private static readonly DiagnosticDescriptor Rvrs001 = new(
        id: "RVRS001",
        title: "Type must be partial",
        messageFormat: "'{0}' is decorated with [Optimistic] but is not declared as partial. Declare it as partial to allow source generation.",
        category: "EFCore.RowVersion",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("EFCore.RowVersion.Attribute.g.cs",    SourceText.From(Emitter.AttributeSource, Encoding.UTF8));
            ctx.AddSource("EFCore.RowVersion.Interface.g.cs",    SourceText.From(Emitter.InterfaceSource,  Encoding.UTF8));
            ctx.AddSource("EFCore.RowVersion.Core.g.cs",         SourceText.From(Emitter.CoreSource,       Encoding.UTF8));
        });

        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var syntax = (ClassDeclarationSyntax)ctx.TargetNode;
                    var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
                    var isPartial = syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
                    var ns       = symbol.ContainingNamespace?.ToDisplayString() ?? "";
                    return (syntax, symbol, isPartial, ns);
                })
            .WithTrackingName("OptimisticCandidates");

        context.RegisterSourceOutput(candidates, static (spc, item) =>
        {
            var (syntax, symbol, isPartial, ns) = item;

            if (!isPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rvrs001, syntax.Identifier.GetLocation(), symbol.Name));
                return;
            }

            var model = new TypeModel { TypeName = symbol.Name, Namespace = ns };
            var source = string.IsNullOrEmpty(ns) ? Emitter.EmitTopLevel(model.TypeName) : Emitter.Emit(model);
            spc.AddSource($"EFCore.RowVersion.{symbol.Name}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }
}
