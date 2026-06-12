namespace SliceFx;

/// <summary>
/// Opts this assembly into SliceFx NativeAOT-safe ASP.NET route registration.
/// </summary>
/// <remarks>
/// <para>
/// When this attribute is applied to an assembly, the SliceFx source generator emits
/// AOT-safe request handlers using <c>new RequestDelegate(…)</c> rather than the default
/// typed-delegate path that flows through <c>RequestDelegateFactory</c>. The handlers
/// perform manual JSON body binding and response serialization via
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> resolved from
/// an application-provided <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// annotated with <c>[SliceJsonContext(SliceJsonTarget.AspNet)]</c>.
/// </para>
/// <para>
/// A <c>[SliceJsonContext(SliceJsonTarget.AspNet)]</c> context must declare a
/// <c>[JsonSerializable]</c> entry for every request and response type used by your features.
/// The source generator reports <c>SLICE071</c> when a required root is missing.
/// </para>
/// <para>
/// Publishing with <c>PublishAot=true</c> will fail on any <c>IL2026</c>/<c>IL3050</c>
/// diagnostic (the project must have <c>TreatWarningsAsErrors</c> or the publishing
/// analyzer will surface them). Enabling this attribute and fixing all <c>SLICE070</c>–
/// <c>SLICE072</c> diagnostics guarantees a clean AOT publish.
/// </para>
/// <para>
/// The default non-AOT registration path (via <c>RequestDelegateFactory</c>) remains
/// active for assemblies that do not carry this attribute, preserving full backwards
/// compatibility.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class SliceAspNetAotAttribute : Attribute { }
