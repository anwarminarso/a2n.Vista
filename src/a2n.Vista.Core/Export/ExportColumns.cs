using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>Reads the value of the property named <paramref name="name"/> from <paramref name="row"/>.</summary>
    /// <param name="row">The projected row object, or <see langword="null"/>.</param>
    /// <param name="name">The property/field name.</param>
    /// <returns>The value, or <see langword="null"/> when the row or property is absent.</returns>
    [RequiresUnreferencedCode("Export reads projected row values by reflection over the (possibly anonymous) row type; use the source generator path for AOT.")]
    public static object? Value(object? row, string name) =>
        row?.GetType().GetProperty(name)?.GetValue(row);
}
