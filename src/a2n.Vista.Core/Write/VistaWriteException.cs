namespace a2n.Vista.Write;

/// <summary>
/// Base type for the typed write failures raised by the write path. Every write exception carries a
/// machine-readable <see cref="Code"/> (and its stable <see cref="ErrorCode"/> wire string) so the
/// AspNetCore layer can translate it onto the shared RFC 7807 envelope without inspecting a concrete
/// exception type or leaking any internal detail. These types live in Core so both the EF execution
/// layer (which raises them) and the AspNetCore mapper (which consumes them) share one contract
/// without referencing each other (Requirement R14.6, Decision Log D120).
/// </summary>
/// <remarks>
/// Messages carried by these exceptions are constructed by Vista and are safe to surface to clients:
/// they never interpolate provider exception text, SQL, schema/object names, connection strings,
/// masked field values, or non-projected entity fields (Requirements R9.6, R10.5).
/// </remarks>
public abstract class VistaWriteException : Exception
{
    /// <summary>Initializes a new <see cref="VistaWriteException"/>.</summary>
    /// <param name="message">A human-readable, leak-free description of the failure.</param>
    /// <param name="innerException">The underlying exception, when applicable. Never surfaced to clients.</param>
    protected VistaWriteException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>The machine-readable classification of this write failure.</summary>
    public abstract WriteErrorCode Code { get; }

    /// <summary>The stable wire code for <see cref="Code"/> (for example <c>write-validation-failed</c>).</summary>
    public string ErrorCode => Code.ToWireCode();
}
