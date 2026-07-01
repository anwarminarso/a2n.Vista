namespace a2n.Vista.Write;

/// <summary>
/// Thrown when a view that declares a concurrency token receives an update or delete without a usable
/// <c>If-Match</c> precondition (the header is missing, empty, or whitespace-only). The AspNetCore
/// layer maps it to HTTP 428 Precondition Required with <see cref="WriteErrorCode.PreconditionRequired"/>,
/// and no change is persisted (Requirement R6.2).
/// </summary>
public sealed class VistaPreconditionRequiredException : VistaWriteException
{
    /// <summary>Initializes a new <see cref="VistaPreconditionRequiredException"/>.</summary>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is used.
    /// </param>
    /// <param name="innerException">The underlying exception, when applicable.</param>
    public VistaPreconditionRequiredException(string? message = null, Exception? innerException = null)
        : base(
            message ?? "This view requires an 'If-Match' precondition for update and delete; the header was missing or empty.",
            innerException)
    {
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.PreconditionRequired;
}
