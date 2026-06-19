using a2n.Vista.Contracts;

namespace a2n.Vista.Filter;

/// <summary>
/// Thrown by <see cref="FilterCompiler"/> when a client filter leaf violates the tri-whitelist
/// (field/operator/scope) or carries a value that cannot be coerced to the target field type.
/// This is how a whitelist violation surfaces from Core: it is a plain CLR exception with a
/// machine-readable <see cref="Code"/> and the offending <see cref="Field"/>/<see cref="Operator"/>,
/// so the AspNetCore layer (Task 10) can map it to an RFC 7807 HTTP 400 without Core taking any HTTP
/// dependency. Authoritative behavior: docs/spec/01-view.md §8.3 and §14; Requirements R5.5, R5.6,
/// R6.2, R9.2.
/// </summary>
public sealed class FilterValidationException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="FilterValidationException"/>.
    /// </summary>
    /// <param name="code">The machine-readable rejection reason.</param>
    /// <param name="message">A human-readable description of the violation.</param>
    /// <param name="field">The offending field name, when applicable.</param>
    /// <param name="operator">The offending operator, when applicable.</param>
    /// <param name="innerException">The underlying exception (for example a failed value conversion).</param>
    public FilterValidationException(
        FilterErrorCode code,
        string message,
        string? field = null,
        FilterOperator? @operator = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Field = field;
        Operator = @operator;
    }

    /// <summary>The machine-readable rejection reason.</summary>
    public FilterErrorCode Code { get; }

    /// <summary>The stable wire code for <see cref="Code"/> (for example <c>filter-field-not-allowed</c>).</summary>
    public string ErrorCode => Code.ToWireCode();

    /// <summary>The offending field name, or <see langword="null"/> when not field-specific.</summary>
    public string? Field { get; }

    /// <summary>The offending operator, or <see langword="null"/> when not operator-specific.</summary>
    public FilterOperator? Operator { get; }
}
