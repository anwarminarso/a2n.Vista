// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters.AgGrid;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter, Property 8: JSON source-gen round-trip (valid LoadSuccessParams).
/// <para>
/// Because (de)serialization is a parse/print pair, a round-trip property is mandatory: serializing and
/// deserializing any <see cref="AgGridRowsRequest"/> / <see cref="AgGridRowsResponse"/> through the
/// source-generated <see cref="AgGridJsonContext"/> must succeed and preserve values, and a serialized
/// <see cref="AgGridRowsResponse"/> must always carry both <c>rowData</c> and <c>rowCount</c> — proving the
/// source-generated context covers every request/response POCO with <b>no reflection-based
/// <c>JsonSerializer.Deserialize</c></b> (R2.2), and that the response is valid AG Grid
/// <c>LoadSuccessParams</c> (R8.6).
/// </para>
/// <para>
/// <b>Value-preservation by re-serialization.</b> The request carries <c>filterModel</c> values as raw
/// <see cref="JsonElement"/> and the response carries <c>rowData</c> as <c>object?</c> rows (which
/// deserialize back to <see cref="JsonElement"/>), neither of which has structural equality. Equivalence is
/// therefore proven by re-serializing the round-tripped value through the <b>same</b> generated context and
/// asserting byte-equality with the first serialization (a deterministic parse/print pair), alongside
/// explicit field-by-field checks for the strongly-typed members.
/// </para>
/// </summary>
public sealed class AgGridJsonContextRoundTripPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    // Feature: ag-grid-adapter, Property 8: JSON source-gen round-trip (valid LoadSuccessParams).
    //
    // Validates: Requirements 2.2, 8.6
    [Test]
    public void Request_And_Response_Round_Trip_Through_The_Source_Generated_Context()
    {
        // Feature: ag-grid-adapter, Property 8: JSON source-gen round-trip (valid LoadSuccessParams).
        var genCase =
            from request in GenRequest
            from response in GenResponse
            select (request, response);

        genCase.Sample(
            tuple =>
            {
                AssertRequestRoundTrip(tuple.request);
                AssertResponseRoundTrip(tuple.response);
            },
            iter: Iterations);
    }

    // -- Request round-trip -----------------------------------------------------------------------------

    /// <summary>
    /// Serializes the request through <see cref="AgGridJsonContext"/>, deserializes it back through the
    /// same context, and asserts the strongly-typed members are reproduced, the <c>filterModel</c> entries
    /// are value-preserved (by canonical JSON), and re-serialization is byte-identical (no field lost).
    /// </summary>
    private static void AssertRequestRoundTrip(AgGridRowsRequest original)
    {
        var json = JsonSerializer.Serialize(original, AgGridJsonContext.Default.AgGridRowsRequest);

        var roundTripped = JsonSerializer.Deserialize(json, AgGridJsonContext.Default.AgGridRowsRequest);
        if (roundTripped is null)
        {
            throw new Exception("Deserializing the AgGridRowsRequest through the generated context returned null.");
        }

        if (roundTripped.StartRow != original.StartRow || roundTripped.EndRow != original.EndRow)
        {
            throw new Exception(
                $"Paging fields not preserved: startRow {original.StartRow}->{roundTripped.StartRow}, " +
                $"endRow {original.EndRow}->{roundTripped.EndRow}.");
        }

        if (!string.Equals(roundTripped.QuickFilter, original.QuickFilter, StringComparison.Ordinal))
        {
            throw new Exception(
                $"QuickFilter not preserved: '{original.QuickFilter}' -> '{roundTripped.QuickFilter}'.");
        }

        if (roundTripped.SortModel.Count != original.SortModel.Count)
        {
            throw new Exception(
                $"SortModel count not preserved: {original.SortModel.Count} -> {roundTripped.SortModel.Count}.");
        }

        for (var i = 0; i < original.SortModel.Count; i++)
        {
            var expected = original.SortModel[i];
            var actual = roundTripped.SortModel[i];
            if (!string.Equals(expected.ColId, actual.ColId, StringComparison.Ordinal)
                || !string.Equals(expected.Sort, actual.Sort, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"SortModel[{i}] not preserved: ({expected.ColId},{expected.Sort}) -> " +
                    $"({actual.ColId},{actual.Sort}).");
            }
        }

        if (roundTripped.FilterModel.Count != original.FilterModel.Count)
        {
            throw new Exception(
                $"FilterModel count not preserved: {original.FilterModel.Count} -> " +
                $"{roundTripped.FilterModel.Count}.");
        }

        foreach (var (key, expectedElement) in original.FilterModel)
        {
            if (!roundTripped.FilterModel.TryGetValue(key, out var actualElement))
            {
                throw new Exception($"FilterModel key '{key}' missing after round-trip.");
            }

            var expectedJson = Canonical(expectedElement);
            var actualJson = Canonical(actualElement);
            if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"FilterModel['{key}'] descriptor not preserved:\n  before: {expectedJson}\n  after:  {actualJson}");
            }
        }

        // Whole-object parse/print equivalence: the generated context is deterministic and lossless, so
        // re-serializing the round-tripped value reproduces the exact JSON (proving no field was dropped).
        var reserialized = JsonSerializer.Serialize(roundTripped, AgGridJsonContext.Default.AgGridRowsRequest);
        if (!string.Equals(reserialized, json, StringComparison.Ordinal))
        {
            throw new Exception(
                $"AgGridRowsRequest round-trip was not byte-stable:\n  first:  {json}\n  second: {reserialized}");
        }
    }

    // -- Response round-trip ----------------------------------------------------------------------------

    /// <summary>
    /// Serializes the response through <see cref="AgGridJsonContext"/> and asserts the emitted JSON is valid
    /// AG Grid <c>LoadSuccessParams</c> — it always carries both a <c>rowData</c> array and a numeric
    /// <c>rowCount</c> (matched case-insensitively, since the context's naming is not asserted here) — then
    /// deserializes it back and asserts value preservation via re-serialization byte-equality.
    /// </summary>
    private static void AssertResponseRoundTrip(AgGridRowsResponse original)
    {
        var json = JsonSerializer.Serialize(original, AgGridJsonContext.Default.AgGridRowsResponse);

        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new Exception($"Serialized AgGridRowsResponse is not a JSON object: {json}");
            }

            if (!TryGetPropertyIgnoreCase(root, "rowData", out var rowData))
            {
                throw new Exception($"Serialized AgGridRowsResponse is missing 'rowData': {json}");
            }

            if (rowData.ValueKind != JsonValueKind.Array)
            {
                throw new Exception($"'rowData' is not a JSON array (was {rowData.ValueKind}): {json}");
            }

            if (rowData.GetArrayLength() != original.RowData.Count)
            {
                throw new Exception(
                    $"'rowData' length not preserved: {original.RowData.Count} -> {rowData.GetArrayLength()}.");
            }

            if (!TryGetPropertyIgnoreCase(root, "rowCount", out var rowCount))
            {
                throw new Exception($"Serialized AgGridRowsResponse is missing 'rowCount': {json}");
            }

            if (rowCount.ValueKind != JsonValueKind.Number || rowCount.GetInt64() != original.RowCount)
            {
                throw new Exception(
                    $"'rowCount' not preserved as a number: expected {original.RowCount}, got {rowCount} ({json}).");
            }
        }

        var roundTripped = JsonSerializer.Deserialize(json, AgGridJsonContext.Default.AgGridRowsResponse);
        if (roundTripped is null)
        {
            throw new Exception("Deserializing the AgGridRowsResponse through the generated context returned null.");
        }

        if (roundTripped.RowCount != original.RowCount)
        {
            throw new Exception($"RowCount not preserved: {original.RowCount} -> {roundTripped.RowCount}.");
        }

        if (roundTripped.RowData.Count != original.RowData.Count)
        {
            throw new Exception(
                $"RowData count not preserved: {original.RowData.Count} -> {roundTripped.RowData.Count}.");
        }

        var reserialized = JsonSerializer.Serialize(roundTripped, AgGridJsonContext.Default.AgGridRowsResponse);
        if (!string.Equals(reserialized, json, StringComparison.Ordinal))
        {
            throw new Exception(
                $"AgGridRowsResponse round-trip was not byte-stable:\n  first:  {json}\n  second: {reserialized}");
        }
    }

    // -- Helpers ----------------------------------------------------------------------------------------

    /// <summary>Canonical (compact, resolver-independent) JSON text for a <see cref="JsonElement"/>.</summary>
    private static string Canonical(JsonElement element) => JsonSerializer.Serialize(element);

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static Gen<T> Pick<T>(IReadOnlyList<T> values) =>
        Gen.Int[0, values.Count - 1].Select(i => values[i]);

    // -- Generators -------------------------------------------------------------------------------------

    private static readonly string[] Columns = { "Id", "Name", "Price", "Category", "CreatedOn" };

    private static readonly string[] SortDirections = { "asc", "desc", "ASC", "DESC", "unknown", "" };

    private static readonly string[] QuickFilters = { "", "abc", "Widget 1", "naïve café", "a\"b\"c" };

    // Representative AG Grid per-column filter descriptors (text/number/date/set/combined). The round-trip
    // holds for any JsonElement; these mirror the D134 wire shapes for realism.
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

    // Representative row payloads for the response (object? rows deserialize back to JsonElement).
    private static readonly string[] RowPayloads =
    {
        "{\"Id\":1,\"Name\":\"Widget 1\",\"Price\":10}",
        "{\"Id\":2,\"Name\":\"Widget 2\",\"Price\":20,\"Category\":\"Tools\"}",
        "{\"Id\":3,\"Name\":null,\"Price\":0}",
        "42",
        "\"scalar-row\"",
        "true",
    };

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

    private static readonly Gen<AgGridRowsRequest> GenRequest =
        from start in Gen.Int[0, 100_000]
        from length in Gen.Int[0, 500]
        from sortModel in GenSort.List[0, 4]
        from filterModel in GenFilterModel
        from quickFilter in Pick(QuickFilters)
        select new AgGridRowsRequest
        {
            StartRow = start,
            EndRow = start + length,
            SortModel = sortModel,
            FilterModel = filterModel,
            QuickFilter = quickFilter,
        };

    private static readonly Gen<AgGridRowsResponse> GenResponse =
        from payloads in Gen.Int[0, RowPayloads.Length - 1].List[0, 6]
        from rowCount in Gen.Long[0, int.MaxValue]
        select new AgGridRowsResponse
        {
            RowData = payloads.ConvertAll(i => (object?)Parse(RowPayloads[i])),
            RowCount = rowCount,
        };
}
