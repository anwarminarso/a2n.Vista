namespace a2n.Vista.Write;

/// <summary>
/// Thrown when a write request carries a body structured as a JSON array (a bulk batch) while bulk
/// execution is not enabled in this milestone. Nothing is created, updated, or deleted, and the
/// AspNetCore layer maps it to HTTP 400 with <see cref="WriteErrorCode.BulkNotEnabled"/>
/// (Requirement R15.1). The <c>AllowBulk</c> authoring flag remains a build-time opt-in that does not,
/// by itself, enable a bulk execution path here (R15.2).
/// </summary>
public sealed class VistaBulkNotEnabledException : VistaWriteException
{
    /// <summary>Initializes a new <see cref="VistaBulkNotEnabledException"/>.</summary>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is used.
    /// </param>
    /// <param name="innerException">The underlying exception, when applicable.</param>
    public VistaBulkNotEnabledException(string? message = null, Exception? innerException = null)
        : base(
            message ?? "Bulk writes are not enabled; submit a single entity rather than an array.",
            innerException)
    {
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.BulkNotEnabled;
}
