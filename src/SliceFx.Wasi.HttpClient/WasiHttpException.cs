namespace SliceFx.Wasi.HttpClient;

/// <summary>
/// Exception thrown when the WASI host reports an error sending an outgoing HTTP request.
/// </summary>
public sealed class WasiHttpException : Exception
{
    /// <summary>Initializes the exception with a message describing the error.</summary>
    public WasiHttpException(string message) : base(message) { }

    /// <summary>Initializes the exception with a message and an inner exception.</summary>
    public WasiHttpException(string message, Exception inner) : base(message, inner) { }
}
