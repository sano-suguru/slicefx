using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SliceFx.SourceGenerator.CodeFixes.Tests;

/// <summary>
/// Integration tests for <see cref="JsonContextCodeFixProvider"/>.
/// These tests create minimal Roslyn workspaces to verify fix behavior
/// without requiring a full IDE environment.
/// </summary>
public sealed class JsonContextCodeFixTests
{
    // ---------------------------------------------------------------------------
    // Unit tests — BuildJsonSerializableAttributeList
    // ---------------------------------------------------------------------------

    [Fact]
    public void BuildJsonSerializableAttributeList_generates_correct_syntax()
    {
        var triviaList = SyntaxFactory.TriviaList(SyntaxFactory.Whitespace("    "));
        var attrList = JsonContextCodeFixProvider.BuildJsonSerializableAttributeList(
            "global::MyApp.Features.Items.CreateItem.Response",
            triviaList);

        var text = attrList.ToFullString();
        Assert.Contains("JsonSerializableAttribute", text, StringComparison.Ordinal);
        Assert.Contains("global::MyApp.Features.Items.CreateItem.Response", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // Integration tests — code fix via workspace
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CodeFix_adds_missing_JsonSerializable_to_context_class()
    {
        const string contextSource = """
            using System.Text.Json.Serialization;
            using SliceFx;

            namespace AotApp
            {
                [SliceJsonContext(SliceJsonTarget.AspNet)]
                public sealed partial class AotContext : global::System.Text.Json.Serialization.JsonSerializerContext { }
            }
            """;

        // A minimal feature source just so the workspace has a second document.
        const string featureSource = """
            namespace AotApp.Features.Items
            {
                public static class GetItem
                {
                    public record Response(string Id);
                }
            }
            """;

        using var workspace = BuildWorkspace("AotApp", [("Context.cs", contextSource), ("Feature.cs", featureSource)]);
        var contextDoc = workspace.CurrentSolution.Projects.Single()
            .Documents.Single(d => d.Name == "Context.cs");

        // Construct the diagnostic with the structured properties the planner would emit.
        var properties = ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            [
                new KeyValuePair<string, string?>("MissingRoots", "global::AotApp.Features.Items.GetItem.Response"),
                new KeyValuePair<string, string?>("ContextFqn", "global::AotApp.AotContext"),
                new KeyValuePair<string, string?>("Target", "AspNet"),
            ]);

        var root = await contextDoc.GetSyntaxRootAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(root);
        var contextClassNode = root.DescendantNodes().OfType<TypeDeclarationSyntax>().First();
        var location = contextClassNode.GetLocation();
        var descriptor = new DiagnosticDescriptor(
            "SLICE071", "title", "message", "Slice", DiagnosticSeverity.Error, isEnabledByDefault: true);
        var diagnostic = Diagnostic.Create(descriptor, location, properties, "AotContext", "missing roots");

        // Invoke the code fix.
        CodeAction? capturedAction = null;
        var fixContext = new CodeFixContext(
            contextDoc,
            diagnostic,
            (action, _) => capturedAction = action,
            TestContext.Current.CancellationToken);

        var provider = new JsonContextCodeFixProvider();
        await provider.RegisterCodeFixesAsync(fixContext);

        Assert.NotNull(capturedAction);

        // Apply the fix and check the resulting source.
        var ops = await capturedAction.GetOperationsAsync(TestContext.Current.CancellationToken);
        var applyOp = ops.OfType<ApplyChangesOperation>().Single();
        var newSolution = applyOp.ChangedSolution;
        var newContextDoc = newSolution.GetDocument(contextDoc.Id);
        Assert.NotNull(newContextDoc);
        var newRoot = await newContextDoc.GetSyntaxRootAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(newRoot);
        var newText = newRoot.ToFullString();

        Assert.Contains("JsonSerializableAttribute", newText, StringComparison.Ordinal);
        Assert.Contains("global::AotApp.Features.Items.GetItem.Response", newText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_does_not_register_fix_when_ContextFqn_absent()
    {
        // Diagnostic without ContextFqn property → no fix offered (context absent case).
        const string contextSource = """
            namespace AotApp
            {
                public class Dummy { }
            }
            """;

        using var workspace = BuildWorkspace("AotApp", [("Dummy.cs", contextSource)]);
        var doc = workspace.CurrentSolution.Projects.Single().Documents.Single();

        var descriptor = new DiagnosticDescriptor(
            "SLICE071", "title", "message", "Slice", DiagnosticSeverity.Error, isEnabledByDefault: true);
        // No MissingRoots or ContextFqn in properties — absent context case.
        var diagnostic = Diagnostic.Create(descriptor, Location.None, ImmutableDictionary<string, string?>.Empty);

        var fixCount = 0;
        var fixContext = new CodeFixContext(
            doc,
            diagnostic,
            (_, _) => fixCount++,
            TestContext.Current.CancellationToken);

        var provider = new JsonContextCodeFixProvider();
        await provider.RegisterCodeFixesAsync(fixContext);

        Assert.Equal(0, fixCount);
    }

    [Fact]
    public async Task CodeFix_adds_multiple_missing_entries_in_one_fix()
    {
        const string contextSource = """
            using System.Text.Json.Serialization;
            using SliceFx;

            namespace AotApp
            {
                [SliceJsonContext(SliceJsonTarget.AspNet)]
                public sealed partial class AotContext : global::System.Text.Json.Serialization.JsonSerializerContext { }
            }
            """;

        using var workspace = BuildWorkspace("AotApp", [("Context.cs", contextSource)]);
        var contextDoc = workspace.CurrentSolution.Projects.Single().Documents.Single();

        // Two missing roots separated by '\n'.
        var properties = ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            [
                new KeyValuePair<string, string?>(
                    "MissingRoots",
                    "global::AotApp.Features.Users.CreateUser.Request\nglobal::AotApp.Features.Users.CreateUser.Response"),
                new KeyValuePair<string, string?>("ContextFqn", "global::AotApp.AotContext"),
                new KeyValuePair<string, string?>("Target", "AspNet"),
            ]);

        var root = await contextDoc.GetSyntaxRootAsync(TestContext.Current.CancellationToken);
        var contextClassNode = root!.DescendantNodes().OfType<TypeDeclarationSyntax>().First();
        var descriptor = new DiagnosticDescriptor(
            "SLICE071", "title", "message", "Slice", DiagnosticSeverity.Error, isEnabledByDefault: true);
        var diagnostic = Diagnostic.Create(descriptor, contextClassNode.GetLocation(), properties);

        CodeAction? capturedAction = null;
        var fixContext = new CodeFixContext(
            contextDoc,
            diagnostic,
            (action, _) => capturedAction = action,
            TestContext.Current.CancellationToken);

        var provider = new JsonContextCodeFixProvider();
        await provider.RegisterCodeFixesAsync(fixContext);

        Assert.NotNull(capturedAction);
        // Title should mention "2 missing" for multi-entry fixes.
        Assert.Contains("2", capturedAction.Title, StringComparison.Ordinal);

        var ops = await capturedAction.GetOperationsAsync(TestContext.Current.CancellationToken);
        var applyOp = ops.OfType<ApplyChangesOperation>().Single();
        var newSolution = applyOp.ChangedSolution;
        var newText = (await newSolution.GetDocument(contextDoc.Id)!
            .GetSyntaxRootAsync(TestContext.Current.CancellationToken))!.ToFullString();

        Assert.Contains("global::AotApp.Features.Users.CreateUser.Request", newText, StringComparison.Ordinal);
        Assert.Contains("global::AotApp.Features.Users.CreateUser.Response", newText, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static AdhocWorkspace BuildWorkspace(string assemblyName, (string Name, string Source)[] files)
    {
        var workspace = new AdhocWorkspace();

        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            name: assemblyName,
            assemblyName: assemblyName,
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: GetMetadataReferences());

        var solution = workspace.CurrentSolution.AddProject(projectInfo);

        foreach (var (name, source) in files)
        {
            var docId = DocumentId.CreateNewId(projectId);
            solution = solution.AddDocument(docId, name, SourceText.From(source));
        }

        workspace.TryApplyChanges(solution);
        return workspace;
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Where(static p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            ?? [];

        foreach (var path in paths)
        {
            yield return MetadataReference.CreateFromFile(path);
        }

        yield return MetadataReference.CreateFromFile(typeof(FeatureAttribute).Assembly.Location);
    }
}
