namespace a2n.Vista.Write;

/// <summary>
/// Thrown when <c>SaveChanges</c> fails for a reason that is neither an optimistic-concurrency
/// conflict nor a database constraint violation — for example a missing table, a broken connection,
/// or any other provider failure. The EF layer raises this with a fixed, safe message; the AspNetCore
/// layer maps it to HTTP 500 with <see cref="WriteErrorCode.PersistenceFailed"/>. The original
/// provider exception is kept as <see cref="Exception.InnerException"/> for server-side logging and
/// is never copied into the client response (spec 01 §14.1, spec 05 §9.2).
/// </summary>
public sealed class VistaPersistenceException : VistaWriteException
{
    /// <summary>Initializes a new <see cref="VistaPersistenceException"/>.</summary>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is used. Must never contain SQL text, schema/object names, or connection detail.
    /// </param>
    /// <param name="innerException">
    /// The underlying provider persistence exception, when applicable. Logged server-side only; never
    /// surfaced to clients.
    /// </param>
    public VistaPersistenceException(string? message = null, Exception? innerException = null)
        : base(
            message ?? "The write could not be saved.",
            innerException)
    {
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.PersistenceFailed;
}
