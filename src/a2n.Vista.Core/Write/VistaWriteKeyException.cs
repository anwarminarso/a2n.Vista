namespace a2n.Vista.Write;

/// <summary>
/// Thrown when the primary key supplied for an update or delete cannot be resolved against the view's
/// ordered <c>KeyFields</c>: the key is missing, incomplete (a composite key omits a field or names a
/// field absent from <c>KeyFields</c>), or a supplied value cannot be coerced to the key member's CLR
/// type. The operation is aborted before any row is loaded or mutated and the AspNetCore layer maps it
/// to HTTP 400 with the carried <see cref="WriteErrorCode"/> (Requirements R2.8, R3.6, R3.7, R5.5,
/// R9.2, R9.3).
/// </summary>
/// <remarks>
/// This type lives in Core so the EF execution layer can raise it while normalizing/coercing a write
/// key, and the AspNetCore mapper can translate it onto the shared RFC 7807 envelope, without either
/// layer referencing the other (Requirement R14.6, Decision Log D120). It is the write-path counterpart
/// of the read path's key-shape/coercion failures: those surface as <c>FilterValidationException</c>,
/// but writes carry the dedicated <c>write-*</c> vocabulary so a client can tell a malformed write key
/// from a malformed read filter.
/// </remarks>
public sealed class VistaWriteKeyException : VistaWriteException
{
    /// <summary>
    /// Initializes a new <see cref="VistaWriteKeyException"/>.
    /// </summary>
    /// <param name="code">
    /// The key-failure classification. Must be one of <see cref="WriteErrorCode.MissingKey"/>,
    /// <see cref="WriteErrorCode.IncompleteKey"/>, or <see cref="WriteErrorCode.KeyTypeCoercion"/>.
    /// </param>
    /// <param name="message">A human-readable, leak-free description of the failure.</param>
    /// <param name="field">
    /// The offending key field name, when the failure is attributable to a specific field;
    /// <see langword="null"/> otherwise.
    /// </param>
    /// <param name="innerException">The underlying exception, when applicable. Never surfaced to clients.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="code"/> is not one of the three key-failure codes.
    /// </exception>
    public VistaWriteKeyException(
        WriteErrorCode code,
        string message,
        string? field = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (code is not (WriteErrorCode.MissingKey or WriteErrorCode.IncompleteKey or WriteErrorCode.KeyTypeCoercion))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "A VistaWriteKeyException must carry a key-failure code (MissingKey, IncompleteKey, or KeyTypeCoercion).");
        }

        Code = code;
        Field = field;
    }

    /// <inheritdoc />
    public override WriteErrorCode Code { get; }

    /// <summary>The offending key field name, or <see langword="null"/> when not field-specific.</summary>
    public string? Field { get; }
}
