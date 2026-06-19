using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SliceFx.Cli.Internal;

namespace SliceFx.Cli.Commands;

internal static partial class GenerateCSharpClientCommand
{
    internal static Command Build()
    {
        var projectOpt = SharedOptions.CreateProject();
        var outputOpt = new Option<string?>("--output")
        {
            Description = "Output .cs file or directory (in which case {className}.g.cs is used). Defaults to SliceApiClient.g.cs in the target project directory.",
        };
        var namespaceOpt = new Option<string?>("--namespace")
        {
            Description = "Namespace for the generated client. Defaults to <RootNamespace>.Client.",
        };
        var classOpt = new Option<string>("--class")
        {
            Description = "Generated client class name.",
            DefaultValueFactory = _ => "SliceApiClient",
        };
        var jsonContextOpt = new Option<string?>("--json-context")
        {
            Description = "Fully-qualified name of an existing JsonSerializerContext to use for trim-safe serialization. " +
                          "When omitted, an internal context class is auto-emitted in the generated file.",
        };
        var forceOpt = SharedOptions.CreateForce();

        var cmd = new Command("csharp", "Generate a C# typed HttpClient for portable Slice routes.")
        {
            projectOpt,
            outputOpt,
            namespaceOpt,
            classOpt,
            jsonContextOpt,
            forceOpt
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var output = parseResult.GetValue(outputOpt);
            var @namespace = parseResult.GetValue(namespaceOpt);
            var className = parseResult.GetValue(classOpt) ?? "SliceApiClient";
            var jsonContext = parseResult.GetValue(jsonContextOpt);
            var force = parseResult.GetValue(forceOpt);

            try
            {
                await RunAsync(project, output, @namespace, className, jsonContext, force, ct).ConfigureAwait(false);
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
        string? project,
        string? output,
        string? @namespace,
        string className,
        string? jsonContextFqn,
        bool force,
        CancellationToken ct)
    {
        var ctx = ProjectContextDiscovery.Discover(project);
        @namespace = CliValidation.RequireNamespace(
            string.IsNullOrWhiteSpace(@namespace) ? $"{ctx.RootNamespace}.Client" : @namespace,
            "Namespace");
        className = CliValidation.RequireClassName(className, "Class");
        if (jsonContextFqn is not null)
        {
            jsonContextFqn = CliValidation.RequireNamespace(jsonContextFqn, "--json-context");
        }

        var discovery = RouteCatalog.DiscoverDetailed(ctx);
        RouteCatalog.WriteAggregatedRouteNotice(discovery);
        var portable = discovery.Routes
            .Where(static route => route.Portability != RouteCatalog.PortabilityAspNetOnly)
            .ToArray();
        foreach (var s in portable.Where(static r => ClientGenerationHelpers.IsNonClientReturnType(
            ClientGenerationHelpers.UnwrapReturnType(r.ReturnType))))
        {
            Console.WriteLine($"// skipped (untyped WasiResponse): {s.EndpointName}");
        }

        var routes = portable
            .Where(static route => !ClientGenerationHelpers.IsNonClientReturnType(
                ClientGenerationHelpers.UnwrapReturnType(route.ReturnType)))
            .ToArray();

        if (routes.Length == 0)
        {
            throw new CliException("No portable or partial Slice routes found for C# client generation.");
        }

        var outputFile = SharedOptions.ResolveOutputFile(output, $"{className}.g.cs", ctx.ProjectDirectory);
        if (File.Exists(outputFile) && !force)
        {
            throw new CliException($"Output file already exists: {outputFile}. Pass --force to overwrite it.");
        }

        var outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var content = RenderClient(@namespace, className, routes, jsonContextFqn);
        await File.WriteAllTextAsync(outputFile, content, ct).ConfigureAwait(false);
        Console.WriteLine($"Generated {outputFile}");
    }

    private static string RenderClient(string @namespace, string className, SliceRouteInfo[] routes, string? jsonContextFqn)
    {
        var autoEmitContext = jsonContextFqn is null;
        // When auto-emitting, the context class is in the same namespace so we can use the short name.
        var contextRef = jsonContextFqn ?? $"{className}JsonContext";

        var groups = routes
            .GroupBy(static route => route.Tag)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var groupNames = groups.ToDictionary(
            static group => group.Key,
            static group => ClientGenerationHelpers.ToPascalIdentifier(group.Key, "Default"),
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("// CS1591: XML doc comments are not required for auto-generated typed client code.");
        sb.AppendLine("#pragma warning disable CS1591");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Net;");
        sb.AppendLine("using System.Net.Http;");
        sb.AppendLine("using System.Net.Http.Json;");
        sb.AppendLine("using System.Text.Json;");
        if (autoEmitContext)
        {
            sb.AppendLine("using System.Text.Json.Serialization;");
        }

        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"namespace {@namespace};");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"public partial class {className}");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly HttpClient _http;");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"    public {className}(HttpClient http)");
        sb.AppendLine("    {");
        sb.AppendLine("        _http = http ?? throw new ArgumentNullException(nameof(http));");
        foreach (var group in groups)
        {
            var groupName = groupNames[group.Key];
            sb.AppendLine(CultureInfo.InvariantCulture, $"        {groupName} = new {groupName}Client(_http, PrepareRequest);");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"    public {className}(HttpMessageHandler handler) : this(new HttpClient(handler)) {{ }}");
        sb.AppendLine();
        sb.AppendLine("    partial void OnRequestPreparing(HttpRequestMessage request);");
        sb.AppendLine();
        sb.AppendLine("    private void PrepareRequest(HttpRequestMessage request) => OnRequestPreparing(request);");
        sb.AppendLine();
        foreach (var group in groups)
        {
            var groupName = groupNames[group.Key];
            sb.AppendLine(CultureInfo.InvariantCulture, $"    public {groupName}Client {groupName} {{ get; }}");
        }

        sb.AppendLine();
        sb.AppendLine("    private static string FormatRouteValue<T>(T value)");
        sb.AppendLine("        => Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"    private static async Task __ThrowApiException(HttpResponseMessage response, string operation, CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine("        SliceProblemDetails? problem = null;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            var __body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("            if (__body.Length > 0)");
        sb.AppendLine("            {");
        sb.AppendLine("                var __mediaType = response.Content.Headers.ContentType?.MediaType;");
        sb.AppendLine("                if (__mediaType is \"application/problem+json\" or \"application/json\")");
        sb.AppendLine("                {");
        sb.AppendLine(CultureInfo.InvariantCulture, $"                    problem = JsonSerializer.Deserialize(__body, {contextRef}.Default.SliceProblemDetails);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex) when (ex is not OperationCanceledException) { }");
        sb.AppendLine("        var __message = problem?.Title ?? $\"{operation} failed: {(int)response.StatusCode} {response.ReasonPhrase}\";");
        sb.AppendLine("        throw new SliceApiException(__message, response.StatusCode, problem);");
        sb.AppendLine("    }");

        foreach (var group in groups)
        {
            sb.AppendLine();
            EmitGroupClient(sb, className, groupNames[group.Key], group.OrderBy(static route => route.FeatureName, StringComparer.Ordinal), contextRef);
        }

        sb.AppendLine();
        sb.AppendLine("    public sealed class SliceProblemDetails");
        sb.AppendLine("    {");
        sb.AppendLine("        public string? Type { get; set; }");
        sb.AppendLine("        public string? Title { get; set; }");
        sb.AppendLine("        public int? Status { get; set; }");
        sb.AppendLine("        public string? Detail { get; set; }");
        sb.AppendLine("        public IReadOnlyDictionary<string, string[]>? Errors { get; set; }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public class SliceApiException : HttpRequestException");
        sb.AppendLine("    {");
        sb.AppendLine("        public SliceProblemDetails? Problem { get; }");
        sb.AppendLine("        public IReadOnlyDictionary<string, string[]>? Errors => Problem?.Errors;");
        sb.AppendLine("        public SliceApiException(string message, HttpStatusCode statusCode, SliceProblemDetails? problem)");
        sb.AppendLine("            : base(message, null, statusCode)");
        sb.AppendLine("        {");
        sb.AppendLine("            Problem = problem;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        // __SliceClientResponse<T> — wraps the typed body with StatusCode + Location so that
        // Created(201) and redirect responses surface their Location header to callers.
        // Note: Redirect(3xx) Location is only observable when HttpClient.AllowAutoRedirect=false;
        // the default (true) transparently follows 3xx so __response is the final 2xx response.
        // The reserved __ prefix prevents collision with user-defined feature Response types.
        sb.AppendLine("    /// <summary>Carries a typed response body together with the HTTP status code and optional Location header.</summary>");
        sb.AppendLine("    public readonly struct __SliceClientResponse<T>");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>The deserialized response body.</summary>");
        sb.AppendLine("        public T Value { get; }");
        sb.AppendLine("        /// <summary>The HTTP status code (e.g. 200, 201).</summary>");
        sb.AppendLine("        public int StatusCode { get; }");
        sb.AppendLine("        /// <summary>The Location header value, or <see langword=\"null\"/> when absent.</summary>");
        sb.AppendLine("        public Uri? Location { get; }");
        sb.AppendLine("        /// <param name=\"value\">Deserialized response body.</param>");
        sb.AppendLine("        /// <param name=\"statusCode\">HTTP status code.</param>");
        sb.AppendLine("        /// <param name=\"location\">Location header (e.g. for 201 Created responses).</param>");
        sb.AppendLine("        public __SliceClientResponse(T value, int statusCode, Uri? location)");
        sb.AppendLine("        {");
        sb.AppendLine("            Value = value;");
        sb.AppendLine("            StatusCode = statusCode;");
        sb.AppendLine("            Location = location;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        // __SliceClientResponse (non-generic) — for non-generic SliceResult routes that have no
        // response body but may carry a Location header (e.g. Created(string location)).
        sb.AppendLine("    /// <summary>Carries the HTTP status code and optional Location header for status-only responses.</summary>");
        sb.AppendLine("    public readonly struct __SliceClientResponse");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>The HTTP status code (e.g. 204).</summary>");
        sb.AppendLine("        public int StatusCode { get; }");
        sb.AppendLine("        /// <summary>The Location header value, or <see langword=\"null\"/> when absent.</summary>");
        sb.AppendLine("        public Uri? Location { get; }");
        sb.AppendLine("        /// <param name=\"statusCode\">HTTP status code.</param>");
        sb.AppendLine("        /// <param name=\"location\">Location header.</param>");
        sb.AppendLine("        public __SliceClientResponse(int statusCode, Uri? location)");
        sb.AppendLine("        {");
        sb.AppendLine("            StatusCode = statusCode;");
        sb.AppendLine("            Location = location;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        if (autoEmitContext)
        {
            sb.AppendLine();
            EmitAutoJsonContext(sb, className, routes);
        }

        return sb.ToString();
    }

    private static void EmitGroupClient(
        StringBuilder sb,
        string outerClassName,
        string groupName,
        IEnumerable<SliceRouteInfo> routes,
        string contextRef)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"    public sealed class {groupName}Client");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly HttpClient _http;");
        sb.AppendLine("        private readonly Action<HttpRequestMessage> _prepareRequest;");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"        internal {groupName}Client(HttpClient http, Action<HttpRequestMessage> prepareRequest)");
        sb.AppendLine("        {");
        sb.AppendLine("            _http = http;");
        sb.AppendLine("            _prepareRequest = prepareRequest;");
        sb.AppendLine("        }");

        foreach (var route in routes)
        {
            sb.AppendLine();
            EmitRouteMethod(sb, outerClassName, route, contextRef);
        }

        sb.AppendLine("    }");
    }

    private static void EmitRouteMethod(StringBuilder sb, string outerClassName, SliceRouteInfo route, string contextRef)
    {
        // payloadType: the inner payload type after unwrapping SliceResult<T> (or "void").
        var payloadType = ToClientType(route, ClientGenerationHelpers.UnwrapReturnType(route.ReturnType));

        // isSliceResult: true when the feature declares SliceResult<T> or non-generic SliceResult.
        // slicePayloadRaw: non-null when generic (SliceResult<T>), null when non-generic.
        var isSliceResult = ClientGenerationHelpers.TryGetSliceResultPayload(route.ReturnType, out _);

        var bodyParameter = ClientGenerationHelpers.FindBodyParameter(route);
        var routeParameters = ClientGenerationHelpers.FindRouteParameters(route);
        var queryParameters = ClientGenerationHelpers.FindQueryParameters(route, routeParameters, bodyParameter);
        var methodParameters = routeParameters
            .Concat(queryParameters)
            .Select(static parameter => $"{parameter.Type} {parameter.Name}")
            .ToList();

        if (bodyParameter is not null)
        {
            methodParameters.Add($"{ToClientType(route, bodyParameter.Type)} {bodyParameter.Name}");
        }

        methodParameters.Add("CancellationToken cancellationToken = default");

        // Determine return type: SliceResult<T> → Task<__SliceClientResponse<T>>,
        // non-generic SliceResult → Task<__SliceClientResponse>, plain T → Task<T>, void → Task.
        var returnType = isSliceResult && payloadType != "void"
            ? $"Task<{outerClassName}.__SliceClientResponse<{payloadType}>>"
            : isSliceResult
                ? $"Task<{outerClassName}.__SliceClientResponse>"
                : payloadType == "void" ? "Task" : $"Task<{payloadType}>";

        sb.AppendLine(CultureInfo.InvariantCulture, $"        public async {returnType} {route.FeatureName}Async({string.Join(", ", methodParameters)})");
        sb.AppendLine("        {");
        sb.AppendLine(CultureInfo.InvariantCulture, $"            var __url = {BuildPathExpression(outerClassName, route.Pattern, routeParameters)};");
        EmitQueryString(sb, outerClassName, queryParameters);

        if (route.Method == "GET" && bodyParameter is null && payloadType != "void")
        {
            sb.AppendLine("            using var __message = new HttpRequestMessage(HttpMethod.Get, __url);");
            sb.AppendLine("            _prepareRequest(__message);");
            sb.AppendLine("            using var __response = await _http.SendAsync(__message, cancellationToken).ConfigureAwait(false);");
            sb.AppendLine("            if (!__response.IsSuccessStatusCode)");
            sb.AppendLine("            {");
            sb.AppendLine(CultureInfo.InvariantCulture, $"                await {outerClassName}.__ThrowApiException(__response, \"{route.FeatureName}\", cancellationToken).ConfigureAwait(false);");
            sb.AppendLine("            }");
            if (isSliceResult)
            {
                // Capture status/location before the response is disposed.
                sb.AppendLine("            var __statusCode = (int)__response.StatusCode;");
                sb.AppendLine("            var __location = __response.Headers.Location;");
                var getPropName = ComputeTypeInfoPropertyName(payloadType);
                sb.AppendLine(CultureInfo.InvariantCulture, $"            var __body = await __response.Content.ReadFromJsonAsync({contextRef}.Default.{getPropName}, cancellationToken).ConfigureAwait(false)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"                ?? throw new HttpRequestException(\"Route '{route.EndpointName}' returned an empty response body.\");");
                sb.AppendLine(CultureInfo.InvariantCulture, $"            return new {outerClassName}.__SliceClientResponse<{payloadType}>(__body, __statusCode, __location);");
            }
            else
            {
                var getPropName = ComputeTypeInfoPropertyName(payloadType);
                sb.AppendLine(CultureInfo.InvariantCulture, $"            return await __response.Content.ReadFromJsonAsync({contextRef}.Default.{getPropName}, cancellationToken).ConfigureAwait(false)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"                ?? throw new HttpRequestException(\"Route '{route.EndpointName}' returned an empty response body.\");");
            }

            sb.AppendLine("        }");
            return;
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"            using var __message = new HttpRequestMessage(new HttpMethod(\"{route.Method}\"), __url);");
        if (bodyParameter is not null)
        {
            var bodyPropName = ComputeTypeInfoPropertyName(ToClientType(route, bodyParameter.Type));
            sb.AppendLine(CultureInfo.InvariantCulture, $"            __message.Content = JsonContent.Create({bodyParameter.Name}, {contextRef}.Default.{bodyPropName});");
        }

        sb.AppendLine("            _prepareRequest(__message);");
        sb.AppendLine("            using var __response = await _http.SendAsync(__message, cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("            if (!__response.IsSuccessStatusCode)");
        sb.AppendLine("            {");
        sb.AppendLine(CultureInfo.InvariantCulture, $"                await {outerClassName}.__ThrowApiException(__response, \"{route.FeatureName}\", cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("            }");

        if (isSliceResult)
        {
            // Capture status/location before the response is disposed.
            sb.AppendLine("            var __statusCode = (int)__response.StatusCode;");
            sb.AppendLine("            var __location = __response.Headers.Location;");
            if (payloadType != "void")
            {
                // SliceResult<T> — deserialize body and wrap with status + location.
                var postPropName = ComputeTypeInfoPropertyName(payloadType);
                sb.AppendLine(CultureInfo.InvariantCulture, $"            var __body = await __response.Content.ReadFromJsonAsync({contextRef}.Default.{postPropName}, cancellationToken).ConfigureAwait(false)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"                ?? throw new HttpRequestException(\"Route '{route.EndpointName}' returned an empty response body.\");");
                sb.AppendLine(CultureInfo.InvariantCulture, $"            return new {outerClassName}.__SliceClientResponse<{payloadType}>(__body, __statusCode, __location);");
            }
            else
            {
                // Non-generic SliceResult — no body, return status + location only.
                sb.AppendLine(CultureInfo.InvariantCulture, $"            return new {outerClassName}.__SliceClientResponse(__statusCode, __location);");
            }

            sb.AppendLine("        }");
            return;
        }

        if (payloadType == "void")
        {
            sb.AppendLine("        }");
            return;
        }

        var propName = ComputeTypeInfoPropertyName(payloadType);
        sb.AppendLine(CultureInfo.InvariantCulture, $"            return await __response.Content.ReadFromJsonAsync({contextRef}.Default.{propName}, cancellationToken).ConfigureAwait(false)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"                ?? throw new HttpRequestException(\"Route '{route.EndpointName}' returned an empty response body.\");");
        sb.AppendLine("        }");
    }

    private static void EmitAutoJsonContext(StringBuilder sb, string className, SliceRouteInfo[] routes)
    {
        var types = CollectJsonSerializableTypes(routes, className).ToArray();
        sb.AppendLine("[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]");
        foreach (var (typeExpr, explicitPropName) in types)
        {
            if (explicitPropName is not null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"[JsonSerializable(typeof({typeExpr}), TypeInfoPropertyName = \"{explicitPropName}\")]");
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"[JsonSerializable(typeof({typeExpr}))]");
            }
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"internal sealed partial class {className}JsonContext : JsonSerializerContext");
        sb.AppendLine("{");
        sb.AppendLine("}");
    }

    internal static IEnumerable<(string TypeExpr, string? ExplicitPropName)> CollectJsonSerializableTypes(
        SliceRouteInfo[] routes, string className)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(string, string?)>();

        foreach (var route in routes)
        {
            var bodyParam = ClientGenerationHelpers.FindBodyParameter(route);
            if (bodyParam is not null)
            {
                var typeExpr = ClientGenerationHelpers.StripGlobal(ToClientType(route, bodyParam.Type));
                if (seen.Add(typeExpr))
                {
                    var computed = ComputeTypeInfoPropertyName(typeExpr);
                    result.Add((typeExpr, computed == ClientGenerationHelpers.ShortName(typeExpr) ? null : computed));
                }
            }

            var responseRaw = ClientGenerationHelpers.UnwrapReturnType(route.ReturnType);
            if (responseRaw is not ("void" or ""))
            {
                var typeExpr = ClientGenerationHelpers.StripGlobal(ToClientType(route, responseRaw));
                if (seen.Add(typeExpr))
                {
                    var computed = ComputeTypeInfoPropertyName(typeExpr);
                    result.Add((typeExpr, computed == ClientGenerationHelpers.ShortName(typeExpr) ? null : computed));
                }
            }
        }

        // Always include SliceProblemDetails from the outer class.
        var pdKey = $"{className}.SliceProblemDetails";
        if (seen.Add(pdKey))
        {
            result.Add((pdKey, null));
        }
        return result;
    }

    internal static string ComputeTypeInfoPropertyName(string typeRef)
    {
        var t = ClientGenerationHelpers.StripGlobal(typeRef);

        // Array: T[] → TList
        if (t.EndsWith("[]", StringComparison.Ordinal))
        {
            return ComputeTypeInfoPropertyName(t[..^2]) + "List";
        }

        // Generic: Outer<Inner>
        var bracket = t.IndexOf('<');
        if (bracket >= 0)
        {
            var outerShort = ClientGenerationHelpers.ShortName(t[..bracket]);
            var innerArgs = t[(bracket + 1)..^1];
            if (outerShort is "IReadOnlyList" or "IList" or "List" or "ICollection" or "IEnumerable")
            {
                return ComputeTypeInfoPropertyName(innerArgs) + "List";
            }

            if (outerShort is "IReadOnlyDictionary" or "IDictionary" or "Dictionary")
            {
                return ComputeTypeInfoPropertyName(ExtractLastGenericArg(innerArgs)) + "Dictionary";
            }

            throw new CliException(
                $"Unsupported generic type '{typeRef}' for source-generated JSON context. " +
                "Use --json-context to provide a custom JsonSerializerContext, or register the type manually " +
                "with an explicit TypeInfoPropertyName.");
        }

        var shortName = ClientGenerationHelpers.ShortName(t);
        // Nested feature types are named Request/Response — combine parent class name to avoid collisions.
        if (shortName is "Request" or "Response")
        {
            var lastDot = t.LastIndexOf('.');
            if (lastDot > 0)
            {
                var parentDot = t.LastIndexOf('.', lastDot - 1);
                var parent = t[(parentDot + 1)..lastDot];
                return parent + shortName;
            }
        }

        return shortName;
    }

    private static string ExtractLastGenericArg(string args)
    {
        var depth = 0;
        var lastComma = -1;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == '<')
            {
                depth++;
            }
            else if (args[i] == '>')
            {
                depth--;
            }
            else if (args[i] == ',' && depth == 0)
            {
                lastComma = i;
            }
        }

        return lastComma >= 0 ? args[(lastComma + 1)..].Trim() : args.Trim();
    }

    private static void EmitQueryString(StringBuilder sb, string outerClassName, SliceRouteParameter[] queryParameters)
    {
        if (queryParameters.Length == 0)
        {
            return;
        }

        sb.AppendLine("            var __query = new List<string>();");
        foreach (var parameter in queryParameters)
        {
            if (ClientGenerationHelpers.NormalizeParameterType(parameter.Type).EndsWith("[]", StringComparison.Ordinal))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"            if ({parameter.Name} is not null)");
                sb.AppendLine("            {");
                sb.AppendLine(CultureInfo.InvariantCulture, $"                foreach (var __value in {parameter.Name})");
                sb.AppendLine("                {");
                sb.AppendLine(CultureInfo.InvariantCulture, $"                    __query.Add(\"{parameter.Name}=\" + {outerClassName}.FormatRouteValue(__value));");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
            }
            else if (parameter.IsNullable)
            {
                // Null nullable params are omitted from the query string rather than emitted as "name="
                // (which the WASI binder would see as Bound("") and could cause silent empty results).
                sb.AppendLine(CultureInfo.InvariantCulture, $"            if ({parameter.Name} is not null)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"                __query.Add(\"{parameter.Name}=\" + {outerClassName}.FormatRouteValue({parameter.Name}));");
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"            __query.Add(\"{parameter.Name}=\" + {outerClassName}.FormatRouteValue({parameter.Name}));");
            }
        }

        sb.AppendLine("            __url += \"?\" + string.Join(\"&\", __query);");
    }

    private static string BuildPathExpression(string outerClassName, string pattern, SliceRouteParameter[] routeParameters)
    {
        var parameterNames = routeParameters.ToDictionary(
            static parameter => parameter.Name,
            static parameter => parameter.Name,
            StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder("$\"");
        var index = 0;
        foreach (Match match in ClientGenerationHelpers.RouteParameterRegex().Matches(pattern))
        {
            sb.Append(EscapeInterpolatedText(pattern[index..match.Index]));
            var routeName = match.Groups["name"].Value;
            if (!parameterNames.TryGetValue(routeName, out var parameterName))
            {
                throw new CliException($"Route parameter '{{{routeName}}}' in '{pattern}' does not match a Handle parameter.");
            }

            sb.Append(CultureInfo.InvariantCulture, $"{{{outerClassName}.FormatRouteValue({parameterName})}}");
            index = match.Index + match.Length;
        }

        sb.Append(EscapeInterpolatedText(pattern[index..]));
        sb.Append('"');
        return sb.ToString();
    }

    private static string ToClientType(SliceRouteInfo route, string type)
    {
        var sb = new StringBuilder(type.Length);
        var index = 0;
        while (index < type.Length)
        {
            var ch = type[index];
            if (!IsIdentifierStart(ch))
            {
                sb.Append(ch);
                index++;
                continue;
            }

            var start = index;
            index++;
            while (index < type.Length && IsIdentifierPart(type[index]))
            {
                index++;
            }

            var identifier = type[start..index];
            if (identifier is "Request" or "Response" && !IsQualifiedTypeMember(type, start))
            {
                sb.Append(CultureInfo.InvariantCulture, $"global::{route.FeatureType}.{identifier}");
            }
            else
            {
                sb.Append(identifier);
            }
        }

        return sb.ToString();
    }

    private static bool IsQualifiedTypeMember(string type, int identifierStart)
    {
        for (var index = identifierStart - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(type[index]))
            {
                continue;
            }

            return type[index] == '.';
        }

        return false;
    }

    private static string EscapeInterpolatedText(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);
    }

    private static bool IsIdentifierStart(char value)
        => value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value)
        => value == '_' || char.IsLetterOrDigit(value);

}
