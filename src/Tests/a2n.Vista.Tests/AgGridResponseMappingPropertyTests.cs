// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter, Property 6: Response rowData/rowCount mapping.
/// <para>
/// AG Grid's server-side row model expects a <c>LoadSuccessParams</c> shape <c>{ rowData, rowCount }</c>.
/// <see cref="AgGridAdapter.ToResponse"/> maps the neutral <see cref="AdapterListResult"/> into that shape
/// deterministically (D135):
/// </para>
/// <list type="bullet">
///   <item><description><c>rowData == result.Rows</c> — the exact same sequence, in order, and an empty
///   <c>rowData</c> array when <c>Rows</c> is empty (R5.1, R5.5);</description></item>
///   <item><description><c>rowCount == result.RecordsFiltered</c> — the filtered total, a non-negative
///   integer usable for AG Grid last-block detection at any offset (R5.1, R5.5);</description></item>
///   <item><description><c>RecordsTotal</c> is <b>never</b> surfaced — the server-side row model has no
///   slot for it (R5.1).</description></item>
/// </list>
/// <para>
/// The mapping is independent of the request and the view, so each generated case pairs an arbitrary row
/// list (covering the empty case) and an arbitrary non-negative filtered total (in the AG-Grid-compatible
/// <c>0..int.MaxValue</c> range) with a distinct <c>RecordsTotal</c> — proving the response never leaks the
/// unfiltered total into <c>rowCount</c>.
/// </para>
/// </summary>
public sealed class AgGridResponseMappingPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    private static readonly AgGridAdapter Adapter = new();

    /// <summary>A minimal view; response mapping never consults the fields.</summary>
    private static readonly ViewMetadata View = BuildMinimalView();

    /// <summary>A minimal request; response mapping never consults it.</summary>
    private static readonly AgGridRowsRequest Request = new()
    {
        StartRow = 0,
        EndRow = 100,
        SortModel = new List<AgGridSortModel>(),
        FilterModel = new Dictionary<string, System.Text.Json.JsonElement>(),
        QuickFilter = string.Empty,
    };

    // Feature: ag-grid-adapter, Property 6: Response rowData/rowCount mapping.
    //
    // Validates: Requirements 5.1, 5.5
    [Test]
    public void ToResponse_Maps_Rows_To_RowData_And_RecordsFiltered_To_RowCount()
    {
        // Feature: ag-grid-adapter, Property 6: Response rowData/rowCount mapping.
        GenCase.Sample(AssertResponseMapping, iter: Iterations);
    }

    /// <summary>
    /// Builds an <see cref="AdapterListResult"/> from the generated case, invokes
    /// <see cref="AgGridAdapter.ToResponse"/>, and asserts the <c>rowData</c>/<c>rowCount</c> invariants.
    /// </summary>
    private static void AssertResponseMapping(ResponseCase testCase)
    {
        var result = new AdapterListResult(
            Rows: testCase.Rows,
            RecordsFiltered: testCase.RecordsFiltered,
            RecordsTotal: testCase.RecordsTotal);

        var response = Adapter.ToResponse(result, Request, View);

        // rowCount == RecordsFiltered — never RecordsTotal (R5.1).
        if (response.RowCount != testCase.RecordsFiltered)
        {
            throw new Exception(
                $"rowCount not mapped from RecordsFiltered: expected {testCase.RecordsFiltered}, " +
                $"got {response.RowCount} (RecordsTotal={testCase.RecordsTotal}).");
        }

        // rowCount is a non-negative integer usable for AG Grid last-block detection (R5.5).
        if (response.RowCount < 0 || response.RowCount > int.MaxValue)
        {
            throw new Exception(
                $"rowCount is not an AG-Grid-compatible non-negative int: got {response.RowCount} " +
                "(require 0..2147483647).");
        }

        // rowData is the exact same sequence as result.Rows, in order (R5.1); empty stays empty (R5.5).
        if (response.RowData.Count != testCase.Rows.Count)
        {
            throw new Exception(
                $"rowData length differs from Rows: expected {testCase.Rows.Count}, got {response.RowData.Count}.");
        }

        for (var i = 0; i < testCase.Rows.Count; i++)
        {
            if (!Equals(response.RowData[i], testCase.Rows[i]))
            {
                throw new Exception(
                    $"rowData[{i}] differs from Rows[{i}]: expected '{testCase.Rows[i] ?? "null"}', " +
                    $"got '{response.RowData[i] ?? "null"}'.");
            }
        }

        // Empty Rows must yield an empty rowData array (R5.5) — checked explicitly for clarity.
        if (testCase.Rows.Count == 0 && response.RowData.Count != 0)
        {
            throw new Exception(
                $"empty Rows must map to an empty rowData array; got {response.RowData.Count} element(s).");
        }
    }

    // -- Generator --------------------------------------------------------------------------------------

    private sealed record ResponseCase(
        IReadOnlyList<object?> Rows,
        long RecordsFiltered,
        long RecordsTotal);

    // A row is one of: an int, a string, or null — enough to prove the sequence is passed through verbatim
    // without inspecting element types. Row lists include the empty case (length 0).
    private static readonly Gen<object?> GenRow =
        Gen.OneOf(
            Gen.Int[0, 1_000_000].Select(i => (object?)i),
            Gen.String[0, 12].Select(s => (object?)s),
            Gen.Const((object?)null));

    private static readonly Gen<ResponseCase> GenCase =
        from rows in GenRow.List[0, 25]
        // RecordsFiltered is a non-negative int within AG Grid's rowCount range (0..int.MaxValue).
        from recordsFiltered in Gen.Int[0, int.MaxValue]
        // RecordsTotal is independent (and typically >= filtered) — proves it never leaks into rowCount.
        from totalDelta in Gen.Int[0, 1_000_000]
        select new ResponseCase(
            rows,
            recordsFiltered,
            Math.Min((long)recordsFiltered + totalDelta, int.MaxValue));

    private static ViewMetadata BuildMinimalView()
    {
        var fields = new[]
        {
            FieldMetadata.Create(
                name: "Id",
                clrType: typeof(int),
                isFilterable: true,
                isSearchable: false,
                isScopable: false,
                allowedOperators: FilterOperator.Equals | FilterOperator.In),
        };

        return new ViewMetadata(
            Name: "AgGridResponseView",
            Route: "/test/AgGridResponseView",
            QueryType: typeof(object),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true);
    }
}
