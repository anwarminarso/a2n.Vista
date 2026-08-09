using System;
using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Adapters.AgGrid;

/// <summary>
/// The AG Grid server-side row model adapter (Spec 04 §7, D133). Translates an AG Grid
/// <c>IServerSideGetRowsRequest</c> (posted as JSON, with the quick-filter text supplied out-of-band via
/// <c>AdapterRequest.Values["q"]</c>) into the neutral <see cref="ViewQueryRequest"/> — populating the
/// <c>Filter</c> channel from <c>filterModel</c> and the <c>Search</c> channel from the quick filter
/// (D111/D134) — and the neutral result back into an <see cref="AgGridRowsResponse"/> (<c>rowData</c>/
/// <c>rowCount</c>, D135). The three steps are pure; the adapter references <c>a2n.Vista.Core</c> only
/// (D48/D66).
/// </summary>
public sealed class AgGridAdapter : ViewAdapter<AgGridRowsRequest, AgGridRowsResponse>
{
    /// <summary>The documented key under which the quick-filter text is supplied out-of-band (R2.5).</summary>
    public const string QuickFilterKey = "q";

    /// <summary>The maximum accepted quick-filter length; a longer value is a bind failure (R2.5).</summary>
    private const int MaxQuickFilterLength = 1024;

    /// <inheritdoc />
    public override string Id => "aggrid";

    /// <inheritdoc />
    public override string? RouteSuffix => "aggrid";

    /// <inheritdoc />
    public override AgGridRowsRequest BindRequest(AdapterRequest raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        // Require a non-blank JSON body — the AG Grid request rides in AdapterRequest.JsonBody (R2.3, RC5).
        if (string.IsNullOrWhiteSpace(raw.JsonBody))
        {
            throw new AdapterBindException(
                "The AG Grid request body is absent, empty, or whitespace-only; a JSON IServerSideGetRowsRequest is required.");
        }

        // Deserialize via the source-generated context only (no reflection-based Deserialize, R2.2/D96);
        // wrap any syntactic/type failure as an AdapterBindException (R2.4).
        AgGridRowsRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(raw.JsonBody, AgGridJsonContext.Default.AgGridRowsRequest);
        }
        catch (JsonException ex)
        {
            throw new AdapterBindException("The AG Grid request body is not valid JSON.", ex);
        }

        if (request is null)
        {
            throw new AdapterBindException("The AG Grid request body deserialized to null.");
        }

        // Validate the bound row range: half-open [StartRow, EndRow) with non-negative bounds (R2.4).
        if (request.StartRow < 0 || request.EndRow < request.StartRow)
        {
            throw new AdapterBindException(
                $"The AG Grid row range is invalid: startRow={request.StartRow}, endRow={request.EndRow} " +
                "(require startRow >= 0 and endRow >= startRow).");
        }

        // Normalize absent collections to empty — never null, never a partial POCO (R2.1, R1.6).
        request.SortModel ??= new List<AgGridSortModel>();
        request.FilterModel ??= new Dictionary<string, JsonElement>();

        // Advanced Filter (nested join/column tree) is deferred for v1 (D134): reject loudly (R4.7). The
        // per-column parser also enforces this at ToQuery time; a cheap top-level scan fails fast here.
        foreach (var (colId, descriptor) in request.FilterModel)
        {
            if (IsAdvancedFilter(descriptor))
            {
                throw new AdapterBindException(
                    $"AG Grid Advanced Filter is not supported (column '{colId}'); it is deferred for v1 (D134).");
            }
        }

        // Read the out-of-band quick-filter text from Values under the documented key (R2.5); cap its length.
        var quickFilter = GetString(raw.Values, QuickFilterKey);
        if (quickFilter is { Length: > MaxQuickFilterLength })
        {
            throw new AdapterBindException(
                $"The AG Grid quick filter exceeds the maximum length of {MaxQuickFilterLength} characters.");
        }

