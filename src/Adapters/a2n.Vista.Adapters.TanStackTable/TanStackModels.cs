using System;
using System.Collections.Generic;
using System.Text.Json;

namespace a2n.Vista.Adapters.TanStackTable;

/// <summary>
/// A TanStack Table server request (the result of <see cref="TanStackTableAdapter.BindRequest"/>).
/// Mirrors the table-state slices a manual table posts: <c>pagination</c>, <c>sorting</c>,
/// <c>columnFilters</c>, <c>globalFilter</c>, and <c>columnVisibility</c>. Those slices are the same
/// in TanStack Table v8 and v9. Grouping, expanding, and row selection are intentionally absent.
/// </summary>
public sealed class TanStackTableRequest
{
    /// <summary>Page index and page size. Omitted on the wire → <c>{ pageIndex: 0, pageSize: 10 }</c>.</summary>
    public TanStackPaginationState Pagination { get; set; } = new();

    /// <summary>Sort keys in priority order (<c>{ id, desc }</c>).</summary>
    public List<TanStackColumnSort> Sorting { get; set; } = new();

    /// <summary>Per-column filters in array order. Each <c>value</c> stays raw until the filter parser.</summary>
    public List<TanStackColumnFilter> ColumnFilters { get; set; } = new();

    /// <summary>Global filter text (TanStack <c>state.globalFilter</c>). Empty when absent.</summary>
    public string GlobalFilter { get; set; } = string.Empty;

    /// <summary>
    /// Column visibility (<c>columnId → visible</c>). <see langword="null"/> or empty means every column
    /// is visible. A <see langword="false"/> entry hides that column. Keys the view does not declare are
    /// presentation-only columns and are ignored.
    /// </summary>
    public Dictionary<string, bool>? ColumnVisibility { get; set; }
}

/// <summary>TanStack <c>PaginationState</c>: <c>{ pageIndex, pageSize }</c>.</summary>
public sealed class TanStackPaginationState
{
    /// <summary>Zero-based page index.</summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Rows per page. The TanStack default is 10 when the property is omitted. A non-positive value is
    /// passed through so the engine rejects "all rows" rather than the adapter inventing a size.
    /// </summary>
    public int PageSize { get; set; } = 10;
}

/// <summary>TanStack <c>ColumnSort</c>: <c>{ id, desc }</c>.</summary>
public sealed class TanStackColumnSort
{
    /// <summary>The column id. Matched ordinally against the view field name.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><see langword="true"/> for descending. Omitted or <see langword="false"/> is ascending.</summary>
    public bool Desc { get; set; }
}

/// <summary>TanStack <c>ColumnFilter</c>: <c>{ id, value }</c>. <see cref="Value"/> is polymorphic.</summary>
public sealed class TanStackColumnFilter
{
    /// <summary>The column id. Matched ordinally against the view field name.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The filter value. Shape is interpreted by <c>TanStackColumnFilterParser</c>.</summary>
    public JsonElement Value { get; set; }
}

/// <summary>
/// The TanStack manual-pagination response: <c>{ rows, rowCount, pageCount }</c>.
/// <see cref="RowCount"/> is the filtered total (TanStack <c>rowCount</c>). <see cref="PageCount"/> is
/// <c>ceil(rowCount / pageSize)</c>, which TanStack derives the same way when <c>pageCount</c> is omitted.
/// </summary>
public sealed class TanStackTableResponse
{
    /// <summary>The page of rows (empty when nothing matches). Pass this to the table as <c>data</c>.</summary>
    public IReadOnlyList<object?> Rows { get; set; } = Array.Empty<object?>();

    /// <summary>The filtered total row count.</summary>
    public long RowCount { get; set; }

    /// <summary>The number of pages at the requested page size, or 0 when the page size is not positive.</summary>
    public long PageCount { get; set; }
}
