using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SliceFx.SourceGenerator.CodeFixes;

/// <summary>
/// Code fix for SLICE071 (ASP.NET NativeAOT) and SLICE021 (WASI) diagnostics.
/// Inserts missing <c>[JsonSerializable(typeof(T))]</c> entries into the existing
/// <c>[SliceJsonContext]</c> JsonSerializerContext subclass.
///
/// Only offered when the diagnostic carries structured properties (context present,
/// types missing). The "context entirely absent" case requires manual creation and
/// does not offer this fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(JsonContextCodeFixProvider))]
[Shared]
public sealed class JsonContextCodeFixProvider : CodeFixProvider
{
    private const string MissingRootsKey = "MissingRoots";
    private const string ContextFqnKey = "ContextFqn";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["SLICE071", "SLICE021"];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];

        // Only offer fix when the diagnostic carries structured properties.
        // Diagnostics for a completely-absent context do not carry ContextFqn.
        if (!diagnostic.Properties.TryGetValue(ContextFqnKey, out var contextFqn)
            || string.IsNullOrEmpty(contextFqn)
            || !diagnostic.Properties.TryGetValue(MissingRootsKey, out var missingRootsRaw)
            || string.IsNullOrEmpty(missingRootsRaw))
        {
            return Task.CompletedTask;
        }

        var missingRoots = missingRootsRaw!.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);
        if (missingRoots.Length == 0)
        {
            return Task.CompletedTask;
        }

        var title = missingRoots.Length == 1
            ? $"Add [JsonSerializable(typeof({TrimGlobal(missingRoots[0])}))]"
            : $"Add {missingRoots.Length} missing [JsonSerializable] entries";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedSolution: ct => AddMissingJsonSerializableAsync(
                    context.Document, contextFqn!, missingRoots, ct),
                equivalenceKey: $"AddJsonSerializable:{contextFqn}"),
            diagnostic);

        return Task.CompletedTask;
    }

    private static async Task<Solution> AddMissingJsonSerializableAsync(
        Document featureDocument,
        string contextFqn,
        string[] missingRootFqns,
        CancellationToken cancellationToken)
    {
        var solution = featureDocument.Project.Solution;

        var compilation = await featureDocument.Project
            .GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false);
        if (compilation is null)
        {
            return solution;
        }

        var metadataName = contextFqn.StartsWith("global::", StringComparison.Ordinal)
            ? contextFqn.Substring("global::".Length)
            : contextFqn;

        var contextTypeSymbol = compilation.GetTypeByMetadataName(metadataName);
        var syntaxRef = contextTypeSymbol?.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null)
        {
            return solution;
        }

        var contextDoc = solution.GetDocument(syntaxRef.SyntaxTree);
        if (contextDoc is null)
        {
            return solution;
        }

        var root = await contextDoc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return solution;
        }

        if (syntaxRef.GetSyntax(cancellationToken) is not TypeDeclarationSyntax contextNode)
        {
            return solution;
        }

        // Use the leading trivia of the last existing attribute list to align the new attributes.
        var lastAttrTrivia = contextNode.AttributeLists.Count > 0
            ? contextNode.AttributeLists[contextNode.AttributeLists.Count - 1].GetLeadingTrivia()
            : SyntaxFactory.TriviaList(SyntaxFactory.ElasticLineFeed);

        var newAttributeLists = Array.ConvertAll(
            missingRootFqns,
            fqn => BuildJsonSerializableAttributeList(fqn, lastAttrTrivia));

        var newContextNode = contextNode.AddAttributeLists(newAttributeLists);
        var newRoot = root.ReplaceNode(contextNode, newContextNode);
        var newDoc = contextDoc.WithSyntaxRoot(newRoot);

        return newDoc.Project.Solution;
    }

    internal static AttributeListSyntax BuildJsonSerializableAttributeList(
        string typeFqn,
        SyntaxTriviaList leadingTrivia)
    {
        var typeofExpr = SyntaxFactory.TypeOfExpression(
            SyntaxFactory.ParseTypeName(typeFqn));

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("global::System.Text.Json.Serialization.JsonSerializableAttribute"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(typeofExpr))));

        return SyntaxFactory
            .AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);
    }

    private static string TrimGlobal(string fqn)
        => fqn.StartsWith("global::", StringComparison.Ordinal)
            ? fqn.Substring("global::".Length)
            : fqn;
}
