using System;
using System.Collections.Generic;
using System.Text.Json;

namespace a2n.Vista.Adapters.AgGrid;

/// <summary>
/// The AG Grid server-side row model request (the result of <see cref="AgGridAdapter.BindRequest"/>).
/// Mirrors the AG Grid <c>IServerSideGetRowsRequest</c> wire shape (the fields Vista maps) plus the
/// out-of-band quick-filter text bound from <c>AdapterRequest.Values</c>. Grouping, aggregation, and
/// pivot fields are intentionally absent (out of scope).
/// </summary>
public sealed class AgGridRowsRequest
{
    /// <summary>Zero-based first row of the requested block (inclusive).</summary>
    public int StartRow { get; set; }

    /// <summary>Row past the requested block (exclusive); the block is the half-open range [StartRow, EndRow).</summary>
    public int EndRow { get; set; }

    /// <summary>Sort keys in priority order.</summary>
    public List<AgGridSortModel> SortModel { get; set; } = new();

    /// <summary>Column filters keyed by colId; each value is a raw filter descriptor (text/number/date/set/combined).</summary>
    public Dictionary<string, JsonElement> FilterModel { get; set; } = new();

    /// <summary>The global quick-filter text (bound out-of-band from <c>AdapterRequest.Values</c>; empty when absent).</summary>
    public string QuickFilter { get; set; } = string.Empty;
}

/// <summary>An AG Grid sort key: <c>{ colId, sort: "asc" | "desc" }</c>.</summary>
public sealed class AgGridSortModel
{
    /// <summary>The column identifier being sorted.</summary>
    public string ColId { get; set; } = string.Empty;

    /// <summary>The sort direction (<c>"asc"</c>/<c>"desc"</c>); any value other than <c>"desc"</c> is treated as ascending.</summary>
    public string Sort { get; set; } = "asc";
}

/// <summary>
/// The AG Grid server-side row model response (<c>LoadSuccessParams</c>): <c>{ rowData, rowCount }</c>.
/// <see cref="RowCount"/> is the filtered total, used by AG Grid for last-block detection (D135).
/// </summary>
public sealed class AgGridRowsResponse
{
    /// <summary>The page of rows for the requested block (empty when nothing matches).</summary>
    public IReadOnlyList<object?> RowData { get; set; } = Array.Empty<object?>();

    /// <summary>The filtered total row count, for AG Grid last-block detection (D135).</summary>
    public long RowCount { get; set; }
}
