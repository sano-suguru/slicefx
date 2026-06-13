using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SliceFx.Cli.Internal;
using SliceFx.Shared;

namespace SliceFx.Cli.Commands;

internal static class JsonContextCommand
{
    private const string TargetAspNet = "AspNet";
    private const string TargetWasi = "Wasi";

    internal static Command Build()
    {
        var checkFlag = new Option<bool>("--check")
        {
            Description = "Report missing [JsonSerializable] entries; exits non-zero if any are found.",
        };
        var fixFlag = new Option<bool>("--fix")
        {
            Description = "Insert missing [JsonSerializable] entries into the JSON context class file(s) in-place.",
        };
        var targetOpt = new Option<string?>("--target")
        {
            Description = "Which context target to inspect: aspnet, wasi, or all (default: all).",
        };
        var projectOpt = SharedOptions.CreateProject();

        var cmd = new Command("json-context", "Check or fix missing [JsonSerializable] entries in SliceFx JSON context classes.")
        {
            checkFlag,
            fixFlag,
            targetOpt,
            projectOpt,
        };

        cmd.SetAction((parseResult) =>
        {
            var check = parseResult.GetValue(checkFlag);
            var fix = parseResult.GetValue(fixFlag);
            var target = parseResult.GetValue(targetOpt);
            var project = parseResult.GetValue(projectOpt);

            // Default to --check when neither flag is passed.
            if (!check && !fix)
            {
                check = true;
            }

            try
            {
                return Run(project, check, fix, target);
            }
            catch (CliException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        });

        return cmd;
    }

    private static int Run(string? project, bool check, bool fix, string? targetFilter)
    {
        var ctx = ProjectContextDiscovery.Discover(project);
        var discovery = RouteCatalog.DiscoverDetailed(ctx);
        var routes = discovery.Routes;

        var contexts = DiscoverContextClasses(ctx.ProjectDirectory.FullName);
        if (contexts.Length == 0)
        {
            if (check)
            {
                Console.Error.WriteLine("No [SliceJsonContext] classes found in project. Nothing to check.");
            }

            return 0;
        }

        var anyMissing = false;
        foreach (var contextInfo in contexts)
        {
            if (!MatchesTargetFilter(contextInfo.Target, targetFilter))
            {
                continue;
            }

            var required = ComputeRequiredRoots(routes, contextInfo.Target);
            var missing = FindMissingRoots(required, contextInfo.RegisteredTypes);

            if (missing.Count == 0)
            {
                continue;
            }

            anyMissing = true;
            var noun = missing.Count == 1 ? "entry" : "entries";
            Console.Error.WriteLine($"[{contextInfo.Target}] {contextInfo.ClassName} is missing {missing.Count} [JsonSerializable] {noun}:");
            foreach (var root in missing)
            {
                Console.Error.WriteLine($"  - {root}");
            }

            if (fix)
            {
                ApplyFix(contextInfo, missing);
                Console.Error.WriteLine($"  => Fixed: added {missing.Count} {noun} to {contextInfo.FilePath}");
            }
        }

        if (check && anyMissing && !fix)
        {
            Console.Error.WriteLine(string.Empty);
            Console.Error.WriteLine("Run 'slicefx json-context --fix' to add the missing entries automatically.");
            return 1;
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Context class discovery
    // -------------------------------------------------------------------------

    private sealed record ContextInfo(
        string FilePath,
        string ClassName,
        string Target,
        IReadOnlySet<string> RegisteredTypes);

    private static ContextInfo[] DiscoverContextClasses(string projectDirectory)
    {
        var results = new List<ContextInfo>();

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string source;
            try
            {
                source = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            var syntaxRoot = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
            foreach (var typeDecl in syntaxRoot.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var target = ReadSliceJsonContextTarget(typeDecl.AttributeLists);
                if (target is null)
                {
                    continue;
                }

                var registeredTypes = ReadJsonSerializableTypes(typeDecl.AttributeLists);
                var className = GetTypeName(typeDecl);
                results.Add(new ContextInfo(file, className, target, registeredTypes));
            }
        }

        return [.. results];
    }

    private static string? ReadSliceJsonContextTarget(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var name = GetLastIdentifier(attr.Name);
                if (!string.Equals(NormalizeAttributeName(name), "SliceJsonContext", StringComparison.Ordinal))
                {
                    continue;
                }

                // Read first argument: SliceJsonTarget.AspNet / AspNet / ...
                var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
                if (arg is null)
                {
                    continue;
                }

                var argText = arg.Expression.ToString();
                if (argText.EndsWith("AspNet", StringComparison.OrdinalIgnoreCase))
                {
                    return TargetAspNet;
                }

                if (argText.EndsWith("Wasi", StringComparison.OrdinalIgnoreCase))
                {
                    return TargetWasi;
                }
            }
        }

        return null;
    }

    private static HashSet<string> ReadJsonSerializableTypes(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in attributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var name = GetLastIdentifier(attr.Name);
                if (!string.Equals(NormalizeAttributeName(name), "JsonSerializable", StringComparison.Ordinal))
                {
                    continue;
                }

                var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
                if (arg?.Expression is TypeOfExpressionSyntax typeOf)
                {
                    // Strip global:: prefix and normalize whitespace.
                    var typeName = typeOf.Type.ToString().Trim();
                    typeName = NormalizeTypeName(typeName);
                    types.Add(typeName);
                }
            }
        }

