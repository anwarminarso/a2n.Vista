using System.Diagnostics.CodeAnalysis;
using System.Linq;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Paging & response shape (Requirement R10), exercised end to end through the real
/// <see cref="a2n.Vista.EntityFrameworkCore.Execution.EfViewExecutor"/> List path against a seeded
/// SQLite database (25 widgets). The executor is wired via <see cref="WidgetTestHarness"/>, which
/// overrides only the source-resolution seam to inject a pre-projected, SQLite-backed queryable; the
/// paging maths, filtered/unfiltered totals, page-size clamp/reject, and async cancellation are the
/// production code under test.
/// <list type="bullet">
/// <item>R10.1 — <see cref="a2n.Vista.Results.PagedResult{T}"/> totals are <see langword="long"/>; <c>PageIndex</c> is 0-based.</item>
/// <item>R10.2 — the List path is async and honors a <see cref="CancellationToken"/>.</item>
/// <item>R10.3 — page size exceeding <see cref="HardLimits.MaxPageSize"/> is clamped; <c>length=-1</c>/non-positive is rejected.</item>
/// <item>R10.4 — both the filtered total (<c>recordsFiltered</c>) and the unfiltered total (<c>recordsTotal</c>) are reported.</item>
/// </list>
/// All assertions use a deterministic ascending sort on <c>Id</c> so page contents are stable (SQLite
/// row order is otherwise unspecified).
/// </summary>
// EfViewExecutor.ListAsync and the overridden source seam are [RequiresUnreferencedCode] (they resolve
// sort/filter/projection from metadata at runtime; the AOT-clean route is the Pilar 3 source generator).
// These tests exercise that reflection path by design, so the trim/AOT diagnostic is suppressed here.
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests exercise the runtime reflection path of EfViewExecutor by design; trimming is not used for tests.")]
public sealed class PagingTests
{
    private static readonly IReadOnlyList<SortSpec> ById = new[] { new SortSpec(nameof(WidgetRow.Id)) };

    /// <summary>
    /// R10.1 / R10.4: a first page of 10 over 25 rows reports <see langword="long"/> totals
    /// (<c>TotalRows == 25</c>, <c>TotalRowsUnfiltered == 25</c>, <c>TotalPages == 3</c>), echoes the
    /// 0-based <c>PageIndex</c> and the effective <c>PageSize</c>, and returns the first 10 rows.
    /// </summary>
    [Test]
    public async Task First_Page_Reports_Long_Totals_And_Zero_Based_Index()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();
        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 10);

        var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);

        // Totals are long-typed by contract; bind to long locals to assert both the static type and the value.
        long totalRows = result.Page.TotalRows;
        long totalPages = result.Page.TotalPages;
        await Assert.That(totalRows).IsEqualTo(25L);
        await Assert.That(result.TotalRowsUnfiltered).IsEqualTo(25L);
        await Assert.That(totalPages).IsEqualTo(3L);

        await Assert.That(result.Page.PageIndex).IsEqualTo(0);
        await Assert.That(result.Page.PageSize).IsEqualTo(10);
        await Assert.That(result.Page.Items.Count).IsEqualTo(10);

        // 0-based first page = ids 1..10.
        await Assert.That(result.Page.Items.Select(r => r.Id).ToArray()).IsEquivalentTo(Enumerable.Range(1, 10).ToArray());
    }

    /// <summary>
    /// R10.1 (0-based): page index 1 returns the SECOND page (ids 11..20) and echoes the requested page.
    /// </summary>
    [Test]
    public async Task Second_Page_Is_Zero_Based()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();
        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 1, PageSize: 10);

        var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);

        await Assert.That(result.Page.PageIndex).IsEqualTo(1);
        await Assert.That(result.Page.Items.Select(r => r.Id).ToArray()).IsEquivalentTo(Enumerable.Range(11, 10).ToArray());
    }

    /// <summary>
    /// R10.1 (0-based, last page): page index 2 returns the remaining 5 rows (ids 21..25).
    /// </summary>
    [Test]
    public async Task Last_Page_Returns_Remainder()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();
        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 2, PageSize: 10);

        var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);

        await Assert.That(result.Page.PageIndex).IsEqualTo(2);
        await Assert.That(result.Page.Items.Count).IsEqualTo(5);
        await Assert.That(result.Page.Items.Select(r => r.Id).ToArray()).IsEquivalentTo(Enumerable.Range(21, 5).ToArray());
    }

    /// <summary>
    /// R10.4: with a client filter applied (<c>Name CONTAINS "5"</c> → "Widget 5", "Widget 15",
    /// "Widget 25"), the filtered total is less than the unfiltered total. Exercises the real
    /// <see cref="a2n.Vista.EntityFrameworkCore.Execution.ProviderAwareFilterCompiler"/> SQLite <c>LIKE</c>.
    /// </summary>
    [Test]
    public async Task Filtered_Total_Less_Than_Unfiltered_Total()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();
        var filter = new FilterLeaf(nameof(WidgetRow.Name), FilterOperator.Contains, "5");
        var request = new ViewQueryRequest(Filter: filter, Sort: ById, Page: 0, PageSize: 10);

        var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);

        await Assert.That(result.TotalRowsUnfiltered).IsEqualTo(25L);
        await Assert.That(result.Page.TotalRows).IsEqualTo(3L);
        await Assert.That(result.Page.TotalRows).IsLessThan(result.TotalRowsUnfiltered);
        await Assert.That(result.Page.Items.Select(r => r.Id).ToArray()).IsEquivalentTo(new[] { 5, 15, 25 });
    }

    /// <summary>
    /// R10.3 (clamp): a page size beyond the view's <see cref="HardLimits.MaxPageSize"/> is clamped to
    /// the limit — the result reports the clamped page size and returns no more than that many rows.
    /// </summary>
    [Test]
    public async Task PageSize_Exceeding_Limit_Is_Clamped()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView(maxPageSize: 10);
        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 1000);

        var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);

        await Assert.That(result.Page.PageSize).IsEqualTo(10);
        await Assert.That(result.Page.Items.Count).IsEqualTo(10);
    }

    /// <summary>
    /// R10.3 (reject): a non-positive page size (DataTables <c>length=-1</c>, or <c>0</c>) is rejected
    /// with <see cref="ArgumentOutOfRangeException"/> before any DB round-trip. The AspNetCore layer
    /// maps this to HTTP 400.
    /// </summary>
    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    public async Task NonPositive_PageSize_Is_Rejected(int pageSize)
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();
        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: pageSize);

        await Assert.That(async () =>
                await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// R10.2: the List path honors cancellation — an already-canceled token surfaces as an
    /// <see cref="OperationCanceledException"/> (<see cref="TaskCanceledException"/> derives from it).
    /// </summary>
    [Test]
    public async Task Canceled_Token_Throws_OperationCanceled()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();
        var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 10);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () =>
                await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), cts.Token))
            .Throws<OperationCanceledException>();
    }
}
