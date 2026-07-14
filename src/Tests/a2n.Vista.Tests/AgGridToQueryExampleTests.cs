// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter (task 4.5) — targeted example/edge-case coverage for
/// <see cref="AgGridAdapter.ToQuery(AgGridRowsRequest, ViewMetadata)"/>. These are plain (non-property)
/// unit tests over fixed inputs that complement the block-paging (task 4.2), sort-model (task 4.3), and
/// channel-isolation (task 4.4) property tests.
/// <para>They assert the concrete <c>ToQuery</c> guarantees over worked examples:</para>
/// <list type="bullet">
///   <item><description>block-paging arithmetic — <c>PageSize = EndRow - StartRow</c> and
///   <c>Page = StartRow / PageSize</c> (zero-based floor), with a non-positive <c>PageSize</c> passed
///   through unchanged so the engine rejects it (R3.1/R3.2).</description></item>
///   <item><description>multi-sort priority order preserved; a non-<c>"desc"</c> direction is ascending;
///   an entry whose <c>colId</c> is not a view field is skipped without disturbing the rest
///   (R3.3/R3.4).</description></item>
///   <item><description>the parsed <c>filterModel</c> lands only in the <c>Filter</c> slot (AND-ed across
///   columns), and the quick filter lands only in the <c>Search</c> slot (a <c>FilterOr</c> of
///   <c>Contains</c> over the searchable string fields) (R4.4/R4.5).</description></item>
///   <item><description>null-when-empty: an empty <c>filterModel</c> leaves <c>Filter</c> unset and an
///   empty/whitespace quick filter leaves <c>Search</c> unset (R4.8/R4.9).</description></item>
/// </list>
/// </summary>
public sealed class AgGridToQueryExampleTests
{
    private static readonly AgGridAdapter Adapter = new();

    // -- test view -------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a view with two searchable string fields (<c>Name</c>, <c>Description</c>), a non-searchable
    /// numeric key (<c>Id</c>), and a non-searchable numeric field (<c>Price</c>). This exercises the
    /// quick-filter channel's per-field selection: only the two searchable string fields become
    /// <c>Contains</c> leaves.
    /// </summary>
    private static ViewMetadata BuildView(string name = "vToQuery")
    {
        var fields = new[]
        {
            FieldMetadata.Create(
                name: "Id",
                clrType: typeof(int),
                isFilterable: true,
                isSortable: true,
                isSearchable: false,
                allowedOperators: FilterOperator.Equals | FilterOperator.In),
            FieldMetadata.Create(
                name: "Name",
                clrType: typeof(string),
                isFilterable: true,
                isSortable: true,
                isSearchable: true,
                allowedOperators: FilterOperator.Text | FilterOperator.In),
            FieldMetadata.Create(
                name: "Description",
                clrType: typeof(string),
                isFilterable: true,
                isSortable: true,
                isSearchable: true,
                allowedOperators: FilterOperator.Text),
            FieldMetadata.Create(
                name: "Price",
                clrType: typeof(decimal),
                isFilterable: true,
                isSortable: true,
                isSearchable: false,
                allowedOperators: FilterOperator.Range | FilterOperator.Equals),
        };

        return new ViewMetadata(
            Name: name,
            Route: $"/test/{name}",
            QueryType: typeof(object),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: true)
        {
            KeyFields = ["Id"],
        };
    }

    /// <summary>
    /// Builds a view with exactly one searchable string field (<c>Name</c>) so the single-field
    /// quick-filter branch (a bare <see cref="FilterLeaf"/>, not a <see cref="FilterOr"/>) can be asserted.
    /// </summary>
    private static ViewMetadata BuildSingleSearchableView()
    {
        var fields = new[]
        {
            FieldMetadata.Create(
                name: "Id",
                clrType: typeof(int),
                isFilterable: true,
                isSortable: true,
                isSearchable: false,
                allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create(
                name: "Name",
                clrType: typeof(string),
                isFilterable: true,
                isSortable: true,
                isSearchable: true,
                allowedOperators: FilterOperator.Text),
        };

        return new ViewMetadata(
            Name: "vSingleSearch",
            Route: "/test/vSingleSearch",
            QueryType: typeof(object),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: true)
        {
            KeyFields = ["Id"],
        };
    }

    /// <summary>Deserializes a JSON object into a self-contained <see cref="JsonElement"/> (safe to store).</summary>
    private static JsonElement El(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    // -- paging arithmetic (R3.1, R3.2) ---------------------------------------------------------------

    /// <summary>R3.1: a block starting at 0 maps to page 0 with the block width as the page size.</summary>
    [Test]
    public async Task Paging_FirstBlock_Page0()
    {
        var request = new AgGridRowsRequest { StartRow = 0, EndRow = 100 };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.PageSize).IsEqualTo(100);
        await Assert.That(query.Page).IsEqualTo(0);
    }

    /// <summary>R3.1: an aligned block maps to a page index via zero-based integer division.</summary>
    [Test]
    public async Task Paging_AlignedBlock_PageIndex()
    {
        var request = new AgGridRowsRequest { StartRow = 20, EndRow = 40 };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.PageSize).IsEqualTo(20);
        await Assert.That(query.Page).IsEqualTo(1);
    }

