using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace a2n.Vista.Metadata;

/// <summary>
/// The single, memoized name → <see cref="FieldMetadata"/> lookup for a <see cref="ViewMetadata"/>.
/// Field metadata is immutable once a view is registered, so the lookup is built at most once per
/// metadata instance and shared by every consumer that needs to resolve a client-supplied field name
/// (the filter compiler and the grid adapters).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why memoize (audit finding <c>PERF-05</c>).</b> Each consumer used to rebuild an identical
/// dictionary per call. A single List request compiles up to three filter channels (scope, filter,
/// search) and a grid adapter binds a fourth, so a hot read path paid four dictionary builds over data
/// that cannot change after registration.
/// </para>
/// <para>
/// <b>Cache keying.</b> The cache is a <see cref="ConditionalWeakTable{TKey, TValue}"/> keyed by
/// <em>reference</em>, not by value: <see cref="ViewMetadata"/> is a record, and record equality over it
/// is not a reliable cache key (see audit finding <c>BUG-10</c>). Reference keying also means a
/// <c>with</c>-derived clone simply gets its own entry, and the entry is collected with the metadata it
/// belongs to — a short-lived metadata instance (a test fixture, a disposed host) leaks nothing.
/// </para>
/// <para>
/// <b>Name matching is ordinal.</b> Field names are matched case-sensitively, so a client cannot reach a
/// field by a differently cased spelling. Duplicate names resolve last-wins, matching the behaviour of
/// the per-call builders this replaced.
/// </para>
/// </remarks>
public static class ViewFieldLookup
{
    private static readonly ConditionalWeakTable<ViewMetadata, FrozenDictionary<string, FieldMetadata>> Cache = new();

    /// <summary>
    /// Returns the ordinal name → field lookup for <paramref name="view"/>, building it on first use and
    /// serving the same instance for every later call with the same metadata instance.
    /// </summary>
    /// <param name="view">The view metadata whose fields are indexed.</param>
    /// <returns>An immutable lookup keyed by <see cref="FieldMetadata.Name"/> (ordinal).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    public static IReadOnlyDictionary<string, FieldMetadata> For(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return Cache.GetValue(view, static metadata => Build(metadata.Fields));
    }

    private static FrozenDictionary<string, FieldMetadata> Build(IReadOnlyList<FieldMetadata> fields)
    {
        // Staged through a mutable dictionary so a duplicate name resolves last-wins instead of throwing,
        // preserving the behaviour of the per-call builders this replaced.
        var staged = new Dictionary<string, FieldMetadata>(fields.Count, StringComparer.Ordinal);
        foreach (var field in fields)
        {
            staged[field.Name] = field;
        }

        // Frozen: the lookup is shared across requests, so it must not be mutable through a downcast.
        return staged.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
