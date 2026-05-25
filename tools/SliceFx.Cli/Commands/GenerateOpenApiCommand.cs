using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SliceFx.Cli.Internal;

namespace SliceFx.Cli.Commands;

internal static partial class GenerateOpenApiCommand
{
    private const string DefaultOpenApiVersion = "3.1.1";
    private const string DefaultApiVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static Command Build()
    {
        var projectOpt = SharedOptions.CreateProject();
        var outputOpt = new Option<FileInfo?>("--output")
        {
            Description = "Output OpenAPI JSON file. Defaults to openapi.json in the target project directory.",
        };
        var titleOpt = new Option<string?>("--title")
        {
            Description = "OpenAPI document title. Defaults to the target assembly name.",
        };
        var versionOpt = new Option<string>("--version")
        {
            Description = "API version for the OpenAPI info object.",
            DefaultValueFactory = _ => DefaultApiVersion,
        };
        var openApiVersionOpt = new Option<string>("--openapi-version")
        {
            Description = "OpenAPI specification version to emit.",
            DefaultValueFactory = _ => DefaultOpenApiVersion,
        };
        var includeAspNetOnlyOpt = new Option<bool>("--include-aspnet-only")
        {
            Description = "Include ASP.NET-only routes with explicit Slice portability metadata and incomplete schemas.",
        };
        var strictOpt = new Option<bool>("--strict")
        {
            Description = "Fail when routes are omitted from the manifest projection.",
        };
        var forceOpt = SharedOptions.CreateForce();

        var cmd = new Command("openapi", "Generate an OpenAPI JSON document from the Slice route manifest.")
        {
            projectOpt,
            outputOpt,
            titleOpt,
            versionOpt,
            openApiVersionOpt,
            includeAspNetOnlyOpt,
            strictOpt,
            forceOpt
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var output = parseResult.GetValue(outputOpt);
            var title = parseResult.GetValue(titleOpt);
            var version = parseResult.GetValue(versionOpt) ?? DefaultApiVersion;
            var openApiVersion = parseResult.GetValue(openApiVersionOpt) ?? DefaultOpenApiVersion;
            var includeAspNetOnly = parseResult.GetValue(includeAspNetOnlyOpt);
            var strict = parseResult.GetValue(strictOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(project, output, title, version, openApiVersion, includeAspNetOnly, strict, force, ct).ConfigureAwait(false);
                return 0;
            }
            catch (CliException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        });

        return cmd;
    }

    private static async Task RunAsync(
        FileInfo? project,
        FileInfo? output,
        string? title,
        string version,
        string openApiVersion,
        bool includeAspNetOnly,
        bool strict,
        bool force,
        CancellationToken ct)
    {
        if (!IsSupportedOpenApiVersion(openApiVersion))
        {
            throw new CliException("OpenAPI version must be 3.0.3, 3.1.0, or 3.1.1.");
        }

        var ctx = ProjectContextDiscovery.Discover(project);
        var discovery = RouteCatalog.DiscoverDetailed(ctx);
        RouteCatalog.WriteAggregatedRouteNotice(discovery);
        var routes = discovery.Routes;
        if (routes.Length == 0)
        {
            throw new CliException("No [Feature] routes found. Build the project first if the generated manifest is not yet available.");
        }

        var omitted = includeAspNetOnly
            ? []
            : routes
                .Where(static route => route.Portability == RouteCatalog.PortabilityAspNetOnly)
                .ToArray();
        if (strict && omitted.Length > 0)
        {
            throw new CliException("OpenAPI manifest projection omitted ASP.NET-only routes:\n" + FormatOmittedRoutes(omitted));
        }

        var included = includeAspNetOnly
            ? routes
            : [.. routes
                .Where(static route => route.Portability != RouteCatalog.PortabilityAspNetOnly)
            ];
        if (included.Length == 0)
        {
            throw new CliException("No portable or partial Slice routes found for OpenAPI manifest projection.");
        }

        var outputFile = output ?? new FileInfo(Path.Combine(ctx.ProjectDirectory.FullName, "openapi.json"));
        if (outputFile.Exists && !force)
        {
            throw new CliException($"Output file already exists: {outputFile.FullName}. Pass --force to overwrite it.");
        }

        var assemblyFiles = BuildOutputAssemblyFinder.FindAssemblyFiles(ctx);
        var reader = TypeSchemaReader.Create(assemblyFiles);
        var document = RenderDocument(
            ctx,
            included,
            omitted,
            reader,
            string.IsNullOrWhiteSpace(title) ? ctx.AssemblyName : title!,
            version,
            openApiVersion);
        var json = JsonSerializer.Serialize(document, JsonOptions);

        Directory.CreateDirectory(outputFile.DirectoryName!);
        await File.WriteAllTextAsync(outputFile.FullName, json + Environment.NewLine, ct).ConfigureAwait(false);
        Console.WriteLine($"Generated {outputFile.FullName}");
        foreach (var route in omitted)
        {
            Console.Error.WriteLine($"Warning: omitted ASP.NET-only route {route.EndpointName} ({route.Method} {route.Pattern}) from manifest OpenAPI projection.");
        }
    }

