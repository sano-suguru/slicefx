using SliceFx.Shared;

namespace SliceFx.SourceGenerator.Tests;

// Direct unit tests for the shared FQN-classification helpers. Linked into the generator (and CLI)
// via <Compile Include>; exercised here through InternalsVisibleTo. These pin the tokenizer that
// drives both missing-root detection (SLICE021/071) and body-vs-service parameter classification.
public sealed class JsonContextRootHelpersTests
{
    [Theory]
    // User type directly, or wrapped in a framework generic container / array → needs registration.
    [InlineData("global::App.MyDto", true)]
    [InlineData("global::System.Collections.Generic.List<global::App.MyDto>", true)]
    [InlineData("global::App.MyDto[]", true)]
    [InlineData("global::System.Collections.Generic.Dictionary<global::System.String, global::App.MyDto>", true)]
    [InlineData("global::System.Collections.Generic.List<global::System.Collections.Generic.List<global::App.MyDto>>", true)]
    // Tuple syntax (parens) as FullyQualifiedFormat emits it, mixing a framework and a user element.
    [InlineData("(global::System.Int32, global::App.MyDto)", true)]
    // Pure framework type trees (built-in STJ support / transitive coverage) → skip.
    [InlineData("global::System.Int32", false)]
    [InlineData("global::System.String", false)]
    [InlineData("global::System.Byte[]", false)]
    [InlineData("global::System.Memory<global::System.Byte>", false)]
    [InlineData("global::System.Collections.Generic.List<global::System.String>", false)]
    [InlineData("global::System.Collections.Generic.Dictionary<global::System.String, global::System.Int32>", false)]
    public void RequiresJsonSerializableRegistration_classifies_type_trees(string typeFqn, bool expected)
        => Assert.Equal(expected, JsonContextRootHelpers.RequiresJsonSerializableRegistration(typeFqn));

    [Theory]
    // Namespace-qualified framework types.
    [InlineData("global::System.Int32", true)]
    [InlineData("System.Collections.Generic.List", true)]
    [InlineData("global::Microsoft.AspNetCore.Http.IResult", true)]
    // C# keyword aliases that FullyQualifiedFormat retains inside generic args — must be framework
    // too, since IsFrameworkType also drives body-vs-service parameter classification (finding #3).
    [InlineData("object", true)]
    [InlineData("byte", true)]
    [InlineData("char", true)]
    [InlineData("string", true)]
    // User types are not framework.
    [InlineData("global::App.MyDto", false)]
    [InlineData("MyApp.Dto", false)]
    public void IsFrameworkType_treats_system_microsoft_and_keyword_aliases_as_framework(string typeFqn, bool expected)
        => Assert.Equal(expected, JsonContextRootHelpers.IsFrameworkType(typeFqn));
}
