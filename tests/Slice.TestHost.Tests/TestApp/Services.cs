namespace Slice.TestHost.TestApp;

public interface IMessageService
{
    string Message { get; }
}

public sealed class DefaultMessageService : IMessageService
{
    public string Message => "app";
}

public sealed class OtherMessageService : IMessageService
{
    public string Message => "other";
}

public sealed class ReplacementMessageService : IMessageService
{
    public string Message => "test";
}
