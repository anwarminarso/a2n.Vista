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

/// <summary>
/// The single request envelope for every write facet (Create/Update/Delete) in the write path
/// (Decision Log D120). The typed write model rides in <see cref="Model"/>; the row key (update/delete)
/// rides in <see cref="Key"/>. Optimistic concurrency is carried out-of-band in the HTTP
/// <c>If-Match</c>/<c>ETag</c> headers rather than in the body, so no token member exists here
/// (Requirement R6.1–R6.4). Deserialized with <see cref="Serialization.VistaJson.Options"/>; the raw
/// <see cref="System.Text.Json.JsonElement"/> members are bound to the view's runtime types by
/// <see cref="VistaWriteBinding"/> so no System.Text.Json type crosses into Core.
/// </summary>
/// <remarks>
/// This supersedes the earlier per-facet <c>VistaCreate/Update/DeleteRequestBody</c> placeholders (which
/// carried an in-body concurrency token and were never wired). Bulk (an array body) is rejected by the
/// binder with <see cref="a2n.Vista.Write.VistaBulkNotEnabledException"/> (Requirement R15.1); a bulk
/// execution path is out of scope for this milestone.
/// </remarks>
public sealed class VistaWriteRequestBody
{
    /// <summary>
    /// The typed write payload (the <c>TCrud</c> model). Bound to the view's <c>CrudType</c> by
    /// <see cref="VistaWriteBinding.BindModel"/>. Required for Create/Update; a missing or non-object
    /// value is rejected as a malformed body (Requirement R9.1).
    /// </summary>
    public JsonElement? Model { get; init; }

    /// <summary>
    /// The row key for Update/Delete: a scalar for a single key, or a field-name→value object for a
    /// composite key. Read into the Core-neutral key shape by <see cref="VistaKeyReader"/>; a missing
    /// key on Update/Delete is rejected as a missing-key error (Requirements R2.8, R5.5, R9.2).
    /// </summary>
    public JsonElement? Key { get; init; }
}
