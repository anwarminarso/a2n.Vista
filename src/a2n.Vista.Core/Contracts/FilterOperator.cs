namespace a2n.Vista.Contracts;

/// <summary>
/// Neutral set of filter operators a client may request against a view field.
/// Authoritative shape: docs/spec/01-view.md §8.
/// </summary>
/// <remarks>
/// Declared as <see cref="FlagsAttribute"/> so a field can advertise the set of
/// operators it allows (see <c>FieldMetadata.AllowedOperators</c>). Individual
/// <c>FilterLeaf</c> values always carry exactly one operator; the flags grouping
/// (<see cref="Range"/>, <see cref="Text"/>) is a convenience for declaring
/// whitelists during authoring.
/// </remarks>
[Flags]
public enum FilterOperator
{
    /// <summary>No operator. Used as the empty whitelist value.</summary>
    None = 0,

    /// <summary>Equality comparison.</summary>
    Equals = 1,

    /// <summary>Inequality comparison.</summary>
    NotEquals = 2,

    /// <summary>Strictly greater-than comparison.</summary>
    GreaterThan = 4,

    /// <summary>Greater-than-or-equal comparison.</summary>
    GreaterThanOrEqual = 8,

    /// <summary>Strictly less-than comparison.</summary>
    LessThan = 16,

    /// <summary>Less-than-or-equal comparison.</summary>
    LessThanOrEqual = 32,

    /// <summary>Substring match (server-determined case-sensitivity, §8.2).</summary>
    Contains = 64,

    /// <summary>Prefix match.</summary>
    StartsWith = 128,

    /// <summary>Suffix match.</summary>
    EndsWith = 256,

    /// <summary>Membership in a supplied set of values.</summary>
    In = 512,

    /// <summary>Inclusive range between two values.</summary>
    Between = 1024,

    /// <summary>Null check.</summary>
    IsNull = 2048,

    /// <summary>Convenience grouping for range-style numeric/date filtering.</summary>
    Range = GreaterThanOrEqual | LessThanOrEqual | Between,

    /// <summary>Convenience grouping for text-style filtering.</summary>
    Text = Equals | NotEquals | Contains | StartsWith | EndsWith | IsNull,
}
