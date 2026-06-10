using System.ComponentModel.DataAnnotations;

namespace SliceFx;

/// <summary>
/// Validates that a <see cref="string"/> property is an absolute HTTPS URL.
/// Unlike <see cref="UrlAttribute"/>, this attribute rejects http and ftp schemes.
/// </summary>
/// <remarks>
/// The source generator emits a compile-time <c>Uri.TryCreate</c> + scheme check,
/// so no reflection is required at runtime. The attribute is valid on ASP.NET,
/// WASI, and Lambda function-per-feature paths.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class HttpsUrlAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new <see cref="HttpsUrlAttribute"/> with the default error message.
    /// </summary>
    public HttpsUrlAttribute()
        : base("The {0} field is not a valid HTTPS URL.")
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        return value is string s
            && Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture, ErrorMessageString, name);
}
