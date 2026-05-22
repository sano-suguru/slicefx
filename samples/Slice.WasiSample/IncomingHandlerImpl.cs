using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Slice.Wasi;
using ITypes = ProxyWorld.wit.imports.wasi.http.v0_2_0.ITypes;

#pragma warning disable CA1707, CA1711
namespace ProxyWorld.wit.exports.wasi.http.v0_2_0;

public class IncomingHandlerImpl : IIncomingHandler
{
    private const int MaxRequestBodyBytes = 1024 * 1024;
    private const int MaxResponseWriteChunkBytes = 4096;

    private static readonly WasiApp _app = CreateApp();

    private static WasiApp CreateApp()
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddSlice();
        builder.Services.AddSingleton(TimeProvider.System);
        return builder.Build();
    }

    public static void Handle(ITypes.IncomingRequest request, ITypes.ResponseOutparam responseOut)
    {
        var method = GetMethod(request.Method());
        var pathWithQuery = request.PathWithQuery() ?? "/";
        var qIndex = pathWithQuery.IndexOf('?');
        var path = qIndex >= 0 ? pathWithQuery[..qIndex] : pathWithQuery;
        var query = qIndex >= 0 ? pathWithQuery[(qIndex + 1)..] : null;
        var headers = ReadHeaders(request.Headers());

        WasiResponse workerResp;
        try
        {
            var body = ReadBody(request, headers);
            var workerReq = new WasiRequest(method, path, headers, query, body);
            workerResp = _app.DispatchAsync(workerReq).GetAwaiter().GetResult();
        }
        catch (RequestBodyTooLargeException)
        {
            workerResp = SliceResult.Problem(413, "Payload Too Large", $"Request body exceeds {MaxRequestBodyBytes} bytes.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            workerResp = SliceResult.Problem(500, "Internal Server Error", "An unexpected error occurred.");
        }

        SendResponse(responseOut, workerResp);
    }

    private static string GetMethod(ITypes.Method method) => method.Tag switch
    {
        ITypes.Method.Tags.Get => "GET",
        ITypes.Method.Tags.Post => "POST",
        ITypes.Method.Tags.Put => "PUT",
        ITypes.Method.Tags.Delete => "DELETE",
        ITypes.Method.Tags.Patch => "PATCH",
        ITypes.Method.Tags.Head => "HEAD",
        ITypes.Method.Tags.Options => "OPTIONS",
        ITypes.Method.Tags.Connect => "CONNECT",
        ITypes.Method.Tags.Trace => "TRACE",
        ITypes.Method.Tags.Other => method.AsOther,
        _ => "GET",
    };

    private static Dictionary<string, string> ReadHeaders(ITypes.Fields fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in fields.Entries())
        {
            result[name] = Encoding.UTF8.GetString(value);
        }
        return result;
    }

    private static byte[]? ReadBody(ITypes.IncomingRequest request, Dictionary<string, string> headers)
    {
        if (headers.TryGetValue("Content-Length", out var contentLength)
            && long.TryParse(contentLength, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredLength)
            && declaredLength > MaxRequestBodyBytes)
        {
            throw new RequestBodyTooLargeException();
        }

        ITypes.IncomingBody inBody;
        try
        {
            inBody = request.Consume();
        }
        catch (ProxyWorld.WitException)
        {
            return null;
        }

        using var body = inBody;
        using var stream = body.Stream();
        var writer = new ArrayBufferWriter<byte>();
        try
        {
            while (true)
            {
                var chunk = stream.BlockingRead(65536);
                if (chunk.Length == 0)
                {
                    break;
                }

                if (writer.WrittenCount + chunk.Length > MaxRequestBodyBytes)
                {
                    throw new RequestBodyTooLargeException();
                }

                writer.Write(chunk);
            }
        }
        catch (ProxyWorld.WitException)
        {
            // Stream ended (EOF represented as a WitException from the WASI runtime).
        }

        if (writer.WrittenCount == 0)
        {
            return null;
        }

        return writer.WrittenSpan.ToArray();
    }

    private static void SendResponse(ITypes.ResponseOutparam responseOut, WasiResponse workerResp)
    {
        var headerList = new List<(string, byte[])>(workerResp.Headers.Count + 1);
        foreach (var (k, v) in workerResp.Headers)
        {
            headerList.Add((k.ToLowerInvariant(), Encoding.UTF8.GetBytes(v)));
        }

        ITypes.Fields fields;
        try
        {
            fields = ITypes.Fields.FromList(headerList);
        }
        catch (ProxyWorld.WitException<ITypes.HeaderError>)
        {
            fields = new ITypes.Fields();
        }

        var response = new ITypes.OutgoingResponse(fields);
        response.SetStatusCode((ushort)workerResp.Status);

        var outBody = response.Body();
        ITypes.ResponseOutparam.Set(responseOut,
            Result<ITypes.OutgoingResponse, ITypes.ErrorCode>.Ok(response));

        if (workerResp.Body.Length > 0)
        {
            using var stream = outBody.Write();
            var remaining = workerResp.Body.AsSpan();
            while (!remaining.IsEmpty)
            {
                var writable = stream.CheckWrite();
                if (writable == 0)
                {
                    stream.BlockingFlush();
                    continue;
                }

                var count = (int)Math.Min((ulong)Math.Min(remaining.Length, MaxResponseWriteChunkBytes), writable);
                stream.Write(remaining[..count]);
                remaining = remaining[count..];
            }

            stream.BlockingFlush();
        }

        ITypes.OutgoingBody.Finish(outBody, null);
    }

    private sealed class RequestBodyTooLargeException : Exception
    {
    }
}
#pragma warning restore CA1707, CA1711
