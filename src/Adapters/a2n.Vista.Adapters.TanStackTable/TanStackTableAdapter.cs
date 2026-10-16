using System;
using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Adapters.TanStackTable;

/// <summary>
/// The TanStack Table adapter (Spec 04, D154–D157). Translates a manual table's state — pagination,
/// sorting, column filters, global filter, and column visibility — into the neutral
/// <see cref="ViewQueryRequest"/> and the neutral result back into <c>{ rows, rowCount, pageCount }</c>.
/// The three steps are pure; the adapter references <c>a2n.Vista.Core</c> only (D48/D66).
/// </summary>
public sealed class TanStackTableAdapter : ViewAdapter<TanStackTableRequest, TanStackTableResponse>
{
    /// <summary>The maximum accepted global-filter length; a longer value is a bind failure.</summary>
    private const int MaxGlobalFilterLength = 1024;

    /// <inheritdoc />
    public override string Id => "tanstack";

    /// <inheritdoc />
    public override string? RouteSuffix => "tanstack";

    /// <inheritdoc />
    public override TanStackTableRequest BindRequest(AdapterRequest raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (string.IsNullOrWhiteSpace(raw.JsonBody))
        {
            throw new AdapterBindException(
                "The TanStack Table request body is absent, empty, or whitespace-only; a JSON table-state object is required.");
        }

        TanStackTableRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(raw.JsonBody, TanStackJsonContext.Default.TanStackTableRequest);
        }
        catch (JsonException ex)
        {
            throw new AdapterBindException("The TanStack Table request body is not valid JSON.", ex);
        }

        if (request is null)
        {
            throw new AdapterBindException("The TanStack Table request body deserialized to null.");
        }

        // Omitted pagination is TanStack's initial state. An explicit null is the same omission.
        request.Pagination ??= new TanStackPaginationState();
        request.Sorting ??= new List<TanStackColumnSort>();
        request.ColumnFilters ??= new List<TanStackColumnFilter>();

        // A negative page index is a malformed offset (the same posture as DataTables start < 0).
        // A non-positive page size is left for ToQuery to pass through, so the engine rejects it.
        if (request.Pagination.PageIndex < 0)
        {
            throw new AdapterBindException(
                $"The TanStack page index is invalid: pageIndex={request.Pagination.PageIndex} (require pageIndex >= 0).");
        }

        if (request.GlobalFilter is { Length: > MaxGlobalFilterLength })
        {
            throw new AdapterBindException(
                $"The TanStack global filter exceeds the maximum length of {MaxGlobalFilterLength} characters.");
        }

        request.GlobalFilter ??= string.Empty;

        foreach (var filter in request.ColumnFilters)
        {
            if (filter.Value.ValueKind == JsonValueKind.Undefined)
            {
                throw new AdapterBindException(
                    $"The TanStack column filter on '{filter.Id}' is missing its 'value'.");
            }
        }

