namespace a2n.Vista.Write;

/// <summary>
/// Machine-readable reason a write (Create/Update/Delete) was rejected. Each value maps to a stable
/// wire code (see <see cref="WriteErrorCodes"/>) that the AspNetCore layer surfaces on the shared
/// RFC 7807 problem-details envelope via <c>extensions["code"]</c> — the same mechanism the read path
/// uses for <c>FilterValidationException</c> (Decision Log D120). Keeping the vocabulary here — in
/// Core — lets both the EF execution layer and the AspNetCore mapper raise and consume typed write
/// failures without either referencing the other (Requirement R14.6).
/// Authoritative behavior: docs/spec write-path §"Error Handling".
/// </summary>
public enum WriteErrorCode
{
    /// <summary>The request body is not valid JSON (R9.1).</summary>
    MalformedBody,

    /// <summary>An update/delete request omitted a required primary key (R9.2, R2.8, R5.5).</summary>
    MissingKey,

    /// <summary>A supplied key value could not be coerced to the key field's CLR type (R9.3).</summary>
    KeyTypeCoercion,

    /// <summary>
    /// A composite key did not supply a value for every field in the view's ordered <c>KeyFields</c>,
    /// or named a field absent from them (R3.7).
    /// </summary>
    IncompleteKey,

    /// <summary>The write model failed model validation (R1.6, R2.7).</summary>
    ValidationFailed,

    /// <summary>
    /// A view that declares a concurrency token received no usable <c>If-Match</c> precondition
    /// (missing, empty, or whitespace-only) (R6.2).
    /// </summary>
    PreconditionRequired,

    /// <summary>
    /// The supplied <c>If-Match</c> token did not match the stored row, the header could not be
    /// used as a concurrency predicate, or <c>SaveChanges</c> raised <c>DbUpdateConcurrencyException</c>
    /// (R6.3, R6.5, D159). HTTP 412. The wire code stays <c>write-concurrency-conflict</c>. The old
    /// <c>write-conflict</c> string is retired and is no longer emitted.
    /// </summary>
    ConcurrencyConflict,

    /// <summary>
    /// A database constraint violation (unique, foreign key, NOT NULL, or CHECK) surfaced from
    /// persistence (D158). HTTP 422 <c>write-constraint-violation</c> — not a conflict, and not
    /// used for a missing table or any other non-constraint <c>DbUpdateException</c>.
    /// </summary>
    ConstraintViolation,

    /// <summary>
    /// <c>SaveChanges</c> failed for a reason other than a concurrency conflict or a database
    /// constraint violation (for example a missing table or a provider I/O failure). Mapped to
    /// HTTP 500. The client message stays leak-free (Decision Log D158; spec 01 §14.1, spec 05 §9.2).
    /// </summary>
    PersistenceFailed,

    /// <summary>A bulk (array) body was posted while bulk execution is not enabled (R15.1).</summary>
    BulkNotEnabled,

    /// <summary>
    /// A write was attempted on a view registered as metadata-only (no executable write plan);
    /// surfaced internally and rendered as an indistinguishable not-found response (R12.4).
    /// </summary>
    NoWritePlan,
}

/// <summary>
/// Stable wire codes for <see cref="WriteErrorCode"/>. These strings are part of the public error
/// contract (problem-detail <c>code</c>) and must not change without a breaking-change note.
/// </summary>
public static class WriteErrorCodes
{
    /// <summary>Wire code for <see cref="WriteErrorCode.MalformedBody"/>.</summary>
    public const string MalformedBody = "write-malformed-body";

    /// <summary>Wire code for <see cref="WriteErrorCode.MissingKey"/>.</summary>
    public const string MissingKey = "write-missing-key";

    /// <summary>Wire code for <see cref="WriteErrorCode.KeyTypeCoercion"/>.</summary>
    public const string KeyTypeCoercion = "write-key-type";

    /// <summary>Wire code for <see cref="WriteErrorCode.IncompleteKey"/>.</summary>
    public const string IncompleteKey = "write-incomplete-key";

    /// <summary>Wire code for <see cref="WriteErrorCode.ValidationFailed"/>.</summary>
    public const string ValidationFailed = "write-validation-failed";

    /// <summary>Wire code for <see cref="WriteErrorCode.PreconditionRequired"/>.</summary>
    public const string PreconditionRequired = "write-precondition-required";

    /// <summary>
    /// Wire code for <see cref="WriteErrorCode.ConcurrencyConflict"/>. HTTP 412
    /// <c>write-concurrency-conflict</c> (Decision Log D159).
    /// </summary>
    public const string ConcurrencyConflict = "write-concurrency-conflict";

    /// <summary>
    /// Wire code for <see cref="WriteErrorCode.ConstraintViolation"/>. HTTP 422. Not a conflict.
    /// </summary>
    public const string ConstraintViolation = "write-constraint-violation";

    /// <summary>Wire code for <see cref="WriteErrorCode.PersistenceFailed"/>.</summary>
    public const string PersistenceFailed = "write-persistence-failed";

    /// <summary>Wire code for <see cref="WriteErrorCode.BulkNotEnabled"/>.</summary>
    public const string BulkNotEnabled = "write-bulk-not-enabled";

    /// <summary>Wire code for <see cref="WriteErrorCode.NoWritePlan"/>.</summary>
    public const string NoWritePlan = "write-no-plan";

    /// <summary>
    /// Maps a <see cref="WriteErrorCode"/> to its stable wire code.
    /// </summary>
    /// <param name="code">The error code to translate.</param>
    /// <returns>The stable wire string for <paramref name="code"/>.</returns>
    public static string ToWireCode(this WriteErrorCode code) => code switch
    {
        WriteErrorCode.MalformedBody => MalformedBody,
        WriteErrorCode.MissingKey => MissingKey,
        WriteErrorCode.KeyTypeCoercion => KeyTypeCoercion,
        WriteErrorCode.IncompleteKey => IncompleteKey,
        WriteErrorCode.ValidationFailed => ValidationFailed,
        WriteErrorCode.PreconditionRequired => PreconditionRequired,
        WriteErrorCode.ConcurrencyConflict => ConcurrencyConflict,
        WriteErrorCode.ConstraintViolation => ConstraintViolation,
        WriteErrorCode.PersistenceFailed => PersistenceFailed,
        WriteErrorCode.BulkNotEnabled => BulkNotEnabled,
        WriteErrorCode.NoWritePlan => NoWritePlan,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown write error code."),
    };
}
