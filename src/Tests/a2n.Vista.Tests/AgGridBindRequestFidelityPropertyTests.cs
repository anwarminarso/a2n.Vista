// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.AgGrid;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter, Property 7: Request bind fidelity and absent-collection defaulting.
/// <para>
/// <see cref="AgGridAdapter.BindRequest"/> is the parse step of the adapter: it must reproduce the AG Grid
/// request faithfully from the JSON body and, critically, <b>default absent collections/text rather than
/// leaving them null or partially populated</b> (R2.1). For any generated <see cref="AgGridRowsRequest"/>
/// serialized to a JSON body, binding must:
/// </para>
/// <list type="bullet">
///   <item><description>reproduce <c>StartRow</c>/<c>EndRow</c> exactly;</description></item>
///   <item><description>reproduce every <c>sortModel</c> entry (<c>colId</c>/<c>sort</c>) in order;</description></item>
///   <item><description>reproduce every <c>filterModel</c> entry value-for-value (by canonical JSON);</description></item>
///   <item><description>bind an <b>absent</b> <c>sortModel</c>/<c>filterModel</c> to an <b>empty</b>
///   (never null) collection;</description></item>
///   <item><description>bind an <b>absent</b> quick-filter key (<c>Values["q"]</c>) to <b>empty</b> text
///   (never null), and a present key to its exact value.</description></item>
/// </list>
/// <para>
/// The quick filter is supplied out-of-band via <c>AdapterRequest.Values["q"]</c> (never in the JSON body),
/// so the generator drives it through the values bag; every generated body is a valid, non-Advanced request
/// (<c>startRow &gt;= 0</c>, <c>endRow &gt;= startRow</c>) so binding succeeds and the fidelity/defaulting
/// invariants are the property under test.
/// </para>
/// </summary>
public sealed class AgGridBindRequestFidelityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    private static readonly AgGridAdapter Adapter = new();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValues =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    // Feature: ag-grid-adapter, Property 7: Request bind fidelity and absent-collection defaulting.
    //
    // Validates: Requirements 2.1
    [Test]
    public void BindRequest_Reproduces_Fields_And_Defaults_Absent_Collections_And_Text()
    {
        // Feature: ag-grid-adapter, Property 7: Request bind fidelity and absent-collection defaulting.
        GenCase.Sample(AssertBindFidelity, iter: Iterations);
    }

    /// <summary>
    /// Serializes the intended request to a JSON body (optionally omitting <c>sortModel</c>/<c>filterModel</c>
    /// to exercise absent-collection defaulting), supplies the quick filter out-of-band via
    /// <c>Values["q"]</c> (optionally absent), then asserts <see cref="AgGridAdapter.BindRequest"/>
    /// reproduces every field and defaults every absent input to a non-null empty value.
    /// </summary>
    private static void AssertBindFidelity(BindCase testCase)
    {
        var raw = new AdapterRequest("v", testCase.Values, testCase.JsonBody);

        var bound = Adapter.BindRequest(raw);

        if (bound is null)
        {
            throw new Exception("BindRequest returned null for a valid AG Grid request body.");
        }

        // Paging fields reproduced exactly.
        if (bound.StartRow != testCase.ExpectedStartRow || bound.EndRow != testCase.ExpectedEndRow)
        {
            throw new Exception(
                $"Paging not reproduced: startRow {testCase.ExpectedStartRow}->{bound.StartRow}, " +
                $"endRow {testCase.ExpectedEndRow}->{bound.EndRow}.\n  body: {testCase.JsonBody}");
        }

        // sortModel: never null; reproduced entry-for-entry when present, empty when absent.
        if (bound.SortModel is null)
        {
            throw new Exception($"SortModel bound to null (must be an empty collection).\n  body: {testCase.JsonBody}");
        }

        if (bound.SortModel.Count != testCase.ExpectedSort.Count)
        {
            throw new Exception(
                $"SortModel count not reproduced: {testCase.ExpectedSort.Count} -> {bound.SortModel.Count}.\n" +
                $"  body: {testCase.JsonBody}");
        }

        for (var i = 0; i < testCase.ExpectedSort.Count; i++)
        {
            var expected = testCase.ExpectedSort[i];
            var actual = bound.SortModel[i];
            if (!string.Equals(expected.ColId, actual.ColId, StringComparison.Ordinal)
                || !string.Equals(expected.Sort, actual.Sort, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"SortModel[{i}] not reproduced: ({expected.ColId},{expected.Sort}) -> " +
                    $"({actual.ColId},{actual.Sort}).\n  body: {testCase.JsonBody}");
            }
        }

        // filterModel: never null; reproduced value-for-value when present, empty when absent.
        if (bound.FilterModel is null)
        {
            throw new Exception($"FilterModel bound to null (must be an empty collection).\n  body: {testCase.JsonBody}");
        }

        if (bound.FilterModel.Count != testCase.ExpectedFilter.Count)
        {
            throw new Exception(
                $"FilterModel count not reproduced: {testCase.ExpectedFilter.Count} -> {bound.FilterModel.Count}.\n" +
                $"  body: {testCase.JsonBody}");
        }

        foreach (var (key, expectedElement) in testCase.ExpectedFilter)
        {
            if (!bound.FilterModel.TryGetValue(key, out var actualElement))
            {
                throw new Exception($"FilterModel key '{key}' missing after bind.\n  body: {testCase.JsonBody}");
            }

            var expectedJson = JsonSerializer.Serialize(expectedElement);
            var actualJson = JsonSerializer.Serialize(actualElement);
            if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"FilterModel['{key}'] not reproduced:\n  before: {expectedJson}\n  after:  {actualJson}");
            }
        }

        // Quick filter: never null; absent key → empty text, present key → the exact value.
        if (bound.QuickFilter is null)
        {
            throw new Exception("QuickFilter bound to null (must be empty text when absent).");
        }

        if (!string.Equals(bound.QuickFilter, testCase.ExpectedQuickFilter, StringComparison.Ordinal))
        {
            throw new Exception(
                $"QuickFilter not reproduced: expected '{testCase.ExpectedQuickFilter}', got '{bound.QuickFilter}'.");
        }
    }

    // -- Test case + generator --------------------------------------------------------------------------

    /// <summary>The generated inputs plus the expected post-bind values.</summary>
    private sealed record BindCase(
        string JsonBody,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
        int ExpectedStartRow,
        int ExpectedEndRow,
        IReadOnlyList<AgGridSortModel> ExpectedSort,
        IReadOnlyDictionary<string, JsonElement> ExpectedFilter,
        string ExpectedQuickFilter);

    private static readonly string[] Columns = { "Id", "Name", "Price", "Category", "CreatedOn" };

    private static readonly string[] SortDirections = { "asc", "desc", "ASC", "DESC", "unknown", "" };

    // Quick-filter options: null models an ABSENT Values["q"] key (→ empty text); a non-null value (incl. "")
    // models a present key whose exact value must be reproduced. All are well under the 1,024-char cap.
    private static readonly string?[] QuickFilterOptions = { null, "", "abc", "Widget 1", "naïve café", "a\"b\"c" };

    // Representative NON-Advanced AG Grid per-column descriptors (text/number/date/set/combined). Bind
    // fidelity holds for any non-Advanced JsonElement; these mirror the D134 wire shapes for realism.
    private static readonly string[] FilterDescriptors =
    {
        "{\"filterType\":\"text\",\"type\":\"contains\",\"filter\":\"abc\"}",
        "{\"filterType\":\"text\",\"type\":\"notContains\",\"filter\":\"xyz\"}",
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
        from colId in Pick(Columns)
        from sort in Pick(SortDirections)
        select new AgGridSortModel { ColId = colId, Sort = sort };

    private static readonly Gen<Dictionary<string, JsonElement>> GenFilterModel =
        (from col in Gen.Int[0, Columns.Length - 1]
         from desc in Gen.Int[0, FilterDescriptors.Length - 1]
         select (col, desc)).List[0, 5]
        .Select(entries =>
        {
            var model = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (col, desc) in entries)
            {
                // Duplicate columns overwrite (a filterModel maps each colId once), matching the wire shape.
                model[Columns[col]] = Parse(FilterDescriptors[desc]);
            }

            return model;
        });

    private static readonly Gen<BindCase> GenCase =
        from start in Gen.Int[0, 100_000]
        from length in Gen.Int[0, 500]
        from sortModel in GenSort.List[0, 4]
        from filterModel in GenFilterModel
        from includeSort in Gen.Bool
        from includeFilter in Gen.Bool
        from quick in Pick(QuickFilterOptions)
        select BuildCase(start, start + length, sortModel, filterModel, includeSort, includeFilter, quick);

    /// <summary>
    /// Serializes an intended request to a JSON body, then (per the include flags) removes the
    /// <c>sortModel</c>/<c>filterModel</c> members to exercise absent-collection defaulting, and always
    /// removes the body-side <c>QuickFilter</c> (it is supplied out-of-band). Expected post-bind values
    /// account for the omissions.
    /// </summary>
    private static BindCase BuildCase(
        int startRow,
        int endRow,
        List<AgGridSortModel> sortModel,
        Dictionary<string, JsonElement> filterModel,
        bool includeSort,
        bool includeFilter,
        string? quick)
    {
        var intended = new AgGridRowsRequest
        {
            StartRow = startRow,
            EndRow = endRow,
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

        var body = node.ToJsonString();

        var values = quick is null
            ? NoValues
            : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["q"] = new[] { quick } };

        return new BindCase(
            JsonBody: body,
            Values: values,
            ExpectedStartRow: startRow,
            ExpectedEndRow: endRow,
            ExpectedSort: includeSort ? sortModel : Array.Empty<AgGridSortModel>(),
            ExpectedFilter: includeFilter
                ? filterModel
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            ExpectedQuickFilter: quick ?? string.Empty);
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
