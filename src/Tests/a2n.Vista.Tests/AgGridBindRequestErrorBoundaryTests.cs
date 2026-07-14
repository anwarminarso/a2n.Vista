// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Collections.Generic;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.AgGrid;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter (task 3.3) — targeted example/edge-case coverage for
/// <see cref="AgGridAdapter.BindRequest"/> error and boundary conditions. These are plain (non-property)
/// unit tests over fixed inputs that complement the bind-fidelity property test (task 3.2).
/// <para>They assert the fail-loud and boundary guarantees the bind step must uphold:</para>
/// <list type="bullet">
///   <item><description>an absent/empty/whitespace-only JSON body →
///   <see cref="AdapterBindException"/> (R2.3).</description></item>
///   <item><description>malformed JSON, or a required field of the wrong JSON type, or an out-of-range
///   row range (<c>StartRow &lt; 0</c>, <c>EndRow &lt; StartRow</c>) → <see cref="AdapterBindException"/>
///   with no partial POCO (R2.4/R1.6).</description></item>
///   <item><description>the quick filter is read from <c>Values["q"]</c> and rejected past 1,024
///   characters — the 1024/1025 boundary (R2.5).</description></item>
///   <item><description>an Advanced-Filter payload (deferred for v1) →
///   <see cref="AdapterBindException"/> (R4.7).</description></item>
/// </list>
/// </summary>
public sealed class AgGridBindRequestErrorBoundaryTests
{
    private const string ViewName = "vTest";
    private const string QuickFilterKey = AgGridAdapter.QuickFilterKey; // "q"

    /// <summary>A syntactically valid, in-range AG Grid request body (block [0, 100)).</summary>
    private const string ValidBody = "{\"startRow\":0,\"endRow\":100}";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValues =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Builds an <see cref="AdapterRequest"/> with the given JSON body and no out-of-band values.</summary>
    private static AdapterRequest Raw(string? jsonBody) => new(ViewName, NoValues, jsonBody);

    /// <summary>Builds an <see cref="AdapterRequest"/> carrying a single <c>Values[key]</c> entry.</summary>
    private static AdapterRequest RawWithValue(string? jsonBody, string key, string value)
    {
        var values = new Dictionary<string, IReadOnlyList<string>>
        {
            [key] = new List<string> { value },
        };
        return new AdapterRequest(ViewName, values, jsonBody);
    }

    private static AgGridRowsRequest Bind(AdapterRequest raw) => new AgGridAdapter().BindRequest(raw);

    // -- absent / blank / whitespace body (R2.3) ----------------------------------------------------

    /// <summary>R2.3: an absent JSON body (<see langword="null"/>) fails loudly.</summary>
    [Test]
    public async Task Absent_Body_Throws()
    {
        await Assert.That(() => Bind(Raw(null))).Throws<AdapterBindException>();
    }

    /// <summary>R2.3: an empty JSON body fails loudly.</summary>
    [Test]
    public async Task Empty_Body_Throws()
    {
        await Assert.That(() => Bind(Raw(string.Empty))).Throws<AdapterBindException>();
    }

    /// <summary>R2.3: a whitespace-only JSON body fails loudly.</summary>
    [Test]
    public async Task Whitespace_Body_Throws()
    {
        await Assert.That(() => Bind(Raw("   \t\r\n  "))).Throws<AdapterBindException>();
    }

    // -- malformed JSON / wrong JSON type (R2.4) ----------------------------------------------------

    /// <summary>R2.4: a syntactically invalid JSON body fails loudly.</summary>
    [Test]
    public async Task Malformed_Json_Throws()
    {
        await Assert.That(() => Bind(Raw("{ this is not valid json"))).Throws<AdapterBindException>();
    }

    /// <summary>R2.4: a required numeric field of the wrong JSON type (<c>startRow</c> as string) fails loudly.</summary>
    [Test]
    public async Task Wrong_Type_StartRow_NonNumeric_Throws()
    {
        await Assert.That(() => Bind(Raw("{\"startRow\":\"abc\",\"endRow\":100}")))
            .Throws<AdapterBindException>();
    }

