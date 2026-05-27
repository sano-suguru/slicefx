using SliceFx.TestHost.SliceApp.Services;

namespace SliceFx.TestHost.SliceApp.Features.Audit;

[Feature("POST /audit")]
public static class CreateAuditEntry
{
    public sealed record Request(string Message);

    public sealed record Response(string Recorded);

    public static Response Handle(Request request, AuditRecorder recorder)
    {
        recorder.Record(request.Message);
        return new Response(recorder.GetLatestEntry());
    }
}
