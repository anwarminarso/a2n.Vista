// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter, Property 3: Sort-model mapping preserves order and direction.
/// <para>
/// <see cref="AgGridAdapter.ToQuery"/> translates the AG Grid <c>sortModel</c> array (a priority-ordered
/// list of <c>{ colId, sort }</c> keys) into the neutral <see cref="SortSpec"/> list on
/// <see cref="ViewQueryRequest.Sort"/>. The contract (R3.3–R3.5) is:
/// </para>
/// <list type="bullet">
///   <item><description>the produced list contains <b>exactly one entry per</b> <c>sortModel</c> entry, in
///   their <b>original relative order</b> (multi-sort priority preserved, R3.3);</description></item>
///   <item><description>each entry maps to <c>SortSpec(colId, Descending)</c> where
///   <c>Descending</c> is <see langword="true"/> for a <c>"desc"</c> direction and <see langword="false"/>
///   for any other value (R3.3);</description></item>
///   <item><description>an entry whose <c>colId</c> is not a view field (or is empty) is <b>carried through
///   verbatim, never dropped</b>, so the engine rejects it with 400 — the adapter builds, the engine
///   enforces (R3.4, D150);</description></item>
///   <item><description>an absent/empty <c>sortModel</c> yields an <b>empty</b> <see cref="SortSpec"/>
///   list with <b>no</b> default ordering (R3.5).</description></item>
/// </list>
/// <para>
/// The mapping is therefore field-set-independent: it is a total, order-preserving function of the
/// <c>sortModel</c> array alone, which is what makes a typo impossible to confuse with a UI column
/// (issue #2). Whether a <c>colId</c> names a field is the engine's ordinal question, asked later.
/// </para>
/// <para>
/// The direction predicate is case-insensitive on <c>"desc"</c>: the implementation is the source of truth
/// (a project non-negotiable), and AG Grid only ever emits lowercase <c>"asc"</c>/<c>"desc"</c> on the
/// wire, so the case-insensitive rule and the literal <c>sort == "desc"</c> rule agree on every real
/// input; the generator additionally probes mixed-case and arbitrary tokens.
/// </para>
/// </summary>
public sealed class AgGridSortModelMappingPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    private static readonly AgGridAdapter Adapter = new();

    /// <summary>The view fields (case-sensitive names) a <c>colId</c> must match to be kept.</summary>
    private static readonly string[] FieldNames = { "Id", "Name", "Price", "Category", "CreatedOn" };

    /// <summary>
    /// Column ids that are NOT view fields — UI columns, typos, mis-cased field names, and the empty string.
    /// Each must still reach the engine unchanged (D150): the adapter has no basis to tell a UI column from a
    /// misspelling, and guessing is what hid a broken sort behind a 200 (issue #2).
    /// </summary>
    private static readonly string[] NonFieldColIds = { "Actions", "_select", "rowIndex", "unknownCol", "name", "price", "" };

    /// <summary>Direction tokens: canonical, mixed-case, and arbitrary non-<c>desc</c> values.</summary>
    private static readonly string[] Directions = { "asc", "desc", "ASC", "DESC", "Desc", "", "descending", "xyz" };

    /// <summary>A single AG Grid view over <see cref="FieldNames"/>; sort mapping is field-set-driven only.</summary>
    private static readonly ViewMetadata View = BuildView();

    // Feature: ag-grid-adapter, Property 3: Sort-model mapping preserves order and direction.
    //
    // Validates: Requirements 3.3, 3.4, 3.5
    [Test]
    public void SortModel_Maps_To_Ordered_Field_SortSpecs_With_Correct_Direction()
    {
        // Feature: ag-grid-adapter, Property 3: Sort-model mapping preserves order and direction.
        GenSortModel.Sample(
            sortModel =>
            {
                var request = new AgGridRowsRequest
                {
                    // A positive block so paging is valid and orthogonal to the sort mapping under test.
                    StartRow = 0,
                    EndRow = 20,
                    SortModel = sortModel,
                };

                var query = Adapter.ToQuery(request, View);

                var expected = ExpectedSort(sortModel);
                AssertSortEquals(expected, query.Sort, sortModel);
            },
            iter: Iterations);
    }

    // Feature: ag-grid-adapter, Property 3: Sort-model mapping preserves order and direction.
    //
    // Validates: Requirements 3.5 (an empty sortModel yields an empty SortSpec list, no default ordering).
    [Test]
    public void Empty_SortModel_Yields_Empty_Sort_With_No_Default_Ordering()
    {
        var request = new AgGridRowsRequest { StartRow = 0, EndRow = 20, SortModel = new List<AgGridSortModel>() };

        var query = Adapter.ToQuery(request, View);

        if (query.Sort is null || query.Sort.Count != 0)
        {
            throw new Exception(
                $"An empty sortModel must yield an empty SortSpec list (no default ordering); got {Describe(query.Sort)}.");
        }
    }

    // -- Oracle -----------------------------------------------------------------------------------------

    /// <summary>
    /// The reference mapping: every entry, in order, as <c>SortSpec(colId, Descending)</c> with
    /// <c>Descending</c> = case-insensitive match on <c>"desc"</c>. The view field set is deliberately not
    /// consulted — an unknown <c>colId</c> is the engine's business, not the adapter's (D150).
    /// </summary>
    private static List<SortSpec> ExpectedSort(IReadOnlyList<AgGridSortModel> sortModel)
    {
        var expected = new List<SortSpec>(sortModel.Count);
        foreach (var entry in sortModel)
        {
            var descending = string.Equals(entry.Sort, "desc", StringComparison.OrdinalIgnoreCase);
            expected.Add(new SortSpec(entry.ColId, descending));
        }

        return expected;
    }

    private static void AssertSortEquals(
        IReadOnlyList<SortSpec> expected,
        IReadOnlyList<SortSpec> actual,
        IReadOnlyList<AgGridSortModel> sortModel)
    {
        if (actual is null)
        {
            throw new Exception($"ToQuery produced a null Sort list.\n  sortModel: {DescribeModel(sortModel)}");
        }

        if (actual.Count != expected.Count)
        {
            throw new Exception(
                "Sort count mismatch (an entry was dropped or fabricated; every sortModel entry must map 1:1).\n" +
                $"  sortModel: {DescribeModel(sortModel)}\n" +
                $"  expected:  {Describe(expected)}\n" +
                $"  actual:    {Describe(actual)}");
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(expected[i].Field, actual[i].Field, StringComparison.Ordinal)
                || expected[i].Descending != actual[i].Descending)
            {
                throw new Exception(
                    $"Sort[{i}] mismatch (order/field/direction).\n" +
                    $"  sortModel: {DescribeModel(sortModel)}\n" +
                    $"  expected:  {Describe(expected)}\n" +
                    $"  actual:    {Describe(actual)}");
            }
        }
    }

    // -- Generators -------------------------------------------------------------------------------------

    /// <summary>Any colId — a view field, a non-field UI column, or the empty string.</summary>
    private static readonly Gen<string> GenColId =
        Gen.OneOf(Pick(FieldNames), Pick(NonFieldColIds));

    private static readonly Gen<AgGridSortModel> GenEntry =
        from colId in GenColId
        from sort in Pick(Directions)
        select new AgGridSortModel { ColId = colId, Sort = sort };

    /// <summary>0–6 sort entries; the empty list exercises the no-default-ordering case (R3.5).</summary>
    private static readonly Gen<List<AgGridSortModel>> GenSortModel = GenEntry.List[0, 6];

    private static Gen<T> Pick<T>(IReadOnlyList<T> values) =>
        Gen.Int[0, values.Count - 1].Select(i => values[i]);

    // -- View + diagnostics -----------------------------------------------------------------------------

    private static ViewMetadata BuildView()
    {
        var fields = new[]
        {
            FieldMetadata.Create("Id", typeof(int), isFilterable: true, isSortable: true, isSearchable: false,
                allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("Name", typeof(string), isFilterable: true, isSortable: true, isSearchable: true,
                allowedOperators: FilterOperator.Text),
            FieldMetadata.Create("Price", typeof(decimal), isFilterable: true, isSortable: true, isSearchable: false,
                allowedOperators: FilterOperator.Range | FilterOperator.Equals),
            FieldMetadata.Create("Category", typeof(string), isFilterable: true, isSortable: true, isSearchable: true,
                allowedOperators: FilterOperator.Text),
            FieldMetadata.Create("CreatedOn", typeof(DateTime), isFilterable: true, isSortable: true, isSearchable: false,
                allowedOperators: FilterOperator.Range | FilterOperator.Equals),
        };

        return new ViewMetadata(
            Name: "AgGridSortView",
            Route: "/test/AgGridSortView",
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

    private static string Describe(IReadOnlyList<SortSpec>? sort) =>
        sort is null ? "<null>" : $"[{string.Join(", ", MapSort(sort))}]";

    private static IEnumerable<string> MapSort(IReadOnlyList<SortSpec> sort)
    {
        foreach (var s in sort)
        {
            yield return $"{s.Field}:{(s.Descending ? "desc" : "asc")}";
        }
    }

    private static string DescribeModel(IReadOnlyList<AgGridSortModel> model)
    {
        var parts = new List<string>(model.Count);
        foreach (var m in model)
        {
            parts.Add($"({m.ColId},{m.Sort})");
        }

        return $"[{string.Join(", ", parts)}]";
    }
}