        return types;
    }

    private static string GetTypeName(TypeDeclarationSyntax typeDecl)
    {
        // Walk up to collect containing namespace(s) — handles file-scoped and block namespaces.
        var namespaceParts = new List<string>();
        var current = typeDecl.Parent;
        while (current is not null)
        {
            if (current is NamespaceDeclarationSyntax ns)
            {
                namespaceParts.Insert(0, ns.Name.ToString());
            }
            else if (current is FileScopedNamespaceDeclarationSyntax fns)
            {
                namespaceParts.Insert(0, fns.Name.ToString());
            }

            current = current.Parent;
        }

        return namespaceParts.Count == 0
            ? typeDecl.Identifier.ValueText
            : $"{string.Join(".", namespaceParts)}.{typeDecl.Identifier.ValueText}";
    }

    // -------------------------------------------------------------------------
    // Required root computation
    // -------------------------------------------------------------------------

    private static List<string> ComputeRequiredRoots(SliceRouteInfo[] routes, string target)
    {
        var roots = new List<string>();
        foreach (var route in routes)
        {
            if (!IsRouteApplicable(route, target))
            {
                continue;
            }

            var body = GetBodyTypeFqn(route);
            if (body is not null && !IsExcludedType(body))
            {
                roots.Add(body);
            }

            var response = GetResponseTypeFqn(route.ReturnType);
            if (response is not null && !IsExcludedType(response))
            {
                roots.Add(response);
            }
        }

        // Deduplicate while preserving first-occurrence order.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. roots.Where(seen.Add)];
    }

    private static bool IsRouteApplicable(SliceRouteInfo route, string target)
    {
        if (target == TargetWasi)
        {
            // If manifest has explicit WASI dispatch status, use it.
            if (!string.IsNullOrEmpty(route.WasiDispatchStatus))
            {
                return string.Equals(route.WasiDispatchStatus, "included", StringComparison.OrdinalIgnoreCase);
            }

            // Source-only fallback: exclude routes that are aspnet-only.
            return route.Portability != RouteCatalog.PortabilityAspNetOnly;
        }

        // AspNet target: include all routes (AspNet AOT can dispatch any feature).
        return true;
    }

    private static string? GetBodyTypeFqn(SliceRouteInfo route)
    {
        // Explicit [FromBody] parameter takes precedence.
        var explicitBody = route.Parameters
            .FirstOrDefault(static p => string.Equals(p.BindingSource, "body", StringComparison.OrdinalIgnoreCase));
        if (explicitBody is not null)
        {
            return NormalizeTypeName(explicitBody.Type);
        }

        // Convention body: POST/PUT/PATCH with a Request-like parameter.
        if (route.Method is not ("POST" or "PUT" or "PATCH"))
        {
            return null;
        }

        // Nested Request record heuristic: param type is a nested type of the feature class.
        foreach (var p in route.Parameters)
        {
            if (p.Type == "CancellationToken" || p.Type.EndsWith(".CancellationToken", StringComparison.Ordinal))
            {
                continue;
            }

            var featureFqn = route.FeatureType; // e.g. "AotApp.Features.Users.CreateUser"
            if (JsonContextRootHelpers.IsNestedTypeOf(p.Type, featureFqn))
            {
                return NormalizeTypeName(p.Type);
            }
        }

        // Syntactic fallback: conventionally named "Request" parameter.
        var conventionBody = route.Parameters
            .FirstOrDefault(static p =>
                p.Type == "Request" || p.Type.EndsWith(".Request", StringComparison.Ordinal));
        return conventionBody is not null ? NormalizeTypeName(conventionBody.Type) : null;
    }

    private static string? GetResponseTypeFqn(string returnType)
    {
        if (string.IsNullOrEmpty(returnType) || returnType == "void")
        {
            return null;
        }

        // Strip Task<> / ValueTask<>
        var unwrapped = StripTaskWrapper(returnType);
        if (unwrapped is null)
        {
            return null;
        }

        // Strip SliceResult<T> to T; non-generic SliceResult → no root.
        if (string.Equals(unwrapped, "SliceFx.SliceResult", StringComparison.Ordinal)
            || string.Equals(unwrapped, "global::SliceFx.SliceResult", StringComparison.Ordinal))
        {
            return null;
        }

        if (unwrapped.StartsWith("SliceFx.SliceResult<", StringComparison.Ordinal) && unwrapped.EndsWith('>'))
        {
            return NormalizeTypeName(unwrapped["SliceFx.SliceResult<".Length..^1]);
        }

        if (unwrapped.StartsWith("global::SliceFx.SliceResult<", StringComparison.Ordinal) && unwrapped.EndsWith('>'))
        {
            return NormalizeTypeName(unwrapped["global::SliceFx.SliceResult<".Length..^1]);
        }

        return NormalizeTypeName(unwrapped);
    }

    private static string? StripTaskWrapper(string typeFqn)
    {
        const string task = "System.Threading.Tasks.Task<";
        const string valueTask = "System.Threading.Tasks.ValueTask<";
        const string globalTask = "global::System.Threading.Tasks.Task<";
        const string globalValueTask = "global::System.Threading.Tasks.ValueTask<";
        const string bareTask = "System.Threading.Tasks.Task";
        const string bareValueTask = "System.Threading.Tasks.ValueTask";

        if (typeFqn is "void" or "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask"
            or "global::System.Threading.Tasks.Task" or "global::System.Threading.Tasks.ValueTask")
        {
            return null;
        }

        if (typeFqn.StartsWith(task, StringComparison.Ordinal) && typeFqn.EndsWith('>'))
        {
            return StripTaskWrapper(typeFqn[task.Length..^1]);
        }

        if (typeFqn.StartsWith(valueTask, StringComparison.Ordinal) && typeFqn.EndsWith('>'))
        {
            return StripTaskWrapper(typeFqn[valueTask.Length..^1]);
        }

        if (typeFqn.StartsWith(globalTask, StringComparison.Ordinal) && typeFqn.EndsWith('>'))
        {
            return StripTaskWrapper(typeFqn[globalTask.Length..^1]);
        }

        if (typeFqn.StartsWith(globalValueTask, StringComparison.Ordinal) && typeFqn.EndsWith('>'))
        {
            return StripTaskWrapper(typeFqn[globalValueTask.Length..^1]);
        }

        // Unwrap IResult / Task / ValueTask without type argument
        if (typeFqn is bareTask or bareValueTask)
        {
            return null;
        }

        return typeFqn;
    }

    private static bool IsExcludedType(string typeFqn)
    {
        if (string.IsNullOrEmpty(typeFqn))
        {
            return true;
        }

        if (JsonContextRootHelpers.IsFrameworkType(typeFqn))
        {
            return true;
        }

        // Exclude IResult and its common subtypes (ASP.NET-specific).
        if (typeFqn.EndsWith(".IResult", StringComparison.Ordinal)
            || typeFqn == "IResult"
            || typeFqn.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
        {
            return true;
        }

        // Exclude well-known primitive-equivalent types that STJ handles natively.
        var bare = JsonContextRootHelpers.TrimGlobalAlias(typeFqn);
        if (s_simpleTypes.Contains(bare))
        {
            return true;
        }

        return false;
    }

    private static readonly HashSet<string> s_simpleTypes = new(StringComparer.Ordinal)
    {
        "string", "bool", "int", "long", "short", "uint", "ulong", "ushort",
        "byte", "char", "double", "float", "decimal",
        "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan", "Uri",
        "string?", "bool?", "int?", "long?", "short?", "uint?", "ulong?", "ushort?",
        "byte?", "char?", "double?", "float?", "decimal?",
        "Guid?", "DateTime?", "DateTimeOffset?", "DateOnly?", "TimeOnly?", "TimeSpan?", "Uri?",
        "System.String", "System.Boolean",
        "System.Int32", "System.Int64", "System.Int16",
        "System.UInt32", "System.UInt64", "System.UInt16",
        "System.Byte", "System.Char", "System.Double", "System.Single",
        "System.Decimal", "System.Guid",
        "System.DateTime", "System.DateTimeOffset",
        "System.DateOnly", "System.TimeOnly", "System.TimeSpan", "System.Uri",
    };

    // -------------------------------------------------------------------------
    // Missing root detection
    // -------------------------------------------------------------------------

    private static List<string> FindMissingRoots(
        List<string> required,
        IReadOnlySet<string> registeredTypes)
    {
        var missing = new List<string>();
        foreach (var root in required)
        {
            if (!IsRegistered(root, registeredTypes))
            {
                missing.Add(root);
            }
        }

        return missing;
    }

    private static bool IsRegistered(string root, IReadOnlySet<string> registeredTypes)
    {
        // Exact match (after global:: normalization already done by NormalizeTypeName).
        if (registeredTypes.Contains(root))
        {
            return true;
        }

        foreach (var registered in registeredTypes)
        {
            if (root == registered)
            {
                return true;
            }

            // Root is FQN, registered is a shorter qualified name:
            //   root     = "AotApp.Features.Todos.CreateTodo.Request"
            //   registered = "CreateTodo.Request"  →  root ends with ".CreateTodo.Request"
            if (root.EndsWith("." + registered, StringComparison.Ordinal))
            {
                return true;
            }

            // Root is a short name (source-scan mode), registered is the full FQN:
            //   root     = "Response"
            //   registered = "AotApp.Features.Items.GetItem.Response"  →  registered ends with ".Response"
            if (registered.EndsWith("." + root, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // In-place fix
    // -------------------------------------------------------------------------

    private static void ApplyFix(ContextInfo contextInfo, List<string> missingRoots)
    {
        var source = File.ReadAllText(contextInfo.FilePath);
        var syntaxRoot = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        // Find the context class declaration.
        TypeDeclarationSyntax? contextClass = null;
        foreach (var typeDecl in syntaxRoot.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (ReadSliceJsonContextTarget(typeDecl.AttributeLists) is not null)
            {
                contextClass = typeDecl;
                break;
            }
        }

        if (contextClass is null)
        {
            return;
        }

        // Build new attribute lists (one [JsonSerializable] per missing root).
        var newAttributeLists = new List<AttributeListSyntax>();
        foreach (var root in missingRoots)
        {
            // Qualify with global:: for unambiguous reference.
            var qualified = root.StartsWith("global::", StringComparison.Ordinal)
                ? root
                : $"global::{root}";
            newAttributeLists.Add(BuildJsonSerializableAttributeList(qualified));
        }

        // Insert after the last existing [JsonSerializable] attribute list, or before the class keyword.
        var lastJsonSerializableIndex = -1;
        var attrLists = contextClass.AttributeLists;
        for (var i = 0; i < attrLists.Count; i++)
        {
            foreach (var attr in attrLists[i].Attributes)
            {
                if (string.Equals(
                    NormalizeAttributeName(GetLastIdentifier(attr.Name)),
                    "JsonSerializable",
                    StringComparison.Ordinal))
                {
                    lastJsonSerializableIndex = i;
                }
            }
        }

        TypeDeclarationSyntax updatedClass;
        if (lastJsonSerializableIndex >= 0)
        {
            // Insert the new lists after the last existing [JsonSerializable].
            var insertAfter = attrLists[lastJsonSerializableIndex];
            var leadingTrivia = insertAfter.GetLeadingTrivia();
            var withTrivia = newAttributeLists
                .Select(a => a.WithLeadingTrivia(leadingTrivia))
                .ToArray();

            var newAttrLists = attrLists.ToList();
            newAttrLists.InsertRange(lastJsonSerializableIndex + 1, withTrivia);
            updatedClass = contextClass.WithAttributeLists(SyntaxFactory.List(newAttrLists));
        }
        else
        {
            // No existing [JsonSerializable] — prepend before the first attribute or before keyword.
            var leadingTrivia = attrLists.Count > 0
                ? attrLists[0].GetLeadingTrivia()
                : contextClass.Keyword.LeadingTrivia;
            var withTrivia = newAttributeLists
                .Select(a => a.WithLeadingTrivia(leadingTrivia))
                .ToArray();
            updatedClass = contextClass.AddAttributeLists(withTrivia);
        }

        var newRoot = syntaxRoot.ReplaceNode(contextClass, updatedClass);
        File.WriteAllText(contextInfo.FilePath, newRoot.ToFullString());
    }

    private static AttributeListSyntax BuildJsonSerializableAttributeList(string typeFqn)
    {
        // Build: [JsonSerializable(typeof(global::X.Y.Z))]
        var typeExpr = SyntaxFactory.TypeOfExpression(
            SyntaxFactory.ParseTypeName(typeFqn));
        var attrArgList = SyntaxFactory.AttributeArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.AttributeArgument(typeExpr)));
        var attr = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("JsonSerializable"),
            attrArgList);
        return SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attr))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool MatchesTargetFilter(string contextTarget, string? filter)
    {
        if (string.IsNullOrEmpty(filter) || string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(contextTarget, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.Trim();
        if (typeName.StartsWith("global::", StringComparison.Ordinal))
        {
            typeName = typeName["global::".Length..];
        }

        return typeName;
    }

    private static string NormalizeAttributeName(string name)
    {
        const string attributeSuffix = "Attribute";
        return name.EndsWith(attributeSuffix, StringComparison.Ordinal)
            ? name[..^attributeSuffix.Length]
            : name;
    }

    private static string GetLastIdentifier(NameSyntax nameSyntax)
        => nameSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.ValueText,
            _ => nameSyntax.ToString(),
        };
}
