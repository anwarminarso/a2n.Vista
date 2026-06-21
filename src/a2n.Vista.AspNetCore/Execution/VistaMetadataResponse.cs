using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Metadata;

namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// Serializable projection of a single field's metadata for the <c>GET {route}/metadata</c> response
/// (Decision Log D110). CLR types are emitted as strings because <see cref="System.Type"/> is not
/// directly serializable.
/// </summary>
public sealed record VistaFieldMetadataResponse(
    string Name,
    string Label,
    string ClrType,
    bool IsFilterable,
    bool IsSortable,
    bool IsSearchable,
    bool IsScopable,
    bool IsHidden,
    bool IsPrimaryKey,
    string AllowedOperators);

/// <summary>
/// Serializable projection of a <see cref="ViewMetadata"/> for the Metadata facet (Decision Log D110).
/// </summary>
public sealed record VistaMetadataResponse(
    string Name,
    string Route,
    bool IsReadOnly,
    IReadOnlyList<string> KeyFields,
    int MaxPageSize,
    int MaxExportRows,
    IReadOnlyList<VistaFieldMetadataResponse> Fields)
{
    /// <summary>Builds the response DTO from a <see cref="ViewMetadata"/> snapshot.</summary>
    /// <param name="view">The view metadata to project.</param>
    /// <returns>The serializable metadata response.</returns>
    public static VistaMetadataResponse From(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var fields = view.Fields
            .Where(f => !f.IsHidden)
            .Select(f => new VistaFieldMetadataResponse(
                f.Name,
                f.Label,
                f.ClrType.Name,
                f.IsFilterable,
                f.IsSortable,
                f.IsSearchable,
                f.IsScopable,
                f.IsHidden,
                f.IsPrimaryKey,
                f.AllowedOperators.ToString()))
            .ToList();

        return new VistaMetadataResponse(
            view.Name,
            view.Route,
            view.IsReadOnly,
            view.KeyFields,
            view.Limits.MaxPageSize,
            view.Limits.MaxExportRows,
            fields);
    }
}
