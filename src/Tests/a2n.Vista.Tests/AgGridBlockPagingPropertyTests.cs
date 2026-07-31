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
/// Feature: ag-grid-adapter, Property 2: Block-paging mapping.
/// <para>
/// AG Grid's server-side row model requests a half-open block <c>[startRow, endRow)</c>. The adapter's
/// <see cref="AgGridAdapter.ToQuery"/> must translate that block into the neutral
/// <see cref="ViewQueryRequest"/> window deterministically (D135, revised by D144):
/// </para>
/// <list type="bullet">
///   <item><description><c>PageSize = endRow - startRow</c> — always, verbatim (R3.1, R3.2);</description></item>
///   <item><description><c>Offset = startRow</c> — the absolute row offset, carried verbatim rather than
///   divided into a page index, so neither an unaligned block nor the engine's page-size clamp can move the
///   window (D144);</description></item>
///   <item><description>when the size is <b>non-positive</b> (<c>endRow &lt;= startRow</c>), the adapter
///   passes the non-positive <c>PageSize</c> through <b>unchanged</b> — no clamping, defaulting, or
///   substitution — so the engine rejects it (R3.2).</description></item>
/// </list>
/// <para>
/// The paging mapping is independent of <c>sortModel</c>/<c>filterModel</c>/quick filter, so each generated
/// request carries only a row range (all other channels empty) and is bound to a minimal view. Start/end are
/// generated as non-negative integers with a delta biased to cover positive, zero, and negative
/// <c>PageSize</c> — exercising both branches of the mapping.
/// </para>
/// </summary>
public sealed class AgGridBlockPagingPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    private static readonly AgGridAdapter Adapter = new();

    /// <summary>A minimal view; paging never consults the fields, so a small metadata suffices.</summary>
    private static readonly ViewMetadata View = BuildMinimalView();

    // Feature: ag-grid-adapter, Property 2: Block-paging mapping.
    //
    // Validates: Requirements 3.1, 3.2
    [Test]
    public void ToQuery_Maps_Block_To_Page_And_PageSize_With_NonPositive_PassThrough()
    {
        // Feature: ag-grid-adapter, Property 2: Block-paging mapping.
        GenRange.Sample(AssertPagingMapping, iter: Iterations);
    }

    /// <summary>
    /// Builds an <see cref="AgGridRowsRequest"/> carrying only the generated block, invokes
    /// <see cref="AgGridAdapter.ToQuery"/>, and asserts the paging invariants for both the positive and the
    /// non-positive (pass-through) branch.
    /// </summary>
    private static void AssertPagingMapping((int Start, int End) range)
    {
        var request = new AgGridRowsRequest
        {
            StartRow = range.Start,
            EndRow = range.End,
            SortModel = new List<AgGridSortModel>(),
            FilterModel = new Dictionary<string, System.Text.Json.JsonElement>(),
            QuickFilter = string.Empty,
        };

        var query = Adapter.ToQuery(request, View);

        var expectedPageSize = range.End - range.Start;

        // PageSize is always endRow - startRow, verbatim — no clamp/default in either branch (R3.1, R3.2).
        if (query.PageSize != expectedPageSize)
        {
            throw new Exception(
                $"PageSize not mapped verbatim: startRow={range.Start}, endRow={range.End}, " +
                $"expected PageSize={expectedPageSize}, got {query.PageSize}.");
        }

        // The block start is carried verbatim as the absolute offset in EVERY branch (D144); no division,
        // so an unaligned block keeps its exact position and the engine's page-size clamp cannot shift it.
        if (query.Offset != range.Start)
        {
            throw new Exception(
                $"Offset not carried verbatim: startRow={range.Start}, endRow={range.End}, " +
                $"expected Offset={range.Start}, got {query.Offset?.ToString() ?? "null"}.");
        }

        if (expectedPageSize <= 0)
        {
            // Non-positive block: the non-positive PageSize is passed through so the engine rejects it. The
            // adapter must not clamp or default the size (already asserted above); Page carries no meaning.
            if (query.PageSize > 0)
            {
                throw new Exception(
                    $"Non-positive PageSize was altered: startRow={range.Start}, endRow={range.End}, " +
                    $"got PageSize={query.PageSize} (expected {expectedPageSize}, unchanged).");
            }
        }
    }

    // -- Generator --------------------------------------------------------------------------------------

    // Non-negative startRow and a delta biased across (-,0,+) so endRow - startRow covers positive, zero,
    // and negative PageSize. endRow is floored at 0 to keep both bounds non-negative (the bound-validity
    // precondition of the property).
    private static readonly Gen<(int Start, int End)> GenRange =
        from start in Gen.Int[0, 100_000]
        from delta in Gen.Int[-100, 100_000]
        select (start, Math.Max(0, start + delta));

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
            Name: "AgGridPagingView",
            Route: "/test/AgGridPagingView",
            QueryType: typeof(object),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true);
    }
}
