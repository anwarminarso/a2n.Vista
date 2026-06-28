using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Contracts;

namespace a2n.Vista.AspNetCore.Execution;

/// <summary>A single sort instruction in a request body (<c>{ "field": "Name", "desc": true }</c>).</summary>
public sealed class VistaSortBody
{
    /// <summary>The field to sort by.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Whether to sort descending. Defaults to ascending.</summary>
    public bool Desc { get; set; }
}

/// <summary>
/// The request body for the List and Export facets (Decision Log D110): the neutral query
/// (filter/search/sort/paging). Deserialized with <see cref="Serialization.VistaJson.Options"/>.
/// </summary>
public sealed class VistaListRequestBody
{
    /// <summary>The structured filter tree, or <see langword="null"/>.</summary>
    public FilterNode? Filter { get; set; }

    /// <summary>Optional global search text (matched against the view's searchable string fields).</summary>
    public string? Search { get; set; }

    /// <summary>
    /// Optional contextual/lookup scoping sub-tree (DynData <c>externalFilter</c> equivalent), validated
    /// under the <c>Scope</c> whitelist (Decision Log D111). <see langword="null"/> when absent.
    /// </summary>
    public FilterNode? Scope { get; set; }

    /// <summary>Ordering instructions, applied in order.</summary>
    public List<VistaSortBody>? Sort { get; set; }

    /// <summary>Zero-based page index.</summary>
    public int Page { get; set; }

    /// <summary>Requested page size (clamped to the view's hard limit by the executor).</summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Optional export format id (for example <c>"csv"</c>/<c>"xlsx"</c>) used only by the Export facet
    /// (Decision Log D115); ignored by List. When <see langword="null"/>/empty, Export returns the JSON
    /// <c>ViewListResult</c> (backward compatible).
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Maps the body to the neutral <see cref="ViewQueryRequest"/> (without the search merge).</summary>
    public ViewQueryRequest ToBaseRequest()
    {
        var sort = new List<SortSpec>(Sort?.Count ?? 0);
        if (Sort is not null)
        {
            foreach (var spec in Sort)
            {
                sort.Add(new SortSpec(spec.Field, spec.Desc));
            }
        }

        return new ViewQueryRequest(Filter, sort, Page, PageSize, SelectFields: null, Search: null, Scope: Scope);
    }
}

/// <summary>The request body for the Detail facet: the row key (scalar or name→value object).</summary>
public sealed class VistaDetailRequestBody
{
    /// <summary>The key element (a scalar for a single key, or an object for a composite key).</summary>
    public JsonElement Key { get; set; }
}

/// <summary>The request body for the Create facet: the typed write payload.</summary>
public sealed class VistaCreateRequestBody
{
    /// <summary>The write model fields.</summary>
    public JsonElement Data { get; set; }
}

/// <summary>The request body for the Update facet: the key, the write payload, and an optional token.</summary>
public sealed class VistaUpdateRequestBody
{
    /// <summary>The key of the row to update.</summary>
    public JsonElement Key { get; set; }

    /// <summary>The write model fields.</summary>
    public JsonElement Data { get; set; }

    /// <summary>Optional optimistic-concurrency token.</summary>
    public string? ConcurrencyToken { get; set; }
}

/// <summary>The request body for the Delete facet: the key and an optional token.</summary>
public sealed class VistaDeleteRequestBody
{
    /// <summary>The key of the row to delete.</summary>
    public JsonElement Key { get; set; }

    /// <summary>Optional optimistic-concurrency token.</summary>
    public string? ConcurrencyToken { get; set; }
}
