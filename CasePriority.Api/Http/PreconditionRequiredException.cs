namespace CasePriority.Api.Http;

/// <summary>
/// Raised when a conditional mutation is attempted without the required
/// If-Match header. The exception handler maps it to 428 Precondition Required.
/// </summary>
public sealed class PreconditionRequiredException : Exception
{
    public PreconditionRequiredException(string message)
        : base(message)
    {
    }
}
