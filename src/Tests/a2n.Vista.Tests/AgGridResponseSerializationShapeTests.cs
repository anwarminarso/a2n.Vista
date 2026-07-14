using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.AspNetCore.Serialization;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter (task 1.4, R8.6) — response serialization shape.
/// <para>
/// R8.6 has two clauses that map to the two serialization paths the design defines:
/// </para>
/// <list type="bullet">
///   <item><description><b>Clause (b) — valid AG Grid <c>LoadSuccessParams</c> (camelCase
///   <c>rowData</c>/<c>rowCount</c>).</b> Per R5.4 the response is emitted through the same
///   host-serializer path as the DataTables adapter (<see cref="VistaJson.Options"/>, Web defaults),
///   which is the authority for the deterministic camelCase wire names. This test asserts that shape
///   at the unit level (no HTTP), complementing the end-to-end HTTP assertion in
///   <see cref="AgGridEndpointIntegrationTests"/>.</description></item>
///   <item><description><b>Clause (a) — serializable via the source-gen context without error.</b>
///   The <see cref="AgGridJsonContext"/> covers the <see cref="AgGridRowsResponse"/> envelope so the
///   response serializes AOT-clean (no reflection-based <c>Deserialize</c>). Note the source-gen
///   context uses the declared (PascalCase) property names; the camelCase wire shape is the host
///   path's responsibility (R5.4), and the row items themselves ride the documented
///   <c>[RequiresUnreferencedCode]</c> row-type path (D96/R5.3), not the context.</description></item>
/// </list>
/// </summary>
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Exercises the reflection-based host serializer path (VistaJson.Options) by design (R5.4/D96).")]
[UnconditionalSuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with RequiresDynamicCodeAttribute may break functionality when AOT compiling",
    Justification = "Exercises the reflection-based host serializer path (VistaJson.Options) by design (R5.4/D96).")]
public sealed class AgGridResponseSerializationShapeTests
{
    /// <summary>
    /// R5.4/R8.6(b): a representative response serialized through the host serializer path
    /// (<see cref="VistaJson.Options"/>) yields the deterministic camelCase <c>{ rowData, rowCount }</c>
    /// shape an AG Grid server-side row model expects — both members present, exact camelCase names,
    /// no PascalCase leakage, and the correct values.
    /// </summary>
    [Test]
    public async Task Response_Serializes_To_CamelCase_RowData_And_RowCount()
    {
        var response = new AgGridRowsResponse
        {
            RowData = new object?[]
            {
                new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Widget 1" },
                new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "Widget 2" },
            },
            RowCount = 42,
        };

        var json = JsonSerializer.Serialize(response, VistaJson.Options);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // camelCase LoadSuccessParams members are both present (R8.6), exactly as the wire shape.
        await Assert.That(root.TryGetProperty("rowData", out var rowData)).IsTrue();
        await Assert.That(root.TryGetProperty("rowCount", out var rowCount)).IsTrue();

        await Assert.That(rowData.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(rowData.GetArrayLength()).IsEqualTo(2);
        await Assert.That(rowCount.GetInt64()).IsEqualTo(42L);

        // No PascalCase leakage — the emitted top-level names are exactly the camelCase pair.
        var names = new List<string>();
        foreach (var member in root.EnumerateObject())
        {
            names.Add(member.Name);
        }

        await Assert.That(names.Count).IsEqualTo(2);
        await Assert.That(names).Contains("rowData");
        await Assert.That(names).Contains("rowCount");
    }

    /// <summary>
    /// R5.5: an empty result still serializes (host path) to the valid <c>LoadSuccessParams</c> shape —
    /// an empty <c>rowData</c> array with a <c>rowCount</c> (which may be <c>0</c>) — so AG Grid
    /// last-block detection works at any offset.
    /// </summary>
    [Test]
    public async Task Empty_Response_Serializes_To_Empty_RowData_With_RowCount()
    {
        var response = new AgGridRowsResponse
        {
            RowData = Array.Empty<object?>(),
            RowCount = 0,
        };

        var json = JsonSerializer.Serialize(response, VistaJson.Options);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        await Assert.That(root.TryGetProperty("rowData", out var rowData)).IsTrue();
        await Assert.That(root.TryGetProperty("rowCount", out var rowCount)).IsTrue();
        await Assert.That(rowData.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(rowData.GetArrayLength()).IsEqualTo(0);
        await Assert.That(rowCount.GetInt64()).IsEqualTo(0L);
    }

    /// <summary>
    /// R8.6(a): the <see cref="AgGridRowsResponse"/> envelope is serializable through the
    /// source-generated <see cref="AgGridJsonContext"/> without error, carrying both
    /// <c>rowData</c>/<c>rowCount</c> members (matched case-insensitively — the source-gen context uses
    /// the declared property names; the camelCase wire shape is the host path's job per R5.4). Row items
    /// are exercised on the host/RUC path (D96/R5.3), so this envelope check uses an empty row set.
    /// </summary>
    [Test]
    public async Task Response_Envelope_Is_Serializable_Via_SourceGen_Context()
    {
        var response = new AgGridRowsResponse
        {
            RowData = Array.Empty<object?>(),
            RowCount = 7,
        };

        // Must not throw: the context covers AgGridRowsResponse (AOT-clean, no reflection-based Deserialize).
        var json = JsonSerializer.Serialize(response, AgGridJsonContext.Default.AgGridRowsResponse);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var hasRowData = false;
        var hasRowCount = false;
        foreach (var member in root.EnumerateObject())
        {
            if (string.Equals(member.Name, "rowData", StringComparison.OrdinalIgnoreCase))
            {
                hasRowData = true;
            }
            else if (string.Equals(member.Name, "rowCount", StringComparison.OrdinalIgnoreCase))
            {
                hasRowCount = true;
            }
        }

        await Assert.That(hasRowData).IsTrue();
        await Assert.That(hasRowCount).IsTrue();
    }
}
