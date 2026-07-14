using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace a2n.Vista.Examples.AgGridNorthwind;

/// <summary>
/// End-to-end verification harness for the AG Grid adapter over the <c>vProductCategory</c> view
/// (Requirements R8.2, R8.6). It drives an AG Grid <c>IServerSideGetRowsRequest</c> through the exact same
/// path the <c>POST {route}/aggrid</c> endpoint uses — <see cref="AgGridAdapter.BindRequest"/> →
/// <see cref="AgGridAdapter.ToQuery"/> → the real Core <see cref="IViewExecutor"/> (mirroring
/// <c>ViewRequestExecutor.ListForAdapterAsync</c>: it closes the generic List method over the view's
/// runtime row type via reflection and rebuilds an <see cref="AdapterListResult"/> from the paged result)
/// → <see cref="AgGridAdapter.ToResponse"/> — and asserts:
/// <list type="bullet">
///   <item><description>
///     The bound request carries <c>startRow</c>/<c>endRow</c> block paging, two <c>sortModel</c> keys, a
///     combined two-condition <c>filterModel</c>, and a quick filter (R8.2 inputs).
///   </description></item>
///   <item><description>
///     The response is the AG Grid <c>{ rowData, rowCount }</c> shape where <c>rowCount</c> equals the
///     total matching rows before paging and <c>rowData</c> is the exact rows within
///     <c>[startRow, endRow)</c> in the requested sort order (R8.2).
///   </description></item>
///   <item><description>
///     The response serializes through the endpoint's serializer to a body carrying camelCase
///     <c>rowData</c> and <c>rowCount</c> (R8.6).
///   </description></item>
/// </list>
/// Run it with <c>dotnet run -- selftest</c>.
/// </summary>
public static class AgGridSelfTest
{
    private const string ViewName = "vProductCategory";

    /// <summary>The out-of-band quick-filter text driven through <c>AdapterRequest.Values["q"]</c> (R2.5).</summary>
    private const string QuickFilter = "e";

    /// <summary>
    /// Shared <c>sortModel</c> (two keys, priority order) and <c>filterModel</c> (a combined two-condition
    /// number filter) used by every block request. Only <c>startRow</c>/<c>endRow</c> vary between blocks,
    /// so a slice of one block is directly comparable to another.
    /// </summary>
    private const string SortModelJson =
        """[{"colId":"CategoryName","sort":"asc"},{"colId":"ProductName","sort":"asc"}]""";

    // Combined AND filter on the real numeric field UnitPrice: 20 <= UnitPrice < 100. Two conditions,
    // operator AND (R8.2 "combined 2-condition filterModel").
    private const string FilterModelJson =
        "{\"UnitPrice\":{\"filterType\":\"number\",\"operator\":\"AND\",\"conditions\":[" +
        "{\"filterType\":\"number\",\"type\":\"greaterThanOrEqual\",\"filter\":20}," +
        "{\"filterType\":\"number\",\"type\":\"lessThan\",\"filter\":100}]}}";