    private static OpenApiDocument RenderDocument(
        ProjectContext ctx,
        SliceRouteInfo[] routes,
        SliceRouteInfo[] omitted,
        TypeSchemaReader reader,
        string title,
        string version,
        string openApiVersion)
    {
        var schemaRegistry = new OpenApiSchemaRegistry(reader, openApiVersion);
        var paths = new SortedDictionary<string, SortedDictionary<string, OpenApiOperation>>(StringComparer.Ordinal);

        foreach (var route in routes.OrderBy(static route => route.Pattern, StringComparer.Ordinal)
                     .ThenBy(static route => route.Method, StringComparer.Ordinal))
        {
            var path = ToOpenApiPath(route.Pattern);
            if (!paths.TryGetValue(path, out var methods))
            {
                methods = new SortedDictionary<string, OpenApiOperation>(StringComparer.Ordinal);
                paths.Add(path, methods);
            }

            methods[route.Method.ToLowerInvariant()] = BuildOperation(route, schemaRegistry);
        }

        return new OpenApiDocument(
            openApiVersion,
            new OpenApiInfo(
                title,
                version,
                $"Generated by `slicefx openapi` from the SliceFx route manifest for {ctx.AssemblyName}. " +
                "This document is a manifest projection for portable tooling; use ASP.NET Core AddOpenApi/MapOpenApi for the authoritative hosted-app document."),
            paths,
            new OpenApiComponents(schemaRegistry.Schemas),
            "manifest",
            [.. omitted.Select(static route => new OpenApiOmittedRoute(
                route.EndpointName,
                route.Method,
                route.Pattern,
                route.PortabilityReason ?? "ASP.NET-only route omitted from manifest OpenAPI projection"))]);
    }

    private static OpenApiOperation BuildOperation(SliceRouteInfo route, OpenApiSchemaRegistry schemaRegistry)
    {
        var bodyParameter = ClientGenerationHelpers.FindBodyParameter(route);
        var routeParameters = ClientGenerationHelpers.FindRouteParameters(route);
        var queryParameters = ClientGenerationHelpers.FindQueryParameters(route, routeParameters, bodyParameter);
        var headerParameters = ClientGenerationHelpers.FindHeaderParameters(route);
        var parameters = routeParameters
            .Select(parameter => BuildParameter(parameter, "path", required: true, schemaRegistry))
            .Concat(queryParameters.Select(parameter => BuildParameter(
                parameter,
                "query",
                required: !parameter.IsNullable,
                schemaRegistry)))
            .Concat(headerParameters.Select(parameter => BuildParameter(
                parameter,
                "header",
                required: !parameter.IsNullable,
                schemaRegistry)))
            .ToArray();

        var requestBody = bodyParameter is null
            ? null
            : new OpenApiRequestBody(
                Required: true,
                Content: new SortedDictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    ["application/json"] = new(schemaRegistry.SchemaFor(bodyParameter.Type)),
                });