        return request;
    }

    /// <inheritdoc />
    public override ViewQueryRequest ToQuery(TanStackTableRequest request, ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(view);

        var pagination = request.Pagination ?? new TanStackPaginationState();
        var pageSize = pagination.PageSize;

        // D144: TanStack is page-based, so the absolute offset is pageIndex * pageSize. Carrying that
        // product (instead of leaving Page = pageIndex) keeps a later page-size clamp from moving the
        // window. A non-positive page size is passed through unchanged — no clamp, default, or
        // substitution — so the engine rejects it.
        long offsetLong;
        try
        {
            offsetLong = checked((long)pagination.PageIndex * pageSize);
        }
        catch (OverflowException ex)
        {
            throw new AdapterBindException(
                $"The TanStack page window is out of range: pageIndex={pagination.PageIndex}, pageSize={pageSize}.",
                ex);
        }

        if (offsetLong is > int.MaxValue or < int.MinValue)
        {
            throw new AdapterBindException(
                $"The TanStack page window is out of range: pageIndex={pagination.PageIndex}, pageSize={pageSize}.");
        }

        return new ViewQueryRequest(
            Filter: TanStackColumnFilterParser.Parse(request.ColumnFilters ?? new List<TanStackColumnFilter>()),
            Sort: BuildSort(request),
            Page: 0,
            PageSize: pageSize,
            SelectFields: BuildSelectFields(request, view),
            Search: BuildGlobalSearch(request, view),
            Scope: null,
            Offset: (int)offsetLong);
    }

    /// <inheritdoc />
    public override TanStackTableResponse ToResponse(
        AdapterListResult result,
        TanStackTableRequest request,
        ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);

        var pageSize = request.Pagination?.PageSize ?? 0;
        return new TanStackTableResponse
        {
            Rows = result.Rows,
            RowCount = result.RecordsFiltered,
            PageCount = PageCount(result.RecordsFiltered, pageSize),
        };
    }

    /// <summary>
    /// <c>ceil(rowCount / pageSize)</c> using integer arithmetic. A non-positive page size yields 0
    /// (there is no page to count when the engine is going to reject the size).
    /// </summary>
    internal static long PageCount(long rowCount, int pageSize)
    {
        if (pageSize <= 0 || rowCount <= 0)
        {
            return 0;
        }

        var size = (long)pageSize;
        return (rowCount / size) + (rowCount % size == 0 ? 0 : 1);
    }

    /// <summary>
    /// Maps <b>every</b> <c>sorting</c> entry to a <see cref="SortSpec"/>, carrying <c>id</c> through
    /// verbatim and preserving array order (D156, D150). The adapter builds; the engine rejects an
    /// unknown or non-sortable id. A TanStack sort entry declares nothing about whether the column is a
    /// view field — the same posture as an AG Grid <c>sortModel</c> — so no entry is dropped.
    /// </summary>
    private static List<SortSpec> BuildSort(TanStackTableRequest request)
    {
        var sorting = request.Sorting ?? new List<TanStackColumnSort>();
        var sort = new List<SortSpec>(sorting.Count);
        foreach (var entry in sorting)
        {
            // A JSON `"id": null` defeats the non-nullable declaration. "" is still not dropped: the
            // engine rejects it as an unknown field.
            sort.Add(new SortSpec(entry.Id ?? string.Empty, entry.Desc));
        }

        return sort;
    }

    /// <summary>
    /// Builds the global-filter sub-tree: a <see cref="FilterOr"/> of <c>Contains</c> leaves, one per
    /// <c>IsSearchable &amp;&amp; string</c> view field. Returns <see langword="null"/> when the text is
    /// empty or no searchable string field exists. Mirrors the AG Grid quick filter and DataTables
    /// <c>BuildGlobalSearch</c>.
    /// </summary>
    private static FilterNode? BuildGlobalSearch(TanStackTableRequest request, ViewMetadata view)
    {
        var value = request.GlobalFilter;
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
    /// Maps <c>columnVisibility</c> onto <see cref="ViewQueryRequest.SelectFields"/> (D156).
    /// <see langword="null"/> or a map that hides nothing → <see langword="null"/> (the full projection).
    /// A <see langword="false"/> entry whose key equals a view field name (ordinal) omits that field.
    /// Keys the view does not declare are UI columns the request itself describes as hidden — they are
    /// skipped, the same way DataTables skips an action column the column table declares. A mis-cased
    /// field name therefore does <b>not</b> hide the field and does <b>not</b> 400: visibility is not a
    /// request to order or filter, and the column simply stays in the projection.
    /// </summary>
    private static IReadOnlyList<string>? BuildSelectFields(TanStackTableRequest request, ViewMetadata view)
    {
        var visibility = request.ColumnVisibility;
        if (visibility is null || visibility.Count == 0)
        {
            return null;
        }

        var anyHidden = false;
        var visible = new List<string>(view.Fields.Count);
        foreach (var field in view.Fields)
        {
            if (visibility.TryGetValue(field.Name, out var shown) && !shown)
            {
                anyHidden = true;
                continue;
            }

            visible.Add(field.Name);
        }

        return anyHidden ? visible : null;
    }
}