        request.QuickFilter = quickFilter ?? string.Empty;
        return request;
    }

    /// <inheritdoc />
    public override ViewQueryRequest ToQuery(AgGridRowsRequest request, ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(view);

        var fields = ViewFieldLookup.For(view);

        // Block paging (D135, revised by D144): PageSize = EndRow - StartRow, and StartRow is carried
        // verbatim as the absolute Offset instead of being divided into a page index — a block boundary is
        // not required to be page-aligned, and the engine's page-size clamp must not shift the window. A
        // non-positive PageSize is passed through UNCHANGED — no clamp, default, or substitution — so the
        // engine rejects it (R3.1, R3.2).
        var pageSize = request.EndRow - request.StartRow;
        var offset = request.StartRow;

        // Sort (R3.3–R3.5): map EVERY sortModel entry to a SortSpec, preserving the array order; the colId
        // travels verbatim and the engine validates it; any non-"desc" Sort → ascending.
        var sort = BuildSort(request);

        // Filter channel (R4.4, R4.8): the parsed AND-of-columns tree, or null when nothing maps.
        var filter = AgGridFilterModelParser.Parse(request.FilterModel, fields);

        // Search channel (R4.5, R4.9): a FilterOr of Contains leaves over the view's searchable string
        // fields, or null when the quick filter is empty/whitespace or no searchable string field exists.
        var search = BuildQuickFilterSearch(request, view);

        return new ViewQueryRequest(
            Filter: filter,
            Sort: sort,
            Page: 0,
            PageSize: pageSize,
            SelectFields: null,
            Search: search,
            Scope: null,
            Offset: offset);
    }

    /// <inheritdoc />
    public override AgGridRowsResponse ToResponse(AdapterListResult result, AgGridRowsRequest request, ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Map the neutral List result into AG Grid's LoadSuccessParams shape (D135, R5.1/R5.2):
        //   rowData  = the engine-projected page of rows (empty rows → empty array, R5.5)
        //   rowCount = RecordsFiltered, the filtered total used for AG Grid last-block detection
        // RecordsTotal is intentionally NOT surfaced — the server-side row model has no slot for it (R5.1).
        return new AgGridRowsResponse
        {
            RowData = result.Rows,
            RowCount = result.RecordsFiltered,
        };
    }

    /// <summary>
    /// Maps <b>every</b> <c>sortModel</c> entry to a <see cref="SortSpec"/>, carrying the <c>colId</c>
    /// through verbatim and preserving the array order (multi-sort priority). Any <c>sort</c> value other
    /// than <c>"desc"</c> yields ascending order (R3.3–R3.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No entry is ever dropped (D150).</b> The adapter <em>builds</em>; the engine <em>rejects</em>
    /// (Spec 04 §6 invariant 2, D67). An unknown or mis-cased <c>colId</c> therefore reaches the engine and
    /// is refused with the same <c>400</c> <c>filter-unknown-field</c> the <c>filterModel</c> channel already
    /// produces for the identical mistake — <c>colId</c> is matched against the view's field names
    /// <b>ordinally</b> (<see cref="ViewFieldLookup"/>), so a merely differently cased spelling is unknown.
    /// </para>
    /// <para>
    /// <b>Why the "skip non-field UI column" exception does not apply here.</b> That sanctioned exception
    /// (Spec 04 §6 invariant 5) exists for a transport that <em>declares</em> its columns: DataTables sends
    /// <c>columns[i][data]</c>/<c>[orderable]</c>, so an action column is self-describing and skipping it
    /// drops nothing the client asked for. An AG Grid <c>sortModel</c> declares nothing — it is a list of
    /// keys the client is asking the <em>server</em> to order by. Matching them against the projection here
    /// would be the adapter enforcing the whitelist, and it made a typo indistinguishable from a UI column:
    /// the request returned <c>200</c> with an untouched row order (issue #2). A genuinely non-field column
    /// belongs out of <c>sortModel</c> in the first place — mark it <c>sortable: false</c> in its
    /// <c>colDef</c>.
    /// </para>
    /// </remarks>
    private static List<SortSpec> BuildSort(AgGridRowsRequest request)
    {
        var sort = new List<SortSpec>(request.SortModel.Count);
        foreach (var entry in request.SortModel)
        {
            var descending = string.Equals(entry.Sort, "desc", StringComparison.OrdinalIgnoreCase);

            // A JSON `"colId": null` defeats the non-nullable declaration, so normalize it here rather than
            // handing a null field name to the engine. It is still not dropped: "" is an unknown field → 400.
            sort.Add(new SortSpec(entry.ColId ?? string.Empty, descending));
        }

        return sort;
    }

    /// <summary>
    /// Builds the quick-filter (global search) sub-tree: a <see cref="FilterOr"/> of <c>Contains</c> leaves,
    /// one per <c>IsSearchable &amp;&amp; string</c> view field, each using the quick-filter text. Returns
    /// <see langword="null"/> when the text is empty/whitespace or no searchable string field exists. Mirrors
    /// the DataTables adapter's <c>BuildGlobalSearch</c> exactly (R4.5, R4.9).
    /// </summary>
    private static FilterNode? BuildQuickFilterSearch(AgGridRowsRequest request, ViewMetadata view)
    {
        var value = request.QuickFilter;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var leaves = new List<FilterNode>();
        foreach (var field in view.Fields)
        {
            if (field.IsSearchable && field.ClrType == typeof(string))
            {
                leaves.Add(new FilterLeaf(field.Name, FilterOperator.Contains, value));
            }
        }

        return leaves.Count switch
        {
            0 => null,
            1 => leaves[0],
            _ => new FilterOr(leaves),
        };
    }

    /// <summary>
    /// Detects an AG Grid Advanced-Filter descriptor shape: a join node (<c>filterType == "join"</c> or
    /// <c>type == "join"</c>) or an explicit <c>filterType == "advanced"</c> marker. Mirrors the parser's
    /// detection so the failure surfaces at bind time (R4.7, D134).
    /// </summary>
    private static bool IsAdvancedFilter(JsonElement descriptor)
    {
        if (descriptor.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var filterType = GetString(descriptor, "filterType");
        if (string.Equals(filterType, "advanced", StringComparison.OrdinalIgnoreCase)
            || string.Equals(filterType, "join", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var type = GetString(descriptor, "type");
        return string.Equals(type, "join", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a string property, or <see langword="null"/> when absent or non-string.</summary>
    private static string? GetString(JsonElement element, string prop) =>
        element.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads the first value for a key from the neutral values bag, or <see langword="null"/> when absent.</summary>
    private static string? GetString(IReadOnlyDictionary<string, IReadOnlyList<string>> values, string key) =>
        values.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;
}
