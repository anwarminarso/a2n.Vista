using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Contracts;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for the multi-channel request (Decision Log D111, R7): the List executor compiles the
/// <c>Filter</c>/<c>Search</c>/<c>Scope</c> sub-trees each under its own origin whitelist, AND-s them, and
/// counts the client <c>Scope</c> toward <c>recordsTotal</c> while excluding <c>Filter</c>/<c>Search</c>.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Executor tests exercise the runtime reflection path by design.")]
public sealed class MultiChannelTests
{
    [Test]
    public async Task Search_Slot_Narrows_But_Preserves_RecordsTotal()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();

        // Global search on the searchable string field "Name".
        var request = new ViewQueryRequest(
            Filter: null,
            Sort: Array.Empty<SortSpec>(),
            Page: 0,
            PageSize: 50,
            SelectFields: null,
            Search: new FilterLeaf("Name", FilterOperator.Contains, "Widget 1"),
            Scope: null);

        var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);

        // "Widget 1", "Widget 10".."Widget 19" → 11 of 25 rows.
        await Assert.That(result.TotalRowsUnfiltered).IsEqualTo(WidgetTestHarness.SeededRowCount);
        await Assert.That(result.Page.TotalRows).IsEqualTo(11L);
    }

    [Test]
    public async Task Search_On_NonSearchable_Field_Is_Rejected()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();

        // "Id" is not searchable → a Search-origin leaf must be rejected.
        var request = new ViewQueryRequest(
            Filter: null,
            Sort: Array.Empty<SortSpec>(),
            Page: 0,
            PageSize: 50,
            SelectFields: null,
            Search: new FilterLeaf("Id", FilterOperator.Contains, "1"),
            Scope: null);

        await Assert.That(await Capture(() =>
            harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None))).IsNotNull();
    }

    [Test]
    public async Task Scope_On_NonScopable_Field_Is_Rejected()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();

        // No field is scopable by default (D47) → a Scope-origin leaf must be rejected.
        var request = new ViewQueryRequest(
            Filter: null,
            Sort: Array.Empty<SortSpec>(),
            Page: 0,
            PageSize: 50,
            SelectFields: null,
            Search: null,
            Scope: new FilterLeaf("Price", FilterOperator.Equals, 50m));

        await Assert.That(await Capture(() =>
            harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None))).IsNotNull();
    }

    [Test]
    public async Task Scope_Counts_Toward_RecordsTotal_While_Filter_Does_Not()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WithScopableId(WidgetTestHarness.BuildView());

        // Scope: Id <= 10 (10 rows in the working context). Filter: Price >= 80 (Id >= 8).
        var request = new ViewQueryRequest(
            Filter: new FilterLeaf("Price", FilterOperator.GreaterThanOrEqual, 80m),
            Sort: Array.Empty<SortSpec>(),
            Page: 0,
            PageSize: 50,
            SelectFields: null,
            Search: null,
            Scope: new FilterLeaf("Id", FilterOperator.LessThanOrEqual, 10));

        var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);

        // recordsTotal counts the scope (10), recordsFiltered also applies the filter → Id 8, 9, 10 = 3.
        await Assert.That(result.TotalRowsUnfiltered).IsEqualTo(10L);
        await Assert.That(result.Page.TotalRows).IsEqualTo(3L);
    }

    [Test]
    public async Task Null_Search_And_Scope_Reproduce_Legacy_Filter_Only_Behavior()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();

        var request = new ViewQueryRequest(
            Filter: new FilterLeaf("Price", FilterOperator.GreaterThanOrEqual, 200m),
            Sort: Array.Empty<SortSpec>(),
            Page: 0,
            PageSize: 50);

        var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);

        // Price >= 200 → Id 20..25 → 6 rows; recordsTotal unaffected (no scope).
        await Assert.That(result.TotalRowsUnfiltered).IsEqualTo(WidgetTestHarness.SeededRowCount);
        await Assert.That(result.Page.TotalRows).IsEqualTo(6L);
    }

    /// <summary>Returns a copy of <paramref name="view"/> with the <c>Id</c> field marked scopable.</summary>
    private static ViewMetadata WithScopableId(ViewMetadata view)
    {
        var fields = view.Fields
            .Select(f => f.Name == "Id" ? f with { IsScopable = true } : f)
            .ToArray();

        return view with { Fields = fields };
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
