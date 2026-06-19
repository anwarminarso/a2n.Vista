namespace a2n.Vista.Contracts;

/// <summary>
/// A single ordering instruction within a <see cref="ViewQueryRequest"/>.
/// Multiple specs are applied in list order (primary sort first).
/// Authoritative shape: docs/spec/01-view.md §8.
/// </summary>
/// <param name="Field">The view field to order by.</param>
/// <param name="Descending">
/// <see langword="true"/> for descending order; <see langword="false"/> (default) for ascending.
/// </param>
public sealed record SortSpec(string Field, bool Descending = false);
