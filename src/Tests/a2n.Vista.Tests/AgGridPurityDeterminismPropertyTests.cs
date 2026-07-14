// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter, Property 1: Adapter purity and determinism.
/// <para>
/// The three <see cref="AgGridAdapter"/> steps — <see cref="AgGridAdapter.BindRequest"/>,
/// <see cref="AgGridAdapter.ToQuery"/>, and <see cref="AgGridAdapter.ToResponse"/> — are pure mapping
/// functions (Spec 04 §5.1, R1.3/R1.4). For any generated input, invoking a step twice with the same input
/// must yield <b>structurally-equal</b> output (determinism), and each step must complete <b>without</b> an
/// <c>HttpContext</c>, a <c>DbContext</c>, network, file, or static mutable state (purity).
/// </para>
/// <para>
/// Determinism is asserted directly: each step is invoked twice per generated case and the two outputs are
/// compared structurally. Purity is asserted <b>by construction</b> — these tests run with no
/// <c>HttpContext</c>/<c>DbContext</c>/network/file available at all, so a step completing to a correct
/// result demonstrates it needs none of them. To additionally rule out hidden <b>instance</b> or
/// <b>static</b> mutable state, the second invocation of every step uses a <b>fresh</b> adapter instance
/// (distinct from the shared one used for the first call): if any per-instance or process-wide state leaked
/// between calls, the two structurally-compared outputs would diverge across the 100+ interleaved cases.
/// </para>
/// <para>
/// Each generated case bundles a valid, non-Advanced request in three forms: an
/// <see cref="AdapterRequest"/> (JSON body + out-of-band <c>Values["q"]</c>) for <c>BindRequest</c>, a
/// populated <see cref="AgGridRowsRequest"/> plus a randomized <see cref="ViewMetadata"/> for
/// <c>ToQuery</c>, and an <see cref="AdapterListResult"/> for <c>ToResponse</c>.
/// </para>
/// </summary>
public sealed class AgGridPurityDeterminismPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    /// <summary>The shared adapter used for the <b>first</b> invocation of each step.</summary>
    private static readonly AgGridAdapter Shared = new();

    // Feature: ag-grid-adapter, Property 1: Adapter purity and determinism.
    //
    // Validates: Requirements 1.3, 1.4
    [Test]
    public void All_Three_Steps_Are_Deterministic_And_Pure()
    {
        // Feature: ag-grid-adapter, Property 1: Adapter purity and determinism.
        GenCase.Sample(AssertPureAndDeterministic, iter: Iterations);
    }

    /// <summary>
    /// Invokes each step twice — once on the shared adapter, once on a fresh instance — with identical
    /// inputs and asserts the two outputs are structurally equal. A fresh instance for the second call rules
    /// out per-instance/static mutable state; running with no HttpContext/DbContext/network/file rules out
    /// external I/O (the step could not complete otherwise).
    /// </summary>
    private static void AssertPureAndDeterministic(AdapterCase testCase)
    {
        var fresh = new AgGridAdapter();

        // --- BindRequest: pure parse of the JSON body + out-of-band quick filter ------------------------
        var bound1 = Shared.BindRequest(testCase.BindInput);
        var bound2 = fresh.BindRequest(testCase.BindInput);
        if (!RequestsEqual(bound1, bound2))
        {
            throw new Exception(
                "BindRequest is not deterministic/pure: two invocations on equal input produced different requests.\n" +
                $"  body: {testCase.BindInput.JsonBody}\n" +
                $"  first:  {DescribeRequest(bound1)}\n" +
                $"  second: {DescribeRequest(bound2)}");
        }

        // --- ToQuery: pure mapping of the request + view into the neutral ViewQueryRequest --------------
        var query1 = Shared.ToQuery(testCase.QueryInput, testCase.View);
        var query2 = fresh.ToQuery(testCase.QueryInput, testCase.View);
        if (!QueriesEqual(query1, query2))
        {
            throw new Exception(
                "ToQuery is not deterministic/pure: two invocations on equal input produced different queries.\n" +
                $"  first:  {DescribeQuery(query1)}\n" +
                $"  second: {DescribeQuery(query2)}");
        }

        // --- ToResponse: pure mapping of the neutral result into the AG Grid response -------------------
        var response1 = Shared.ToResponse(testCase.ResponseInput, testCase.QueryInput, testCase.View);
        var response2 = fresh.ToResponse(testCase.ResponseInput, testCase.QueryInput, testCase.View);
        if (!ResponsesEqual(response1, response2))
        {
            throw new Exception(
                "ToResponse is not deterministic/pure: two invocations on equal input produced different responses.\n" +
                $"  first:  rowCount={response1.RowCount}, rows={response1.RowData.Count}\n" +
                $"  second: rowCount={response2.RowCount}, rows={response2.RowData.Count}");
        }
    }

    // -- Structural equality: AgGridRowsRequest ---------------------------------------------------------

    private static bool RequestsEqual(AgGridRowsRequest a, AgGridRowsRequest b)
    {
        if (a.StartRow != b.StartRow || a.EndRow != b.EndRow)
        {
            return false;
        }

        if (!string.Equals(a.QuickFilter, b.QuickFilter, StringComparison.Ordinal))
        {
            return false;
        }

        if (a.SortModel.Count != b.SortModel.Count)
        {
            return false;
        }

        for (var i = 0; i < a.SortModel.Count; i++)
        {
            if (!string.Equals(a.SortModel[i].ColId, b.SortModel[i].ColId, StringComparison.Ordinal)
                || !string.Equals(a.SortModel[i].Sort, b.SortModel[i].Sort, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (a.FilterModel.Count != b.FilterModel.Count)
        {
            return false;
        }

        foreach (var (key, elementA) in a.FilterModel)
        {
            if (!b.FilterModel.TryGetValue(key, out var elementB))
            {
                return false;
            }

            // Compare descriptors by canonical JSON (JsonElement has no value equality).
            if (!string.Equals(JsonSerializer.Serialize(elementA), JsonSerializer.Serialize(elementB), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // -- Structural equality: ViewQueryRequest ----------------------------------------------------------

    private static bool QueriesEqual(ViewQueryRequest a, ViewQueryRequest b)
    {
        if (a.Page != b.Page || a.PageSize != b.PageSize)
        {
            return false;
        }

        if (!FilterEqual(a.Filter, b.Filter) || !FilterEqual(a.Search, b.Search) || !FilterEqual(a.Scope, b.Scope))
        {
            return false;
        }

        if (!SortEqual(a.Sort, b.Sort))
        {
            return false;
        }

        return SelectFieldsEqual(a.SelectFields, b.SelectFields);
    }

    private static bool SortEqual(IReadOnlyList<SortSpec>? a, IReadOnlyList<SortSpec>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Field, b[i].Field, StringComparison.Ordinal) || a[i].Descending != b[i].Descending)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SelectFieldsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.SequenceEqual(b, StringComparer.Ordinal);
    }

    // -- Structural equality: FilterNode (recursive) ----------------------------------------------------

    private static bool FilterEqual(FilterNode? a, FilterNode? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return (a, b) switch
        {
            (FilterLeaf la, FilterLeaf lb) =>
                string.Equals(la.Field, lb.Field, StringComparison.Ordinal)
                && la.Op == lb.Op
                && ValueEqual(la.Value, lb.Value),
            (FilterNot na, FilterNot nb) => FilterEqual(na.Child, nb.Child),
            (FilterAnd aa, FilterAnd ab) => ChildrenEqual(aa.Children, ab.Children),
            (FilterOr oa, FilterOr ob) => ChildrenEqual(oa.Children, ob.Children),
            _ => false,
        };
    }

    private static bool ChildrenEqual(IReadOnlyList<FilterNode> a, IReadOnlyList<FilterNode> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!FilterEqual(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValueEqual(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        // Value lists (e.g. In / Between) compare element-wise; scalars compare by value.
        if (a is System.Collections.IEnumerable ea and not string && b is System.Collections.IEnumerable eb and not string)
        {
            return ea.Cast<object?>().SequenceEqual(eb.Cast<object?>());
        }

        return a.Equals(b);
    }

    // -- Structural equality: AgGridRowsResponse --------------------------------------------------------

    private static bool ResponsesEqual(AgGridRowsResponse a, AgGridRowsResponse b)
    {
        if (a.RowCount != b.RowCount)
        {
            return false;
        }

        if (a.RowData.Count != b.RowData.Count)
        {
            return false;
        }

        for (var i = 0; i < a.RowData.Count; i++)
        {
            if (!ValueEqual(a.RowData[i], b.RowData[i]))
            {
                return false;
            }
        }

        return true;
    }

    // -- Diagnostics ------------------------------------------------------------------------------------

    private static string DescribeRequest(AgGridRowsRequest r) =>
        $"start={r.StartRow}, end={r.EndRow}, sort={r.SortModel.Count}, filter={r.FilterModel.Count}, q='{r.QuickFilter}'";

    private static string DescribeQuery(ViewQueryRequest q) =>
        $"page={q.Page}, size={q.PageSize}, sort={q.Sort?.Count ?? -1}, filter={Describe(q.Filter)}, search={Describe(q.Search)}, scope={Describe(q.Scope)}";

    private static string Describe(FilterNode? node) => node switch
    {
        null => "<null>",
        FilterLeaf l => $"Leaf({l.Field},{l.Op},{l.Value ?? "null"})",
        FilterNot n => $"Not({Describe(n.Child)})",
        FilterAnd a => $"And[{string.Join(",", a.Children.Select(Describe))}]",
        FilterOr o => $"Or[{string.Join(",", o.Children.Select(Describe))}]",
        _ => "<node>",
    };

    // -- Test case + generators -------------------------------------------------------------------------

    /// <summary>All three step inputs for one generated case.</summary>
    private sealed record AdapterCase(
        AdapterRequest BindInput,
        AgGridRowsRequest QueryInput,
        ViewMetadata View,
        AdapterListResult ResponseInput);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValues =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>View field names; the generated view includes an arbitrary non-empty subset.</summary>
    private static readonly string[] FieldNames = { "Id", "Name", "Price", "Category", "CreatedOn", "Sku" };

    /// <summary>CLR types a generated field can take (only <see cref="string"/> is search-eligible).</summary>
    private static readonly Type[] Types = { typeof(string), typeof(int), typeof(decimal), typeof(DateTime) };

    /// <summary>colIds for sort/filter — a superset of the field names (Ghost/Phantom are non-fields).</summary>
    private static readonly string[] ColIdPool =
        { "Id", "Name", "Price", "Category", "CreatedOn", "Sku", "Ghost", "Phantom" };

    private static readonly string[] SortDirections = { "asc", "desc", "ASC", "DESC", "", "xyz" };

    /// <summary>Quick-filter options; combined with an "include" flag that models an absent <c>Values["q"]</c> key.</summary>
    private static readonly string[] QuickOptions = { "", "   ", "search", "Widget 1", "naïve café" };

    /// <summary>Representative non-Advanced AG Grid descriptors (text/number/date/set/combined).</summary>
    private static readonly string[] FilterDescriptors =
    {
        "{\"filterType\":\"text\",\"type\":\"contains\",\"filter\":\"abc\"}",
        "{\"filterType\":\"text\",\"type\":\"notContains\",\"filter\":\"xyz\"}",
        "{\"filterType\":\"text\",\"type\":\"equals\",\"filter\":\"exact\"}",
        "{\"filterType\":\"number\",\"type\":\"equals\",\"filter\":42}",
        "{\"filterType\":\"number\",\"type\":\"inRange\",\"filter\":10,\"filterTo\":100}",
        "{\"filterType\":\"date\",\"type\":\"greaterThan\",\"dateFrom\":\"2020-01-01\"}",
        "{\"filterType\":\"set\",\"values\":[\"a\",\"b\",\"c\"]}",
        "{\"filterType\":\"set\",\"values\":[]}",
        "{\"filterType\":\"text\",\"operator\":\"OR\",\"conditions\":[" +
            "{\"filterType\":\"text\",\"type\":\"startsWith\",\"filter\":\"x\"}," +
            "{\"filterType\":\"text\",\"type\":\"endsWith\",\"filter\":\"y\"}]}",
    };

    private static Gen<T> Pick<T>(IReadOnlyList<T> values) =>
        Gen.Int[0, values.Count - 1].Select(i => values[i]);

    private static readonly Gen<AgGridSortModel> GenSort =
        from colId in Pick(ColIdPool)
        from sort in Pick(SortDirections)
        select new AgGridSortModel { ColId = colId, Sort = sort };

    private static readonly Gen<Dictionary<string, JsonElement>> GenFilterModel =
        (from col in Gen.Int[0, FieldNames.Length - 1]
         from desc in Gen.Int[0, FilterDescriptors.Length - 1]
         select (col, desc)).List[0, 4]
        .Select(entries =>
        {
            var model = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (col, desc) in entries)
            {
                model[FieldNames[col]] = Parse(FilterDescriptors[desc]);
            }

            return model;
        });

    /// <summary>Row items for the neutral result: a mix of strings, ints, and nulls.</summary>
    private static readonly Gen<object?> GenRow =
        Gen.OneOf(
            Gen.Int[0, 1_000].Select(i => (object?)i),
            Pick(new[] { "alpha", "beta", "gamma", "" }).Select(s => (object?)s),
            Gen.Const((object?)null));

    private static readonly Gen<AdapterCase> GenCase =
        from start in Gen.Int[0, 100_000]
        from length in Gen.Int[0, 500]
        from sortModel in GenSort.List[0, 4]
        from filterModel in GenFilterModel
        from quick in Pick(QuickOptions)
        from includeQuick in Gen.Bool
        from includeSort in Gen.Bool
        from includeFilter in Gen.Bool
        from view in GenView
        from rows in GenRow.List[0, 10]
        from recordsFiltered in Gen.Int[0, int.MaxValue]
        from recordsTotal in Gen.Int[0, int.MaxValue]
        select BuildCase(start, length, sortModel, filterModel, includeQuick ? quick : null, includeSort, includeFilter, view, rows, recordsFiltered, recordsTotal);

    private static AdapterCase BuildCase(
        int start,
        int length,
        List<AgGridSortModel> sortModel,
        Dictionary<string, JsonElement> filterModel,
        string? quick,
        bool includeSort,
        bool includeFilter,
        ViewMetadata view,
        List<object?> rows,
        int recordsFiltered,
        int recordsTotal)
    {
        var end = start + length;

        // --- BindRequest input: serialize a valid, non-Advanced request to a JSON body ------------------
        var intended = new AgGridRowsRequest
        {
            StartRow = start,
            EndRow = end,
            SortModel = sortModel,
            FilterModel = filterModel,
            QuickFilter = string.Empty,
        };

        var node = JsonNode.Parse(
            JsonSerializer.Serialize(intended, AgGridJsonContext.Default.AgGridRowsRequest))!.AsObject();

        // The quick filter never travels in the JSON body — it is read from Values["q"].
        RemoveIgnoreCase(node, "QuickFilter");
        if (!includeSort)
        {
            RemoveIgnoreCase(node, "SortModel");
        }

        if (!includeFilter)
        {
            RemoveIgnoreCase(node, "FilterModel");
        }

        var values = quick is null
            ? NoValues
            : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["q"] = new[] { quick } };

        var bindInput = new AdapterRequest("v", values, node.ToJsonString());

        // --- ToQuery input: a fully-populated request (quick filter set directly) -----------------------
        var queryInput = new AgGridRowsRequest
        {
            StartRow = start,
            EndRow = end,
            SortModel = sortModel,
            FilterModel = filterModel,
            QuickFilter = quick ?? string.Empty,
        };

        // --- ToResponse input: a neutral list result ----------------------------------------------------
        var responseInput = new AdapterListResult(rows, recordsFiltered, recordsTotal);

        return new AdapterCase(bindInput, queryInput, view, responseInput);
    }

    private static Gen<ViewMetadata> GenView =>
        from includeMask in Gen.Int[1, (1 << FieldNames.Length) - 1]
        from typeCodes in Gen.Int[0, Types.Length - 1].Array[FieldNames.Length]
        from searchMask in Gen.Int[0, (1 << FieldNames.Length) - 1]
        from filterMask in Gen.Int[0, (1 << FieldNames.Length) - 1]
        select BuildView(includeMask, typeCodes, searchMask, filterMask);

    private static ViewMetadata BuildView(int includeMask, int[] typeCodes, int searchMask, int filterMask)
    {
        var fields = new List<FieldMetadata>();
        for (var i = 0; i < FieldNames.Length; i++)
        {
            if ((includeMask & (1 << i)) == 0)
            {
                continue;
            }

            fields.Add(FieldMetadata.Create(
                name: FieldNames[i],
                clrType: Types[typeCodes[i]],
                isFilterable: (filterMask & (1 << i)) != 0,
                isSortable: true,
                isSearchable: (searchMask & (1 << i)) != 0,
                allowedOperators: FilterOperator.Text | FilterOperator.Equals | FilterOperator.In | FilterOperator.Range));
        }

        return new ViewMetadata(
            Name: "AgGridPurityView",
            Route: "/test/AgGridPurityView",
            QueryType: typeof(object),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: true)
        {
            KeyFields = new[] { fields[0].Name },
        };
    }

    private static void RemoveIgnoreCase(JsonObject obj, string name)
    {
        string? match = null;
        foreach (var member in obj)
        {
            if (string.Equals(member.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                match = member.Key;
                break;
            }
        }

        if (match is not null)
        {
            obj.Remove(match);
        }
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