    /// <summary>R3.1: a non-aligned block floors the page index (<c>10 / 30 == 0</c>).</summary>
    [Test]
    public async Task Paging_NonAlignedBlock_FloorsPageIndex()
    {
        var request = new AgGridRowsRequest { StartRow = 10, EndRow = 40 };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.PageSize).IsEqualTo(30);
        await Assert.That(query.Page).IsEqualTo(0);
    }

    /// <summary>R3.2: a degenerate block (<c>EndRow == StartRow</c>) passes <c>PageSize = 0</c> through unchanged.</summary>
    [Test]
    public async Task Paging_ZeroWidthBlock_PassesZeroThrough()
    {
        var request = new AgGridRowsRequest { StartRow = 10, EndRow = 10 };

        var query = Adapter.ToQuery(request, BuildView());

        // No clamp / default / substitution: the engine rejects the zero page size.
        await Assert.That(query.PageSize).IsEqualTo(0);
        await Assert.That(query.Page).IsEqualTo(0);
    }

    /// <summary>R3.2: an inverted block (<c>EndRow &lt; StartRow</c>) passes the negative <c>PageSize</c> through unchanged.</summary>
    [Test]
    public async Task Paging_InvertedBlock_PassesNegativeThrough()
    {
        var request = new AgGridRowsRequest { StartRow = 50, EndRow = 40 };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.PageSize).IsEqualTo(-10);
        await Assert.That(query.Page).IsEqualTo(0);
    }

    // -- multi-sort priority + direction (R3.3) -------------------------------------------------------

    /// <summary>R3.3: multi-sort entries keep their relative order and map direction from <c>sort</c>.</summary>
    [Test]
    public async Task Sort_MultiSort_PreservesOrderAndDirection()
    {
        var request = new AgGridRowsRequest
        {
            SortModel =
            {
                new AgGridSortModel { ColId = "Name", Sort = "desc" },
                new AgGridSortModel { ColId = "Price", Sort = "asc" },
            },
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Sort.Count).IsEqualTo(2);
        await Assert.That(query.Sort[0]).IsEqualTo(new SortSpec("Name", true));
        await Assert.That(query.Sort[1]).IsEqualTo(new SortSpec("Price", false));
    }

    /// <summary>R3.3: any <c>sort</c> value other than <c>"desc"</c> (case-insensitive) yields ascending.</summary>
    [Test]
    public async Task Sort_NonDescDirection_IsAscending()
    {
        var request = new AgGridRowsRequest
        {
            SortModel =
            {
                new AgGridSortModel { ColId = "Name", Sort = "sideways" },
                new AgGridSortModel { ColId = "Price", Sort = "DESC" },
            },
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Sort[0]).IsEqualTo(new SortSpec("Name", false));
        await Assert.That(query.Sort[1]).IsEqualTo(new SortSpec("Price", true));
    }

    // -- skip non-field colId (R3.4) ------------------------------------------------------------------

    /// <summary>R3.4: an entry whose <c>colId</c> is not a view field is skipped; the rest keep their order.</summary>
    [Test]
    public async Task Sort_SkipsNonFieldColId_PreservingRemainingOrder()
    {
        var request = new AgGridRowsRequest
        {
            SortModel =
            {
                new AgGridSortModel { ColId = "Name", Sort = "asc" },
                new AgGridSortModel { ColId = "NotAField", Sort = "desc" },
                new AgGridSortModel { ColId = "Price", Sort = "desc" },
            },
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Sort.Count).IsEqualTo(2);
        await Assert.That(query.Sort[0]).IsEqualTo(new SortSpec("Name", false));
        await Assert.That(query.Sort[1]).IsEqualTo(new SortSpec("Price", true));
    }

    /// <summary>R3.4: an entry with an empty <c>colId</c> is skipped (never fabricating a sort).</summary>
    [Test]
    public async Task Sort_SkipsEmptyColId()
    {
        var request = new AgGridRowsRequest
        {
            SortModel =
            {
                new AgGridSortModel { ColId = string.Empty, Sort = "desc" },
                new AgGridSortModel { ColId = "Name", Sort = "asc" },
            },
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Sort.Count).IsEqualTo(1);
        await Assert.That(query.Sort[0]).IsEqualTo(new SortSpec("Name", false));
    }

    // -- Filter slot placement (R4.4) -----------------------------------------------------------------

    /// <summary>R4.4: a single mapped column lands as one leaf in the <c>Filter</c> slot; <c>Search</c> stays null.</summary>
    [Test]
    public async Task Filter_SingleColumn_LandsInFilterSlot()
    {
        var request = new AgGridRowsRequest
        {
            FilterModel =
            {
                ["Name"] = El("{\"filterType\":\"text\",\"type\":\"contains\",\"filter\":\"widget\"}"),
            },
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Filter is FilterLeaf).IsTrue();
        var leaf = (FilterLeaf)query.Filter!;
        await Assert.That(leaf.Field).IsEqualTo("Name");
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(leaf.Value).IsEqualTo("widget");

        // The structured filter never leaks into the Search channel.
        await Assert.That(query.Search).IsNull();
    }

    /// <summary>R4.4: two mapped columns are AND-ed together into a single <c>Filter</c> sub-tree.</summary>
    [Test]
    public async Task Filter_MultipleColumns_AndedInFilterSlot()
    {
        var request = new AgGridRowsRequest
        {
            FilterModel =
            {
                ["Name"] = El("{\"filterType\":\"text\",\"type\":\"contains\",\"filter\":\"widget\"}"),
                ["Price"] = El("{\"filterType\":\"number\",\"type\":\"greaterThan\",\"filter\":10}"),
            },
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Filter is FilterAnd).IsTrue();
        var and = (FilterAnd)query.Filter!;
        await Assert.That(and.Children.Count).IsEqualTo(2);
        await Assert.That(query.Search).IsNull();
    }

    // -- Search slot placement (R4.5) -----------------------------------------------------------------

    /// <summary>
    /// R4.5: a non-empty quick filter lands in the <c>Search</c> slot as a <see cref="FilterOr"/> of
    /// <c>Contains</c> leaves over exactly the searchable string fields (<c>Name</c>, <c>Description</c>) —
    /// non-searchable / non-string fields (<c>Id</c>, <c>Price</c>) are excluded; <c>Filter</c> stays null.
    /// </summary>
    [Test]
    public async Task Search_QuickFilter_LandsInSearchSlotAsOrOfContains()
    {
        var request = new AgGridRowsRequest { QuickFilter = "gadget" };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Filter).IsNull();
        await Assert.That(query.Search is FilterOr).IsTrue();

        var or = (FilterOr)query.Search!;
        await Assert.That(or.Children.Count).IsEqualTo(2);

        var first = (FilterLeaf)or.Children[0];
        var second = (FilterLeaf)or.Children[1];
        await Assert.That(first.Field).IsEqualTo("Name");
        await Assert.That(first.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(first.Value).IsEqualTo("gadget");
        await Assert.That(second.Field).IsEqualTo("Description");
        await Assert.That(second.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(second.Value).IsEqualTo("gadget");
    }

    /// <summary>R4.5: with a single searchable string field, the quick filter is a bare leaf (not wrapped in an Or).</summary>
    [Test]
    public async Task Search_QuickFilter_SingleSearchableField_IsBareLeaf()
    {
        var request = new AgGridRowsRequest { QuickFilter = "gadget" };

        var query = Adapter.ToQuery(request, BuildSingleSearchableView());

        await Assert.That(query.Search is FilterLeaf).IsTrue();
        var leaf = (FilterLeaf)query.Search!;
        await Assert.That(leaf.Field).IsEqualTo("Name");
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(leaf.Value).IsEqualTo("gadget");
    }

    /// <summary>R4.4/R4.5: the two channels coexist without leaking into each other.</summary>
    [Test]
    public async Task FilterAndSearch_Coexist_InSeparateSlots()
    {
        var request = new AgGridRowsRequest
        {
            QuickFilter = "gadget",
            FilterModel =
            {
                ["Price"] = El("{\"filterType\":\"number\",\"type\":\"greaterThan\",\"filter\":10}"),
            },
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Filter is FilterLeaf).IsTrue();
        await Assert.That(((FilterLeaf)query.Filter!).Field).IsEqualTo("Price");
        await Assert.That(query.Search is FilterOr).IsTrue();
        await Assert.That(query.Scope).IsNull();
    }

    // -- null-when-empty (R4.8, R4.9) -----------------------------------------------------------------

    /// <summary>R4.8: an empty <c>filterModel</c> leaves the <c>Filter</c> slot unset.</summary>
    [Test]
    public async Task Filter_EmptyModel_LeavesFilterSlotNull()
    {
        var request = new AgGridRowsRequest { StartRow = 0, EndRow = 10 };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Filter).IsNull();
    }

    /// <summary>R4.9: an empty quick filter leaves the <c>Search</c> slot unset.</summary>
    [Test]
    public async Task Search_EmptyQuickFilter_LeavesSearchSlotNull()
    {
        var request = new AgGridRowsRequest { QuickFilter = string.Empty };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Search).IsNull();
    }

    /// <summary>R4.9: a whitespace-only quick filter leaves the <c>Search</c> slot unset.</summary>
    [Test]
    public async Task Search_WhitespaceQuickFilter_LeavesSearchSlotNull()
    {
        var request = new AgGridRowsRequest { QuickFilter = "   \t  " };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Search).IsNull();
    }
}
