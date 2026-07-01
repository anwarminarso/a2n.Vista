namespace a2n.Vista.Write;

/// <summary>
/// Thrown when a write model fails model validation. Carries the list of offending field names so the
/// AspNetCore layer can report which field or fields failed on the shared RFC 7807 envelope, mapped to
/// HTTP 400 with <see cref="WriteErrorCode.ValidationFailed"/> (Requirements R1.6, R2.7). No change is
/// persisted when this is raised (R9.7).
/// </summary>
public sealed class VistaValidationException : VistaWriteException
{
    /// <summary>
    /// Initializes a new <see cref="VistaValidationException"/>.
    /// </summary>
    /// <param name="fields">
    /// The names of the fields that failed validation. Copied into an immutable snapshot; may be empty
    /// when the failure is not attributable to a specific field.
    /// </param>
    /// <param name="message">
    /// An optional human-readable, leak-free description. When <see langword="null"/>, a default
    /// message is derived from <paramref name="fields"/>.
    /// </param>
    /// <param name="innerException">The underlying validation exception, when applicable.</param>
    public VistaValidationException(
        IEnumerable<string> fields,
        string? message = null,
        Exception? innerException = null)
        : base(message ?? BuildMessage(fields), innerException)
    {
        Fields = fields is null ? Array.Empty<string>() : fields.ToArray();
    }

    /// <inheritdoc />
    public override WriteErrorCode Code => WriteErrorCode.ValidationFailed;

    /// <summary>The names of the fields that failed validation, in the order supplied.</summary>
    public IReadOnlyList<string> Fields { get; }

    private static string BuildMessage(IEnumerable<string> fields)
    {
        var names = fields is null ? Array.Empty<string>() : fields.ToArray();
        return names.Length == 0
            ? "The write model failed validation."
            : $"The write model failed validation for: {string.Join(", ", names)}.";
    }
}