        var rawReturnType = ClientGenerationHelpers.UnwrapReturnType(route.ReturnType);
        var response = BuildSuccessResponse(route, rawReturnType, schemaRegistry);
        return new OpenApiOperation(
            route.EndpointName,
            route.Summary,
            [route.Tag],
            parameters.Length == 0 ? null : parameters,
            requestBody,
            new SortedDictionary<string, OpenApiResponse>(StringComparer.Ordinal)
            {
                ["200"] = response,
            },
            route.Portability,
            route.PortabilityReason);
    }

    private static OpenApiParameter BuildParameter(
        SliceRouteParameter parameter,
        string location,
        bool required,
        OpenApiSchemaRegistry schemaRegistry)
        => new(
            parameter.WireName,
            location,
            required,
            schemaRegistry.SchemaFor(parameter.Type));

    private static OpenApiResponse BuildSuccessResponse(
        SliceRouteInfo route,
        string rawReturnType,
        OpenApiSchemaRegistry schemaRegistry)
    {
        if (rawReturnType is "void" ||
            route.Portability == RouteCatalog.PortabilityAspNetOnly)
        {
            var description = route.Portability == RouteCatalog.PortabilityAspNetOnly
                ? "Successful response. ASP.NET-only result schema is not available from the Slice route manifest."
                : "Successful response.";
            return new OpenApiResponse(description, null);
        }

        return new OpenApiResponse(
            "Successful response.",
            new SortedDictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                ["application/json"] = new(schemaRegistry.SchemaFor(rawReturnType)),
            });
    }

    private static string ToOpenApiPath(string pattern)
    {
        var normalized = RouteParameterRegex().Replace(pattern, static match =>
        {
            var token = match.Groups["token"].Value.TrimStart('*');
            var terminator = token.IndexOfAny([':', '?', '=']);
            var name = terminator >= 0 ? token[..terminator] : token;
            return "{" + name + "}";
        });
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static bool IsSupportedOpenApiVersion(string version)
        => version is "3.0.3" or "3.1.0" or "3.1.1";

    private static string FormatOmittedRoutes(SliceRouteInfo[] omitted)
        => string.Join(Environment.NewLine, omitted.Select(static route =>
            $"  - {route.EndpointName}: {route.Method} {route.Pattern} ({route.PortabilityReason ?? "ASP.NET-only"})"));

    [GeneratedRegex(@"\{(?<token>\*{0,2}[A-Za-z_][A-Za-z0-9_]*(?::[^}\?=]+)?(?:\?|\=[^}]+)?)\}")]
    private static partial Regex RouteParameterRegex();

    private sealed class OpenApiSchemaRegistry
    {
        private readonly TypeSchemaReader _reader;
        private readonly string _openApiVersion;
        private readonly Dictionary<string, OpenApiSchema> _schemas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _componentNamesByType = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedComponentNames = new(StringComparer.Ordinal);

        internal OpenApiSchemaRegistry(TypeSchemaReader reader, string openApiVersion)
        {
            _reader = reader;
            _openApiVersion = openApiVersion;
        }

        internal SortedDictionary<string, OpenApiSchema> Schemas { get; } = new(StringComparer.Ordinal);

        internal OpenApiSchema SchemaFor(string dotNetType, bool stringEnum = false)
        {
            var nullable = TryUnwrapNullable(dotNetType, out var nullableInner) ||
                RouteCatalog.NormalizeWhitespace(dotNetType).EndsWith('?');
            var normalized = NormalizeTypeName(nullable ? nullableInner : dotNetType);

            if (IsBinaryType(normalized))
            {
                return AddNullable(new OpenApiSchema(Type: "string", Format: "byte"), nullable);
            }

            if (normalized.EndsWith("[]", StringComparison.Ordinal))
            {
                var schema = new OpenApiSchema(Type: "array", Items: SchemaFor(normalized[..^2]));
                return AddNullable(schema, nullable);
            }

            if (TryUnwrapDictionary(normalized, out var valueType))
            {
                var schema = new OpenApiSchema(
                    Type: "object",
                    AdditionalProperties: SchemaFor(valueType));
                return AddNullable(schema, nullable);
            }

            if (TryUnwrapCollection(normalized, out var elementType))
            {
                var schema = new OpenApiSchema(Type: "array", Items: SchemaFor(elementType));
                return AddNullable(schema, nullable);
            }

            var primitive = PrimitiveSchema(normalized);
            if (primitive is not null)
            {
                return AddNullable(primitive, nullable);
            }

            var enumSchema = _reader.ReadEnumSchema(normalized);
            if (enumSchema is not null)
            {
                var componentName = EnsureEnumSchema(enumSchema, stringEnum);
                return AddNullable(OpenApiSchema.Reference(componentName), nullable);
            }

            var properties = _reader.ReadPublicPropertySchemas(normalized);
            if (properties.Count == 0)
            {
                return AddNullable(new OpenApiSchema(Type: "object"), nullable);
            }

            EnsureObjectSchema(normalized, properties);
            return AddNullable(OpenApiSchema.Reference(GetComponentName(normalized)), nullable);
        }

        private void EnsureObjectSchema(string typeName, IReadOnlyList<TypeSchemaProperty> properties)
        {
            var componentName = GetComponentName(typeName);
            if (_schemas.ContainsKey(componentName))
            {
                return;
            }

            _schemas.Add(componentName, new OpenApiSchema(Type: "object"));
            var propertySchemas = new SortedDictionary<string, OpenApiSchema>(StringComparer.Ordinal);
            var required = new List<string>();
            foreach (var property in properties)
            {
                try
                {
                    propertySchemas.Add(property.JsonName, SchemaFor(property.Type, property.IsStringEnum));
                }
                catch (ArgumentException ex)
                {
                    throw new CliException(
                        $"Type '{typeName}' maps multiple CLR properties to JSON property '{property.JsonName}'. Use unique JsonPropertyName values.",
                        ex);
                }

                if (property.IsRequired)
                {
                    required.Add(property.JsonName);
                }
            }

            var schema = new OpenApiSchema(
                Type: "object",
                Properties: propertySchemas,
                Required: required.Count == 0 ? null : [.. required.Order(StringComparer.Ordinal)]);
            _schemas[componentName] = schema;
            Schemas[componentName] = schema;
        }

        private string EnsureEnumSchema(EnumSchemaInfo enumSchema, bool stringEnum)
        {
            stringEnum |= enumSchema.IsStringEnum;
            var componentTypeName = stringEnum ? enumSchema.TypeName + ".JsonString" : enumSchema.TypeName;
            var componentName = GetComponentName(componentTypeName);
            if (Schemas.ContainsKey(componentName))
            {
                return componentName;
            }

            Schemas[componentName] = new OpenApiSchema(
                Type: stringEnum ? "string" : "integer",
                Format: stringEnum ? null : "int32",
                Enum: stringEnum ? enumSchema.Names.ToArray() : enumSchema.Values.ToArray(),
                EnumNames: [.. enumSchema.Names]);
            return componentName;
        }

        private OpenApiSchema AddNullable(OpenApiSchema schema, bool nullable)
        {
            if (!nullable)
            {
                return schema;
            }

            if (_openApiVersion.StartsWith("3.1", StringComparison.Ordinal))
            {
                if (schema.Ref is not null)
                {
                    return new OpenApiSchema(AnyOf: [schema, new OpenApiSchema(Type: "null")]);
                }

                if (schema.Type is null)
                {
                    return new OpenApiSchema(AnyOf: [schema, new OpenApiSchema(Type: "null")]);
                }

                return schema with { Type = new[] { schema.Type, "null" } };
            }

            return schema with { Nullable = true };
        }

        private static OpenApiSchema? PrimitiveSchema(string type)
        {
            var normalized = ClientGenerationHelpers.NormalizeParameterType(type);
            return normalized switch
            {
                "string" or "String" or "System.String" => new OpenApiSchema(Type: "string"),
                "Guid" or "System.Guid" => new OpenApiSchema(Type: "string", Format: "uuid"),
                "DateTime" or "System.DateTime" or "DateTimeOffset" or "System.DateTimeOffset" => new OpenApiSchema(Type: "string", Format: "date-time"),
                "DateOnly" or "System.DateOnly" => new OpenApiSchema(Type: "string", Format: "date"),
                "TimeOnly" or "System.TimeOnly" or "TimeSpan" or "System.TimeSpan" or "char" or "Char" or "System.Char" => new OpenApiSchema(Type: "string"),
                "bool" or "Boolean" or "System.Boolean" => new OpenApiSchema(Type: "boolean"),
                "int" or "Int32" or "System.Int32" or "short" or "Int16" or "System.Int16" or "byte" or "Byte" or "System.Byte" => new OpenApiSchema(Type: "integer", Format: "int32"),
                "long" or "Int64" or "System.Int64" => new OpenApiSchema(Type: "integer", Format: "int64"),
                "uint" or "UInt32" or "System.UInt32" or "ushort" or "UInt16" or "System.UInt16" or "ulong" or "UInt64" or "System.UInt64" => new OpenApiSchema(Type: "integer", Format: "int64"),
                "float" or "Single" or "System.Single" => new OpenApiSchema(Type: "number", Format: "float"),
                "double" or "Double" or "System.Double" => new OpenApiSchema(Type: "number", Format: "double"),
                "decimal" or "Decimal" or "System.Decimal" => new OpenApiSchema(Type: "number", Format: "decimal"),
                "object" or "Object" or "System.Object" => new OpenApiSchema(Type: "object"),
                _ => null,
            };
        }

        private static bool IsBinaryType(string type)
        {
            var normalized = ClientGenerationHelpers.NormalizeParameterType(type);
            return normalized is "byte[]" or "Byte[]" or "System.Byte[]" or
                "Memory<byte>" or "System.Memory<byte>" or "ReadOnlyMemory<byte>" or "System.ReadOnlyMemory<byte>" or
                "Memory<System.Byte>" or "System.Memory<System.Byte>" or "ReadOnlyMemory<System.Byte>" or "System.ReadOnlyMemory<System.Byte>";
        }

        private static bool TryUnwrapNullable(string type, out string inner)
        {
            var normalized = RouteCatalog.NormalizeWhitespace(type);
            if (normalized.EndsWith('?'))
            {
                inner = normalized[..^1];
                return true;
            }

            return TryUnwrapGeneric(normalized, "System.Nullable", out inner) ||
                   TryUnwrapGeneric(normalized, "Nullable", out inner);
        }

        private static bool TryUnwrapCollection(string type, out string elementType)
            => TryUnwrapGeneric(type, "IReadOnlyList", out elementType) ||
               TryUnwrapGeneric(type, "System.Collections.Generic.IReadOnlyList", out elementType) ||
               TryUnwrapGeneric(type, "IEnumerable", out elementType) ||
               TryUnwrapGeneric(type, "System.Collections.Generic.IEnumerable", out elementType) ||
               TryUnwrapGeneric(type, "List", out elementType) ||
               TryUnwrapGeneric(type, "System.Collections.Generic.List", out elementType);

        private static bool TryUnwrapDictionary(string type, out string valueType)
        {
            valueType = "";
            if (!TryUnwrapGeneric(type, "Dictionary", out var args) &&
                !TryUnwrapGeneric(type, "System.Collections.Generic.Dictionary", out args) &&
                !TryUnwrapGeneric(type, "IReadOnlyDictionary", out args) &&
                !TryUnwrapGeneric(type, "System.Collections.Generic.IReadOnlyDictionary", out args))
            {
                return false;
            }

            var split = SplitTopLevelComma(args);
            if (split.Length != 2)
            {
                return false;
            }

            valueType = split[1];
            return true;
        }

        private static bool TryUnwrapGeneric(string type, string wrapper, out string inner)
        {
            var prefix = wrapper + "<";
            if (type.StartsWith(prefix, StringComparison.Ordinal) && type.EndsWith('>'))
            {
                inner = type[prefix.Length..^1];
                return true;
            }

            inner = "";
            return false;
        }

        private static string[] SplitTopLevelComma(string value)
        {
            var depth = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '<')
                {
                    depth++;
                }
                else if (value[i] == '>')
                {
                    depth--;
                }
                else if (value[i] == ',' && depth == 0)
                {
                    return [value[..i].Trim(), value[(i + 1)..].Trim()];
                }
            }

            return [value.Trim()];
        }

        private static string NormalizeTypeName(string type)
        {
            var normalized = RouteCatalog.NormalizeWhitespace(type);
            if (normalized.StartsWith("global::", StringComparison.Ordinal))
            {
                normalized = normalized["global::".Length..];
            }

            return normalized;
        }

        private string GetComponentName(string typeName)
        {
            var normalized = NormalizeTypeName(typeName).TrimEnd('?');
            if (_componentNamesByType.TryGetValue(normalized, out var existing))
            {
                return existing;
            }

            var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var count = 1; count <= segments.Length; count++)
            {
                var candidate = ComponentName(string.Join(".", segments[^count..]));
                if (_usedComponentNames.Add(candidate))
                {
                    _componentNamesByType.Add(normalized, candidate);
                    return candidate;
                }
            }

            var fallback = ComponentName(normalized + "_" + StableHash(normalized));
            if (!_usedComponentNames.Add(fallback))
            {
                throw new CliException($"Unable to create a unique OpenAPI component name for '{normalized}'.");
            }

            _componentNamesByType.Add(normalized, fallback);
            return fallback;
        }

        private static string ComponentName(string typeName)
        {
            var normalized = NormalizeTypeName(typeName).TrimEnd('?');
            var chars = normalized.Select(static ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray();
            var result = new string(chars).Trim('_');
            return result.Length == 0 || char.IsDigit(result[0]) ? "Schema_" + result : result;
        }

        private static string StableHash(string value)
        {
            var hash = 2166136261U;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private sealed record OpenApiDocument(
        [property: JsonPropertyName("openapi")] string OpenApi,
        OpenApiInfo Info,
        SortedDictionary<string, SortedDictionary<string, OpenApiOperation>> Paths,
        OpenApiComponents Components,
        [property: JsonPropertyName("x-slicefx-source")] string SliceSource,
        [property: JsonPropertyName("x-slicefx-omitted")] OpenApiOmittedRoute[]? OmittedRoutes);

    private sealed record OpenApiInfo(string Title, string Version, string Description);

    private sealed record OpenApiComponents(SortedDictionary<string, OpenApiSchema> Schemas);

    private sealed record OpenApiOperation(
        string OperationId,
        string? Summary,
        string[] Tags,
        OpenApiParameter[]? Parameters,
        OpenApiRequestBody? RequestBody,
        SortedDictionary<string, OpenApiResponse> Responses,
        [property: JsonPropertyName("x-slicefx-portability")] string Portability,
        [property: JsonPropertyName("x-slicefx-portability-reason")] string? PortabilityReason);

    private sealed record OpenApiParameter(string Name, string In, bool Required, OpenApiSchema Schema);

    private sealed record OpenApiRequestBody(bool Required, SortedDictionary<string, OpenApiMediaType> Content);

    private sealed record OpenApiMediaType(OpenApiSchema Schema);

    private sealed record OpenApiResponse(string Description, SortedDictionary<string, OpenApiMediaType>? Content);

    private sealed record OpenApiOmittedRoute(string OperationId, string Method, string Pattern, string Reason);

    private sealed record OpenApiSchema(
        [property: JsonPropertyName("$ref")] string? Ref = null,
        object? Type = null,
        string? Format = null,
        OpenApiSchema? Items = null,
        OpenApiSchema? AdditionalProperties = null,
        SortedDictionary<string, OpenApiSchema>? Properties = null,
        string[]? Required = null,
        object? Enum = null,
        [property: JsonPropertyName("x-enumNames")] string[]? EnumNames = null,
        OpenApiSchema[]? AnyOf = null,
        bool? Nullable = null)
    {
        internal static OpenApiSchema Reference(string componentName)
            => new(Ref: "#/components/schemas/" + componentName);
    }
}
