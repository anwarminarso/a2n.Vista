namespace a2n.Vista.Write;

/// <summary>
/// Thrown when persistence fails because of a database constraint (unique, foreign key, NOT NULL, or
/// CHECK) rather than an optimistic-concurrency conflict. The AspNetCore layer maps it to HTTP 422
/// with <see cref="WriteErrorCode.ConstraintViolation"/> (<c>write-constraint-violation</c>). It is not
/// a 409, and a missing table or any other non-constraint <c>DbUpdateException</c> is a
/// <see cref="VistaPersistenceException"/> instead (Decision Log D158). The provider exception stays on
/// <see cref="Exception.InnerException"/> and is never copied into the client response (R9.6).
/// </summary>
public sealed class VistaConstraintViolationException : VistaWriteException
{
    /// <summary>Initializes a new <see cref="VistaConstraintViolationException"/>.</summary>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is used. Must never contain SQL text, schema/object names, or connection detail.
    /// </param>
    /// <param name="innerException">
    /// The underlying provider constraint exception, when applicable. Logged server-side only; never
    /// surfaced to clients.
    /// </param>
    public VistaConstraintViolationException(string? message = null, Exception? innerException = null)
        : base(
            message ?? "The write violates a database constraint and was not applied.",
            innerException)
    {
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.ConstraintViolation;
}
