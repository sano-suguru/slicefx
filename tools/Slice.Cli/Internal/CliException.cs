namespace Slice.Cli.Internal;

internal sealed class CliException : Exception
{
    public CliException(string message)
        : base(message)
    {
    }
}
