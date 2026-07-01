namespace a2n.Vista.Write;

/// <summary>
/// Thrown when an optimistic-concurrency check fails: either the supplied <c>If-Match</c> token does
/// not exactly match the stored row's current token (pre-check), or <c>SaveChanges</c> raised a
/// concurrency violation. The operation is aborted with no partial persistence and the AspNetCore
/// layer maps it to HTTP 409 Conflict with <see cref="WriteErrorCode.ConcurrencyConflict"/>
/// (Requirements R6.3, R6.5).
/// </summary>
public sealed class VistaConcurrencyConflictException : VistaWriteException
{
    /// <summary>Initializes a new <see cref="VistaConcurrencyConflictException"/>.</summary>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is used.
    /// </param>
    /// <param name="innerException">
    /// The underlying provider concurrency exception, when applicable. Logged server-side only; never
    /// surfaced to clients.
    /// </param>
    public VistaConcurrencyConflictException(string? message = null, Exception? innerException = null)
        : base(
            message ?? "The row was modified by another request; the concurrency precondition did not match.",
            innerException)
    {
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.ConcurrencyConflict;
}