    private static readonly MethodInfo ListAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.ListAsync))!;

    /// <summary>
    /// Runs the AG Grid self-test against a built application's services.
    /// </summary>
    /// <param name="services">The root service provider (a scope is created internally).</param>
    /// <returns><see langword="true"/> when every check passed; otherwise <see langword="false"/>.</returns>
    [RequiresUnreferencedCode("Self-test closes the generic IViewExecutor over the view's runtime row type via reflection.")]
    public static async Task<bool> RunAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var registry = sp.GetRequiredService<IViewRegistry>();
        var executor = sp.GetRequiredService<IViewExecutor>();
        var viewScope = sp.GetRequiredService<IViewScope>();

        var view = registry.Get(ViewName);
        if (view is null)
        {
            Console.WriteLine($"FAIL: view '{ViewName}' is not registered.");
            return false;
        }

        var adapter = new AgGridAdapter();

        Console.WriteLine("=== Vista AG Grid self-test ===");
        Console.WriteLine($"View       : {view.Name}");
        Console.WriteLine($"Route      : {view.Route} (adapter endpoint: POST {view.Route}/{adapter.RouteSuffix})");
        Console.WriteLine($"IsReadOnly : {view.IsReadOnly}");
        Console.WriteLine($"RowType    : {view.QueryType.Name}");
        Console.WriteLine($"Fields     : {string.Join(", ", view.Fields.Select(f => $"{f.Name}{(f.IsHidden ? " (hidden)" : "")}"))}");
        Console.WriteLine($"sortModel  : CategoryName asc, ProductName asc");
        Console.WriteLine($"filterModel: UnitPrice >= 20 AND UnitPrice < 100 (combined AND)");
        Console.WriteLine($"quick 'q'  : \"{QuickFilter}\"");
        Console.WriteLine();

        var allPassed = true;

        // The authoritative, unpaged ordering: one AG Grid block covering the whole matching set (the view's
        // MaxPageSize default is 100 and the full Northwind catalog is 77 rows, so [0, 100) returns every
        // matching row in the requested sort order). Every paged block must be a contiguous slice of this.
        var (full, bindOk) = await RunBlockAsync(adapter, view, executor, viewScope, startRow: 0, endRow: 100);
        allPassed &= bindOk;

        var totalMatching = full.RowCount;
        var fullRows = full.RowData;
        Console.WriteLine("[0] Full matching set (block [0, 100))");
        Console.WriteLine($"    rowCount={totalMatching}  rowData={fullRows.Count}");
        var fullConsistent = totalMatching == fullRows.Count && totalMatching > 10;
        Console.WriteLine($"    rowCount equals returned rows and set is large enough to page -> " +
            $"{(fullConsistent ? "PASS" : "FAIL")}");
        allPassed &= fullConsistent;

        // R8.2 — every matching row honours BOTH channels: the structured filter (UnitPrice in [20, 100)) and
        // the quick filter (substring "e" in a searchable string field). This proves rowCount is the true
        // matching total, not an unfiltered count.
        allPassed &= CheckMatchPredicate(fullRows);

        // R8.2 — the primary sort key (CategoryName) is non-decreasing, proving the sortModel was applied.
        allPassed &= CheckPrimarySortApplied(fullRows);

        // R8.2 — the first block [0, 10) is exactly the first 10 rows of the ordered matching set.
        allPassed &= await CheckBlockIsSliceAsync(
            adapter, view, executor, viewScope, fullRows, totalMatching, startRow: 0, endRow: 10);

        // R8.2 — a later block [10, 20) is exactly rows 10..20 of the ordered matching set (block paging maps
        // startRow/endRow to Page = startRow / pageSize with a block-aligned start).
        allPassed &= await CheckBlockIsSliceAsync(
            adapter, view, executor, viewScope, fullRows, totalMatching, startRow: 10, endRow: 20);

        // R8.6 — the response serializes (through the endpoint's serializer) to the AG Grid LoadSuccessParams
        // envelope with camelCase rowData/rowCount.
        allPassed &= CheckResponseShapeSerializes(full);

        Console.WriteLine();
        Console.WriteLine(allPassed ? "RESULT: PASS" : "RESULT: FAIL");
        return allPassed;
    }

    /// <summary>
    /// Builds an AG Grid request for the block <c>[startRow, endRow)</c> (shared sortModel/filterModel + the
    /// out-of-band quick filter), then drives it through the adapter and executor exactly as the endpoint
    /// does: <c>BindRequest</c> → <c>ToQuery</c> → <c>ListForAdapterAsync</c>-equivalent → <c>ToResponse</c>.
    /// </summary>
    /// <returns>The AG Grid response and whether the bound request carried the expected inputs (R8.2).</returns>
    [RequiresUnreferencedCode("Reflection over the runtime row type.")]
    private static async Task<(AgGridRowsResponse Response, bool BindOk)> RunBlockAsync(
        AgGridAdapter adapter,
        ViewMetadata view,
        IViewExecutor executor,
        IViewScope scope,
        int startRow,
        int endRow)
    {
        var jsonBody =
            $$"""
            {"startRow":{{startRow}},"endRow":{{endRow}},"sortModel":{{SortModelJson}},"filterModel":{{FilterModelJson}}}
            """;

        // The quick-filter text rides out-of-band under the documented "q" key (mirrors ?q= folded into
        // AdapterRequest.Values by the AspNetCore glue, R2.5).
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["q"] = new[] { QuickFilter },
        };

        var raw = new AdapterRequest(view.Name, values, JsonBody: jsonBody);
        var request = adapter.BindRequest(raw);

        // Verify the bound request carries the R8.2 inputs (block paging, >= 2 sort keys, a combined filter,
        // and the quick filter) before mapping — a partial bind would silently weaken every downstream check.
        var bindOk =
            request.StartRow == startRow &&
            request.EndRow == endRow &&
            request.SortModel.Count == 2 &&
            request.FilterModel.ContainsKey("UnitPrice") &&
            request.QuickFilter == QuickFilter;

        var query = adapter.ToQuery(request, view);

        // Execute through the same neutral pipeline the endpoint uses. ListForAdapterAsync needs an
        // HttpContext (auth + ShapeQuery), so here we invoke the executor directly and rebuild the
        // AdapterListResult from the paged result exactly as ListForAdapterAsync does
        // (rows + recordsFiltered + recordsTotal).
        var result = await ExecuteAsync(view, executor, query, scope);
        var response = adapter.ToResponse(result, request, view);

        return (response, bindOk);
    }

    /// <summary>
    /// Invokes the generic <see cref="IViewExecutor.ListAsync{TRow}"/> closed over the view's runtime row
    /// type and rebuilds an <see cref="AdapterListResult"/> from the paged result — mirroring
    /// <c>ViewRequestExecutor.ListForAdapterAsync</c>'s reflection bridge (rows + recordsFiltered +
    /// recordsTotal).
    /// </summary>
    [RequiresUnreferencedCode("Reflection over the runtime row type.")]
    private static async Task<AdapterListResult> ExecuteAsync(
        ViewMetadata view, IViewExecutor executor, ViewQueryRequest request, IViewScope scope)
    {
        var closed = ListAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, new object?[] { view, request, scope, CancellationToken.None })!;
        await task.ConfigureAwait(false);
        var listResult = task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task)!;

        var page = Prop(listResult, "Page")!;
        var recordsTotal = (long)Prop(listResult, "TotalRowsUnfiltered")!;
        var recordsFiltered = (long)Prop(page, "TotalRows")!;
        var rows = ToObjectList(Prop(page, "Items")!);

        return new AdapterListResult(rows, recordsFiltered, recordsTotal);
    }

    /// <summary>
    /// R8.2 — asserts the paged block <c>[startRow, endRow)</c> is exactly the corresponding slice of the
    /// ordered matching set, and that its <c>rowCount</c> equals the total matching rows before paging.
    /// </summary>
    [RequiresUnreferencedCode("Reflection over the runtime row type.")]
    private static async Task<bool> CheckBlockIsSliceAsync(
        AgGridAdapter adapter,
        ViewMetadata view,
        IViewExecutor executor,
        IViewScope scope,
        IReadOnlyList<object?> fullRows,
        long totalMatching,
        int startRow,
        int endRow)
    {
        var (block, bindOk) = await RunBlockAsync(adapter, view, executor, scope, startRow, endRow);

        var expected = fullRows.Skip(startRow).Take(endRow - startRow).Select(RowId).ToList();
        var actual = block.RowData.Select(RowId).ToList();

        var ok =
            bindOk &&
            block.RowCount == totalMatching &&
            actual.SequenceEqual(expected);

        Console.WriteLine($"[block [{startRow}, {endRow})] rowCount={block.RowCount} " +
            $"(expected {totalMatching}), rowData ProductIds=[{string.Join(", ", actual)}]");
        Console.WriteLine($"    matches ordered slice [{startRow}, {endRow}) -> {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    /// <summary>
    /// R8.2 — every returned row satisfies the structured filter (UnitPrice in [20, 100)) AND the quick
    /// filter (substring match on a searchable string field), proving both channels narrowed the result.
    /// </summary>
    private static bool CheckMatchPredicate(IReadOnlyList<object?> rows)
    {
        var ok = rows.Count > 0;
        foreach (var row in rows)
        {
            var unitPrice = Convert.ToDecimal(Prop(row!, "UnitPrice")!, CultureInfo.InvariantCulture);
            var inRange = unitPrice >= 20m && unitPrice < 100m;

            // Quick filter is a Contains over the searchable string fields (ProductName/CategoryName/
            // SupplierName). AG Grid quick-filter matching is case-insensitive.
            var productName = (string)Prop(row!, "ProductName")!;
            var categoryName = (string)Prop(row!, "CategoryName")!;
            var supplierName = (string)Prop(row!, "SupplierName")!;
            var matchesQuick =
                productName.Contains(QuickFilter, StringComparison.OrdinalIgnoreCase) ||
                categoryName.Contains(QuickFilter, StringComparison.OrdinalIgnoreCase) ||
                supplierName.Contains(QuickFilter, StringComparison.OrdinalIgnoreCase);

            ok &= inRange && matchesQuick;
        }

        Console.WriteLine($"[1] Every row honours filter (UnitPrice in [20,100)) AND quick filter " +
            $"(\"{QuickFilter}\") -> {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    /// <summary>R8.2 — the primary sort key (CategoryName) is non-decreasing, proving the sortModel was applied.</summary>
    private static bool CheckPrimarySortApplied(IReadOnlyList<object?> rows)
    {
        var categories = rows.Select(r => (string)Prop(r!, "CategoryName")!).ToList();
        var sorted = categories.SequenceEqual(categories.OrderBy(c => c, StringComparer.Ordinal));
        Console.WriteLine($"[2] Primary sort key CategoryName is non-decreasing -> {(sorted ? "PASS" : "FAIL")}");
        return sorted;
    }

    /// <summary>
    /// R8.6 — serializes the response through the endpoint's serializer (<see cref="VistaJson.Options"/>,
    /// web defaults) and asserts the AG Grid LoadSuccessParams envelope carries camelCase <c>rowData</c>
    /// and <c>rowCount</c>. This is the same serializer <c>Results.Json(.., VistaJson.Options)</c> uses at
    /// the endpoint, so the asserted body is the real wire shape.
    /// </summary>
    private static bool CheckResponseShapeSerializes(AgGridRowsResponse response)
    {
        var json = JsonSerializer.Serialize(response, VistaJson.Options);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var hasRowData = root.TryGetProperty("rowData", out var rowData) && rowData.ValueKind == JsonValueKind.Array;
        var hasRowCount = root.TryGetProperty("rowCount", out var rowCount) && rowCount.ValueKind == JsonValueKind.Number;

        var ok = hasRowData && hasRowCount;
        Console.WriteLine($"[3] Response serializes to {{ rowData, rowCount }} (camelCase) -> {(ok ? "PASS" : "FAIL")}");
        if (!ok)
        {
            Console.WriteLine($"    body: {json}");
        }

        return ok;
    }

    /// <summary>Reads the hidden <c>ProductId</c> primary key from a projected row (stable row identity).</summary>
    private static int RowId(object? row) =>
        Convert.ToInt32(Prop(row!, "ProductId")!, CultureInfo.InvariantCulture);

    private static object? Prop(object instance, string name) =>
        instance.GetType().GetProperty(name)?.GetValue(instance);

    private static List<object?> ToObjectList(object items)
    {
        var list = new List<object?>();
        foreach (var item in (IEnumerable)items)
        {
            list.Add(item);
        }

        return list;
    }
}
