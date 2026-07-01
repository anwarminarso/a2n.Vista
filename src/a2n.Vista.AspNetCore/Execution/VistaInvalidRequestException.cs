using a2n.Vista.Write;

namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// Thrown when an action-endpoint request body is malformed or missing required content (for example
/// invalid JSON, or a Detail request without a <c>key</c>). Maps to <c>400 Bad Request</c> via
/// <c>VistaProblemResults</c> (Decision Log D110).
/// </summary>
/// <remarks>
/// For write requests the exception can additionally carry a <see cref="WriteErrorCode"/> so the shared
/// RFC 7807 mapper can surface the precise machine-readable write code (for example
/// <c>write-malformed-body</c> or <c>write-missing-key</c>) instead of the generic
/// <c>invalid-request</c> code (Decision Log D120). When <see cref="WriteErrorCode"/> is
/// <see langword="null"/>, the read-path <c>invalid-request</c> classification applies.
/// </remarks>
public sealed class VistaInvalidRequestException : Exception
{
    /// <summary>Initializes a new <see cref="VistaInvalidRequestException"/> without a write classification.</summary>
    /// <param name="message">A human-readable description of why the request is invalid.</param>
    public VistaInvalidRequestException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="VistaInvalidRequestException"/> carrying a machine-readable
    /// <paramref name="writeErrorCode"/> so the mapper can emit the precise write error code.
    /// </summary>
    /// <param name="message">A human-readable, leak-free description of why the request is invalid.</param>
    /// <param name="writeErrorCode">The write-path classification for this failure.</param>
    public VistaInvalidRequestException(string message, WriteErrorCode writeErrorCode)
        : base(message)
    {
        WriteErrorCode = writeErrorCode;
    }

    /// <summary>
    /// The write-path classification for this failure, or <see langword="null"/> for a read-path
    /// invalid request. Drives <c>extensions["code"]</c> on the shared problem-details envelope.
    /// </summary>
    public WriteErrorCode? WriteErrorCode { get; }
}
