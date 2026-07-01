namespace a2n.Vista.Write;

/// <summary>
/// Thrown when persistence fails due to a database constraint violation (for example a unique-index or
/// foreign-key breach surfaced as a provider <c>DbUpdateException</c>). The EF layer catches the
/// provider exception and raises this with a fixed, safe message; the AspNetCore layer maps it to
/// HTTP 409 Conflict with <see cref="WriteErrorCode.WriteConflict"/> (Requirement R9.4). The original
/// provider exception is logged server-side only and never surfaced to clients (R9.6).
/// </summary>
public sealed class VistaWriteConflictException : VistaWriteException
{
    /// <summary>Initializes a new <see cref="VistaWriteConflictException"/>.</summary>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is used. Must never contain SQL text, schema/object names, or connection detail.
    /// </param>
    /// <param name="innerException">
    /// The underlying provider persistence exception, when applicable. Logged server-side only; never
    /// surfaced to clients.
    /// </param>
    public VistaWriteConflictException(string? message = null, Exception? innerException = null)
        : base(
            message ?? "The write conflicts with a database constraint and was not applied.",
            innerException)
    {
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.WriteConflict;
}
