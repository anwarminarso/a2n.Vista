namespace a2n.Vista.Contracts;

/// <summary>
/// Identifies which whitelist path a filter sub-tree originated from, so the executor
/// can validate each leaf against the correct rule set before building an expression.
/// Authoritative behavior: docs/spec/01-view.md §8.3.
/// </summary>
public enum FilterOrigin
{
    /// <summary>
    /// Structured client filter. Each leaf must target a filterable field and use an
    /// operator within that field's allowed operators.
    /// </summary>
    Filter,

    /// <summary>
    /// Global search expansion. Allows <see cref="FilterOperator.Contains"/> only against
    /// searchable string fields.
    /// </summary>
    Search,

    /// <summary>
    /// Contextual/lookup scoping from the client (DynData <c>externalFilter</c> equivalent).
    /// Each leaf must target a field declared <c>Scopable</c>.
    /// </summary>
    Scope,
}
