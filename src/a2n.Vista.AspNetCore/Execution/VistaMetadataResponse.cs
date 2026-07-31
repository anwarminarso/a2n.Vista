using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
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
    string AllowedOperators)
{
    /// <summary>
    /// The author's display-format hint for the field, or <see langword="null"/> when none was set
    /// (Decision Log D149). Published for the client to apply when rendering; the server never interprets
    /// it, so filtering, sorting, and export are unaffected.
    /// </summary>
    /// <remarks>
    /// Omitted from the payload when unset rather than emitted as <c>null</c>. Most fields carry no hint, so
    /// writing it always would add a member per field to an endpoint whose whole point is to be cached, and
    /// absent already means "no hint" to a client. The emitted OpenAPI schema marks it optional to match.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; init; }
}

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
    private static readonly ConditionalWeakTable<ViewMetadata, VistaMetadataResponse> Cache = new();

    /// <summary>Builds the response DTO from a <see cref="ViewMetadata"/> snapshot.</summary>
    /// <remarks>
    /// The projection is memoized per metadata instance and the returned response is shared. View metadata
    /// is immutable once registered, so rebuilding this DTO (and its per-field DTO) on every
    /// <c>GET {route}/metadata</c> request was pure waste (audit finding <c>PERF-07</c>). The cache is a
    /// <see cref="ConditionalWeakTable{TKey, TValue}"/>: keyed by reference (record equality over
    /// <see cref="ViewMetadata"/> is not a dependable cache key) and collected with the metadata it belongs
    /// to, so a disposed host's views leak nothing. Callers must treat the result as read-only — it is.
    /// </remarks>
    /// <param name="view">The view metadata to project.</param>
    /// <returns>The serializable metadata response.</returns>
    public static VistaMetadataResponse From(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return Cache.GetValue(view, static metadata => Project(metadata));
    }

    private static VistaMetadataResponse Project(ViewMetadata view)
    {
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
                f.AllowedOperators.ToString())
            {
                Format = f.Format,
            })
            .ToList();

        return new VistaMetadataResponse(
            view.Name,
            view.Route,
            view.IsReadOnly,
            view.KeyFields,
            view.Limits.MaxPageSize,
            view.Limits.MaxExportRows,
            // Wrapped because the response instance is shared across requests: a plain List<T> handed out as
            // IReadOnlyList<T> stays mutable through a downcast.
            new ReadOnlyCollection<VistaFieldMetadataResponse>(fields));
    }
}
