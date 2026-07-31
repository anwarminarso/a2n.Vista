using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using a2n.Vista.Metadata;

namespace a2n.Vista.Export;

/// <summary>
/// Shared helpers for the built-in export writers: the exportable column set (the view's non-hidden
/// fields, in projection order) and reading a row value by field name. The value read reflects over the
/// (often anonymous, Style A) projected row type, so it is the documented <c>[RequiresUnreferencedCode]</c>
/// path (Decision Log D96).
/// </summary>
public static class ExportColumns
{
    /// <summary>An exportable column: the projected field name and its display label.</summary>
    /// <param name="Name">The projected field name (used to read the value).</param>
    /// <param name="Label">The human-friendly header label.</param>
    public readonly record struct Column(string Name, string Label);

    /// <summary>
    /// Returns the exportable columns for <paramref name="view"/>: every non-hidden field, in projection
    /// order, using <see cref="FieldMetadata.Label"/> for the header.
    /// </summary>
    /// <param name="view">The view metadata.</param>
    /// <returns>The ordered exportable columns.</returns>
    public static IReadOnlyList<Column> For(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var columns = new List<Column>(view.Fields.Count);
        foreach (var field in view.Fields)
        {
            if (!field.IsHidden)
            {
                columns.Add(new Column(field.Name, field.Label));
            }
        }

        return columns;
    }

    /// <summary>
    /// Reads the value of the field named <paramref name="fieldName"/> from <paramref name="row"/> in the
    /// context of the view <paramref name="viewName"/>, preferring a generated accessor when one is
    /// registered and falling back to the reflection read otherwise (coexistence, Decision Log D117).
    /// </summary>
    /// <remarks>
    /// When a typed Style B view has a generated accessor registered in
    /// <see cref="ViewAccessorRegistry"/>, the value is read through a compiled cast + property read, so
    /// this branch is AOT-clean and this overload carries no <see cref="RequiresUnreferencedCode"/>
    /// requirement. Only the fallback for views without a generated accessor (e.g. anonymous Style A)
    /// uses reflection; that branch is isolated in <see cref="ValueByReflection"/> so it does not force
    /// the AOT-clean path (or its callers, the export writers) to be trim-unsafe.
    /// </remarks>
    /// <param name="viewName">The name of the view the row belongs to (used to resolve a generated accessor).</param>
    /// <param name="row">The projected row object, or <see langword="null"/>.</param>
    /// <param name="fieldName">The property/field name.</param>
    /// <returns>The value, or <see langword="null"/> when the row or property is absent.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="viewName"/> or <paramref name="fieldName"/> is <see langword="null"/>.
    /// </exception>
    public static object? Value(string viewName, object? row, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(fieldName);

        if (row is null)
        {
            return null;
        }

        // Prefer the generated accessor (cast + property read, no reflection) — AOT-clean hot path.
        if (ViewAccessorRegistry.TryGetAccessor(viewName, fieldName, out var accessor))
        {
            return accessor(row);
        }

        // Coexistence fallback: no generated accessor (e.g. anonymous Style A) → reflection read (RUC).
        return ValueByReflection(row, fieldName);
    }

    /// <summary>Reads the value of the property named <paramref name="name"/> from <paramref name="row"/>.</summary>
    /// <remarks>
    /// The resolved <see cref="PropertyInfo"/> is memoized per <c>(row type, name)</c> pair. This method runs
    /// once per exported <em>cell</em>, so an uncached <see cref="Type.GetProperty(string)"/> here cost a name
    /// lookup per cell — a million lookups for a 100,000-row × 10-column Style A export (audit finding
    /// <c>PERF-02</c>). A negative result is cached too, so a name that does not exist on the row type is
    /// looked up once rather than on every row.
    /// </remarks>
    /// <param name="row">The projected row object, or <see langword="null"/>.</param>
    /// <param name="name">The property/field name.</param>
    /// <returns>The value, or <see langword="null"/> when the row or property is absent.</returns>
    [RequiresUnreferencedCode("Export reads projected row values by reflection over the (possibly anonymous) row type; use the source generator path for AOT.")]
    public static object? Value(object? row, string name)
    {
        if (row is null)
        {
            return null;
        }

        var rowType = row.GetType();
        if (!PropertyCache.TryGetValue(rowType, out var byName))
        {
            byName = PropertyCache.GetValue(rowType, static _ => new ConcurrentDictionary<string, PropertyInfo?>(StringComparer.Ordinal));
        }

        if (!byName.TryGetValue(name, out var property))
        {
            // Deliberately resolved here rather than in a cache factory lambda: a lambda is compiled into a
            // separate method that does not inherit this method's [RequiresUnreferencedCode], so the trim
            // analyser would flag the reflective lookup there.
            property = rowType.GetProperty(name);
            byName[name] = property;
        }

        return property?.GetValue(row);
    }

    // Per-row-type member cache for the reflection fallback. Keyed weakly by Type so a row type from a
    // collectible assembly (or a short-lived test host) is not rooted by the cache; the inner map is
    // concurrent because export requests run in parallel.
    private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, PropertyInfo?>> PropertyCache = new();

    // Isolates the reflection fallback so the AOT-clean Value(viewName, row, fieldName) overload — and the
    // export writers that call it — are not forced to be [RequiresUnreferencedCode]. The suppression is
    // sound because this branch only runs for views without a generated accessor (the documented
    // coexistence fallback); typed Style B views read through the AOT-clean generated accessor instead.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access",
        Justification = "Reflection fallback runs only for views lacking a generated accessor (coexistence, D117); typed Style B views use the AOT-clean generated accessor.")]
    private static object? ValueByReflection(object row, string fieldName) => Value(row, fieldName);
}
