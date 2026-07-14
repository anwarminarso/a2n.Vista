namespace a2n.Vista.Client.TypeScript.Emit;

/// <summary>
/// The single source of truth for how the emitters order everything they walk: types, view clients, and
/// object members are ordered by a stable total order derived solely from their declared names, using a
/// fixed, ordinal, case-sensitive comparison, independent of the document's member enumeration order
/// (Requirement 9.2).
/// </summary>
/// <remarks>
/// <para>
/// Determinism is the stabilizing property of the whole generator (Requirement 9): the same document and
/// config must produce byte-for-byte identical output on every run and every operating system. That
/// guarantee rests on two invariants — the emitters read only the in-memory model (no clock, environment,
/// or filesystem enumeration), and every collection they walk is pre-sorted here. Centralizing the ordering
/// in one helper keeps the comparison identical for every emitter and removes any chance of one emitter
/// drifting to a culture-sensitive or case-insensitive comparison.
/// </para>
/// <para>
/// <see cref="Comparer"/> is <see cref="StringComparer.Ordinal"/>: it compares by raw UTF-16 code units, so
/// it is case-sensitive, culture-independent, and a total order over distinct strings. Declared names in
/// the Vista contract are unique (each generated type is declared once — Requirement 2.5), so the order is a
/// true total order with no ties; the underlying sort is stable regardless.
/// </para>
/// <para>
/// All methods are pure and allocate a fresh result, leaving the input sequence untouched.
/// </para>
/// </remarks>
public static class DeterministicOrder
{
    /// <summary>
    /// The shared ordinal, case-sensitive string comparer used for every name comparison in the emit stage.
    /// Exposed so callers that need a comparer directly (e.g. a sorted dictionary keyed by name) use the
    /// exact same comparison the ordering helpers apply.
    /// </summary>
    public static StringComparer Comparer => StringComparer.Ordinal;

    /// <summary>
    /// Orders a sequence of items by a name selector using the fixed ordinal, case-sensitive comparison,
    /// returning a new list in stable total order (Requirement 9.2).
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items to order; not mutated.</param>
    /// <param name="nameSelector">Projects each item to its declared name, the sole ordering key.</param>
    /// <returns>A new, ordinally-sorted, read-only list of the items.</returns>
    public static IReadOnlyList<T> ByName<T>(IEnumerable<T> items, Func<T, string> nameSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameSelector);

        return items.OrderBy(nameSelector, Comparer).ToArray();
    }

    /// <summary>
    /// Orders a sequence of names using the fixed ordinal, case-sensitive comparison, returning a new list
    /// in stable total order (Requirement 9.2).
    /// </summary>
    /// <param name="names">The names to order; not mutated.</param>
    /// <returns>A new, ordinally-sorted, read-only list of the names.</returns>
    public static IReadOnlyList<string> OrderNames(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names.OrderBy(name => name, Comparer).ToArray();
    }
}
