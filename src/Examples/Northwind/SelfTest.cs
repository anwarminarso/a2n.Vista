using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace a2n.Vista.Examples.Northwind;

/// <summary>
/// End-to-end verification harness for the <c>vProductCategory</c> view (Requirement R12). It drives the
/// real Core <see cref="IViewExecutor"/> exactly as the HTTP layer does — closing the generic
/// List/Detail methods over the view's runtime (anonymous) row type via reflection — and asserts that:
/// <list type="bullet">
///   <item><description>List returns a paged <c>PagedResult</c> with the expected totals/paging (R12.1).</description></item>
///   <item><description>Filter + sort + global-search-style Contains narrow the result (R12.3).</description></item>
///   <item><description>Detail by the hidden <c>ProductId</c> primary key resolves a row (R12.2).</description></item>
/// </list>
/// Run it with <c>dotnet run -- selftest</c>.
/// </summary>
public static class SelfTest
{
    private const string ViewName = "vProductCategory";

    private static readonly MethodInfo ListAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.ListAsync))!;

    private static readonly MethodInfo DetailAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.DetailAsync))!;

    /// <summary>
    /// Runs the self-test against a built application's services.
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

        Console.WriteLine("=== Vista Northwind self-test ===");
        Console.WriteLine($"View      : {view.Name}");
        Console.WriteLine($"Route     : {view.Route}");
        Console.WriteLine($"IsReadOnly: {view.IsReadOnly}");
        Console.WriteLine($"RowType   : {view.QueryType.Name}");
        Console.WriteLine($"Fields    : {string.Join(", ", view.Fields.Select(f => $"{f.Name}{(f.IsHidden ? " (hidden)" : "")}"))}");
        Console.WriteLine();

        var allPassed = true;

        allPassed &= await PagingCheckAsync(view, executor, viewScope);
        allPassed &= await FilterSortSearchCheckAsync(view, executor, viewScope);
        allPassed &= await DetailCheckAsync(view, executor, viewScope);

        Console.WriteLine();
        Console.WriteLine(allPassed ? "RESULT: PASS" : "RESULT: FAIL");
        return allPassed;
    }

    /// <summary>R12.1 — List returns a paged <c>PagedResult</c> (0-based index, long totals, clamped page).</summary>
    [RequiresUnreferencedCode("Reflection over the runtime row type.")]
    private static async Task<bool> PagingCheckAsync(ViewMetadata view, IViewExecutor executor, IViewScope scope)
    {
        // No filter, sort by ProductName ascending, page 0 with a small page size to force multiple pages.
        var request = new ViewQueryRequest(
            Filter: null,
            Sort: new[] { new SortSpec("ProductName") },
            Page: 0,
            PageSize: 3);

        var listResult = await InvokeListAsync(view, executor, request, scope);
        var page = Prop(listResult, "Page")!;
        var totalRowsUnfiltered = (long)Prop(listResult, "TotalRowsUnfiltered")!;

        var totalRows = (long)Prop(page, "TotalRows")!;
        var pageIndex = (int)Prop(page, "PageIndex")!;
        var pageSize = (int)Prop(page, "PageSize")!;
        var totalPages = (long)Prop(page, "TotalPages")!;
        var items = ToObjectList(Prop(page, "Items")!);
        var names = items.Select(i => (string)Prop(i, "ProductName")!).ToList();

        Console.WriteLine("[1] Paging (no filter, sort ProductName asc, page 0, size 3)");
        Console.WriteLine($"    TotalRows(filtered)={totalRows}  TotalRowsUnfiltered={totalRowsUnfiltered}  " +
            $"PageIndex={pageIndex}  PageSize={pageSize}  TotalPages={totalPages}  Items={items.Count}");
        Console.WriteLine($"    Page items: {string.Join(", ", names)}");

        var sortedAscending = names.SequenceEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        var ok =
            totalRows == 77 &&
            totalRowsUnfiltered == 77 &&
            pageIndex == 0 &&
            pageSize == 3 &&
            totalPages == 26 &&
            items.Count == 3 &&
            sortedAscending;

        Console.WriteLine($"    -> {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    /// <summary>R12.3 — filter (UnitPrice) + global-search-style Contains (ProductName) + sort.</summary>
    [RequiresUnreferencedCode("Reflection over the runtime row type.")]
    private static async Task<bool> FilterSortSearchCheckAsync(ViewMetadata view, IViewExecutor executor, IViewScope scope)
    {
        // A structured filter (UnitPrice >= 20) AND a global-search-style substring match on a string
        // field (ProductName Contains "a"). The whole tree is validated under FilterOrigin.Filter by the
        // executor; both leaves target filterable fields with allowed operators.
        var filter = new FilterAnd(new FilterNode[]
        {
            new FilterLeaf("UnitPrice", FilterOperator.GreaterThanOrEqual, 20m),
            new FilterLeaf("ProductName", FilterOperator.Contains, "a"),
        });

        var request = new ViewQueryRequest(
            Filter: filter,
            Sort: new[] { new SortSpec("ProductName") },
            Page: 0,
            PageSize: 10);

        var listResult = await InvokeListAsync(view, executor, request, scope);
        var page = Prop(listResult, "Page")!;
        var totalRowsUnfiltered = (long)Prop(listResult, "TotalRowsUnfiltered")!;
        var totalRows = (long)Prop(page, "TotalRows")!;
        var items = ToObjectList(Prop(page, "Items")!);
        var names = items.Select(i => (string)Prop(i, "ProductName")!).ToList();

        Console.WriteLine("[2] Filter + search (UnitPrice>=20 AND ProductName Contains 'a', sort ProductName asc)");
        Console.WriteLine($"    recordsTotal(unfiltered)={totalRowsUnfiltered}  recordsFiltered={totalRows}  Items={items.Count}");
        Console.WriteLine($"    Matched: {string.Join(", ", names)}");

        // The full Northwind catalog has 77 products; this structured filter + substring search narrows
        // it to a stable subset. We assert the filter actually reduces the set and the first page is full
        // and correctly ordered, rather than pinning an exact name list (accented names make that brittle).
        var ok =
            totalRowsUnfiltered == 77 &&
            totalRows == 31 &&
            totalRows < totalRowsUnfiltered &&
            items.Count == 10 &&
            names.SequenceEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        Console.WriteLine($"    -> {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    /// <summary>R12.2 — Detail resolves a row by the hidden <c>ProductId</c> primary key.</summary>
    [RequiresUnreferencedCode("Reflection over the runtime row type.")]
    private static async Task<bool> DetailCheckAsync(ViewMetadata view, IViewExecutor executor, IViewScope scope)
    {
        const int productId = 1;

        var closed = DetailAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, new object?[] { view, productId, scope, CancellationToken.None })!;
        await task.ConfigureAwait(false);
        var row = task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task);

        Console.WriteLine($"[3] Detail by ProductId={productId} (hidden PK, resolved by convention)");
        if (row is null)
        {
            Console.WriteLine("    -> FAIL: no row returned");
            return false;
        }

        var resolvedId = Convert.ToInt32(Prop(row, "ProductId")!, CultureInfo.InvariantCulture);
        var name = (string)Prop(row, "ProductName")!;
        var categoryName = (string)Prop(row, "CategoryName")!;
        Console.WriteLine($"    Row: ProductId={resolvedId}  ProductName={name}  CategoryName={categoryName}");

        var ok = resolvedId == productId && name == "Chai";
        Console.WriteLine($"    -> {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    [RequiresUnreferencedCode("Reflection over the runtime row type.")]
    private static async Task<object> InvokeListAsync(
        ViewMetadata view, IViewExecutor executor, ViewQueryRequest request, IViewScope scope)
    {
        var closed = ListAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, new object?[] { view, request, scope, CancellationToken.None })!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task)!;
    }

    private static object? Prop(object instance, string name) =>
        instance.GetType().GetProperty(name)?.GetValue(instance);

    private static List<object> ToObjectList(object items)
    {
        var list = new List<object>();
        foreach (var item in (IEnumerable)items)
        {
            list.Add(item);
        }

        return list;
    }
}
