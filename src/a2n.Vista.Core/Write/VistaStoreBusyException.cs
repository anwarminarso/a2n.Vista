namespace a2n.Vista.Write;

/// <summary>
/// Thrown when <c>SaveChanges</c> fails because the store is briefly locked
/// (<c>SQLITE_BUSY</c> / <c>SQLITE_LOCKED</c>, or a provider exception marked transient).
/// The AspNetCore layer maps this to HTTP 503 with <c>Retry-After</c> and the wire code
/// <c>write-busy</c> (Decision Log D160). It is not a lost update and not a persistence defect.
/// </summary>
public sealed class VistaStoreBusyException : VistaWriteException
{
    /// <summary>The <c>Retry-After</c> delta, in seconds, advertised with the 503 response.</summary>
    public const int RetryAfterSeconds = 2;

    /// <summary>Initializes a new <see cref="VistaStoreBusyException"/>.</summary>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is used. Must never contain SQL text, schema/object names, or connection detail.
    /// </param>
    /// <param name="innerException">
    /// The underlying provider exception, when applicable. Logged server-side only; never surfaced
    /// to clients.
    /// </param>
    public VistaStoreBusyException(string? message = null, Exception? innerException = null)
        : base(
            message ?? "The store is busy. Retry the write.",
            innerException)
    {
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.StoreBusy;
}
