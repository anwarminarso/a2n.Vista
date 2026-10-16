namespace a2n.Vista.Write;

/// <summary>
/// Legacy name for a constraint failure. New code raises <see cref="VistaConstraintViolationException"/>.
/// This type is HTTP 422 <c>write-constraint-violation</c>, not HTTP 409. The 409 <c>write-conflict</c>
/// code is <see cref="VistaConcurrencyConflictException"/> (Decision Log D154).
/// </summary>
public sealed class VistaWriteConflictException : VistaWriteException
{
    /// <summary>Initializes a new <see cref="VistaWriteConflictException"/>.</summary>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is used. Must never contain SQL text, schema/object names, or connection detail.
    /// </param>
    /// <param name="innerException">
    /// The underlying provider exception, when applicable. Logged server-side only; never surfaced
    /// to clients.
    /// </param>
    public VistaWriteConflictException(string? message = null, Exception? innerException = null)
        : base(
            message ?? "The write violates a database constraint and was not applied.",
            innerException)
    {
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.ConstraintViolation;
}