    /// <summary>R2.4: a field of the wrong JSON type (<c>sortModel</c> not an array) fails loudly.</summary>
    [Test]
    public async Task Wrong_Type_SortModel_NotArray_Throws()
    {
        await Assert.That(() => Bind(Raw("{\"startRow\":0,\"endRow\":100,\"sortModel\":5}")))
            .Throws<AdapterBindException>();
    }

    // -- out-of-range row bounds (R2.4) -------------------------------------------------------------

    /// <summary>R2.4: a negative <c>startRow</c> is out of range and fails loudly.</summary>
    [Test]
    public async Task Negative_StartRow_Throws()
    {
        await Assert.That(() => Bind(Raw("{\"startRow\":-1,\"endRow\":100}")))
            .Throws<AdapterBindException>();
    }

    /// <summary>R2.4: <c>endRow</c> less than <c>startRow</c> is out of range and fails loudly.</summary>
    [Test]
    public async Task EndRow_Less_Than_StartRow_Throws()
    {
        await Assert.That(() => Bind(Raw("{\"startRow\":100,\"endRow\":50}")))
            .Throws<AdapterBindException>();
    }

    /// <summary>R2.4: a degenerate-but-valid range (<c>endRow == startRow</c>) binds (the engine rejects the zero page size, not the bind).</summary>
    [Test]
    public async Task EndRow_Equal_To_StartRow_Binds()
    {
        var request = Bind(Raw("{\"startRow\":10,\"endRow\":10}"));

        await Assert.That(request.StartRow).IsEqualTo(10);
        await Assert.That(request.EndRow).IsEqualTo(10);
    }

    // -- quick filter read from Values["q"] + length boundary (R2.5) --------------------------------

    /// <summary>R2.5: the quick-filter text is read from <c>Values["q"]</c>.</summary>
    [Test]
    public async Task QuickFilter_Read_From_Values_Q()
    {
        var request = Bind(RawWithValue(ValidBody, QuickFilterKey, "widget"));

        await Assert.That(request.QuickFilter).IsEqualTo("widget");
    }

    /// <summary>R2.1/R2.5: an absent <c>q</c> binds the quick filter to empty (never null).</summary>
    [Test]
    public async Task QuickFilter_Absent_Binds_To_Empty()
    {
        var request = Bind(Raw(ValidBody));

        await Assert.That(request.QuickFilter).IsEqualTo(string.Empty);
    }

    /// <summary>R2.5: a quick filter of exactly 1,024 characters is accepted (the inclusive upper bound).</summary>
    [Test]
    public async Task QuickFilter_Length_1024_Is_Accepted()
    {
        var value = new string('x', 1024);

        var request = Bind(RawWithValue(ValidBody, QuickFilterKey, value));

        await Assert.That(request.QuickFilter.Length).IsEqualTo(1024);
    }

    /// <summary>R2.5: a quick filter of 1,025 characters exceeds the cap and fails loudly.</summary>
    [Test]
    public async Task QuickFilter_Length_1025_Throws()
    {
        var value = new string('x', 1025);

        await Assert.That(() => Bind(RawWithValue(ValidBody, QuickFilterKey, value)))
            .Throws<AdapterBindException>();
    }

    // -- Advanced Filter body (deferred for v1, R4.7) -----------------------------------------------

    /// <summary>R4.7: an Advanced-Filter column descriptor (<c>filterType:"advanced"</c>) in the body is rejected at bind time.</summary>
    [Test]
    public async Task Advanced_Filter_Body_Throws()
    {
        const string body =
            "{\"startRow\":0,\"endRow\":100,\"filterModel\":{" +
            "\"Name\":{\"filterType\":\"advanced\",\"type\":\"equals\",\"filter\":\"x\"}}}";

        await Assert.That(() => Bind(Raw(body))).Throws<AdapterBindException>();
    }

    /// <summary>R4.7: an Advanced-Filter join node (<c>type:"join"</c>) in the body is rejected at bind time.</summary>
    [Test]
    public async Task Advanced_Filter_Join_Body_Throws()
    {
        const string body =
            "{\"startRow\":0,\"endRow\":100,\"filterModel\":{" +
            "\"root\":{\"filterType\":\"join\",\"type\":\"AND\",\"conditions\":[]}}}";

        await Assert.That(() => Bind(Raw(body))).Throws<AdapterBindException>();
    }
}
