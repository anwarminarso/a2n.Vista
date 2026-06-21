namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// Thrown when an action-endpoint request body is malformed or missing required content (for example
/// invalid JSON, or a Detail request without a <c>key</c>). Maps to <c>400 Bad Request</c> via
/// <c>VistaProblemResults</c> (Decision Log D110).
/// </summary>
public sealed class VistaInvalidRequestException : Exception
{
    /// <summary>Initializes a new <see cref="VistaInvalidRequestException"/>.</summary>
    /// <param name="message">A human-readable description of why the request is invalid.</param>
    public VistaInvalidRequestException(string message)
        : base(message)
    {
    }
}
