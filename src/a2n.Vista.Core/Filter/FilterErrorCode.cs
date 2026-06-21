namespace a2n.Vista.Filter;

/// <summary>
/// Machine-readable reason a filter leaf was rejected during compilation. Each value maps to a
/// stable wire code (see <see cref="FilterErrorCodes"/>) that the AspNetCore layer surfaces in an
/// RFC 7807 problem document with HTTP 400 (Task 10). Keeping the code here — in Core — lets the
/// filter compiler reject input without taking any HTTP dependency.
/// Authoritative behavior: docs/spec/01-view.md §8.3 and §14.
/// </summary>
public enum FilterErrorCode
{
    /// <summary>The referenced field does not exist in the view projection.</summary>
    UnknownField,

    /// <summary>
    /// The field exists but the requested path is not permitted on it: it is not filterable
    /// (<c>Filter</c> origin) or not searchable / not a string (<c>Search</c> origin).
    /// </summary>
    FieldNotAllowed,

    /// <summary>
    /// The requested operator is not permitted: it is not within the field's allowed operators, it is
    /// not a single atomic operator, or it is not the only operator allowed on the path (for example a
    /// search leaf using something other than <c>Contains</c>).
    /// </summary>
    OperatorNotAllowed,

    /// <summary>The field is not declared <c>Scopable</c>, so it cannot be used as a client scope key.</summary>
    ScopeNotAllowed,

    /// <summary>The supplied value could not be coerced to the field's CLR type, or has the wrong shape.</summary>
    InvalidValue,

    /// <summary>
    /// The request exceeds a complexity hard limit (filter depth, leaf count, string length, or
    /// <c>In</c> value count); a denial-of-service guard (Decision Log D108, §8.2/§8.3).
    /// </summary>
    RequestTooComplex,
}

/// <summary>
/// Stable wire codes for <see cref="FilterErrorCode"/>. These strings are part of the public error
/// contract (problem-detail <c>type</c>/<c>code</c>) and must not change without a breaking-change note.
/// </summary>
public static class FilterErrorCodes
{
    /// <summary>Wire code for <see cref="FilterErrorCode.UnknownField"/>.</summary>
    public const string UnknownField = "filter-unknown-field";

    /// <summary>Wire code for <see cref="FilterErrorCode.FieldNotAllowed"/>.</summary>
    public const string FieldNotAllowed = "filter-field-not-allowed";

    /// <summary>Wire code for <see cref="FilterErrorCode.OperatorNotAllowed"/>.</summary>
    public const string OperatorNotAllowed = "filter-operator-not-allowed";

    /// <summary>Wire code for <see cref="FilterErrorCode.ScopeNotAllowed"/>.</summary>
    public const string ScopeNotAllowed = "filter-scope-not-allowed";

    /// <summary>Wire code for <see cref="FilterErrorCode.InvalidValue"/>.</summary>
    public const string InvalidValue = "filter-invalid-value";

    /// <summary>Wire code for <see cref="FilterErrorCode.RequestTooComplex"/>.</summary>
    public const string RequestTooComplex = "filter-too-complex";

    /// <summary>
    /// Maps a <see cref="FilterErrorCode"/> to its stable wire code.
    /// </summary>
    /// <param name="code">The error code to translate.</param>
    /// <returns>The stable wire string for <paramref name="code"/>.</returns>
    public static string ToWireCode(this FilterErrorCode code) => code switch
    {
        FilterErrorCode.UnknownField => UnknownField,
        FilterErrorCode.FieldNotAllowed => FieldNotAllowed,
        FilterErrorCode.OperatorNotAllowed => OperatorNotAllowed,
        FilterErrorCode.ScopeNotAllowed => ScopeNotAllowed,
        FilterErrorCode.InvalidValue => InvalidValue,
        FilterErrorCode.RequestTooComplex => RequestTooComplex,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown filter error code."),
    };
}
