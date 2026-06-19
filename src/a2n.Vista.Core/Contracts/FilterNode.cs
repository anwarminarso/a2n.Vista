namespace a2n.Vista.Contracts;

/// <summary>
/// Single neutral filter tree shared by every adapter (DataTables, jQuery-QueryBuilder,
/// AG Grid, OData, ...). Adapters translate client requests into this structure before
/// it reaches the Core executor. Authoritative shape: docs/spec/01-view.md §8.
/// </summary>
/// <remarks>
/// This is a closed hierarchy: a node is exactly one of
/// <see cref="FilterLeaf"/>, <see cref="FilterAnd"/>, <see cref="FilterOr"/> or
/// <see cref="FilterNot"/>.
/// </remarks>
public abstract record FilterNode;

/// <summary>
/// A single predicate against a view field, e.g. <c>Equals(Status, "Active")</c>.
/// </summary>
/// <param name="Field">The view field name the predicate applies to.</param>
/// <param name="Op">The single operator to evaluate (never a flags combination).</param>
/// <param name="Value">The comparison value, or <see langword="null"/> for operators such as <see cref="FilterOperator.IsNull"/>.</param>
public sealed record FilterLeaf(string Field, FilterOperator Op, object? Value) : FilterNode;

/// <summary>Conjunction: all <paramref name="Children"/> must match.</summary>
/// <param name="Children">The child nodes combined with logical AND.</param>
public sealed record FilterAnd(IReadOnlyList<FilterNode> Children) : FilterNode;

/// <summary>Disjunction: any of <paramref name="Children"/> may match.</summary>
/// <param name="Children">The child nodes combined with logical OR.</param>
public sealed record FilterOr(IReadOnlyList<FilterNode> Children) : FilterNode;

/// <summary>Negation of a single child node.</summary>
/// <param name="Child">The node whose result is inverted.</param>
public sealed record FilterNot(FilterNode Child) : FilterNode;
