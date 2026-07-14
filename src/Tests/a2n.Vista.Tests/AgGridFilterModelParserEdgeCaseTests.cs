// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter (task 2.3) — targeted example/edge-case coverage for
/// <see cref="AgGridFilterModelParser"/> that complements the D134 fidelity property test (task 2.2).
/// <para>
/// These are plain (non-property) unit tests over fixed inputs asserting the fail-loud and boundary
/// behaviors the parser must guarantee (R1.6/R4.7 "never silently drop a filter"):
/// </para>
/// <list type="bullet">
///   <item><description><c>inRange</c> missing either bound → <see cref="AdapterBindException"/>
///   (R4.1).</description></item>
///   <item><description>an unknown <c>type</c> or an unknown <c>filterType</c> →
///   <see cref="AdapterBindException"/> (R4.1/R4.7).</description></item>
///   <item><description>an Advanced-Filter payload (deferred for v1) →
///   <see cref="AdapterBindException"/> (R4.7).</description></item>
///   <item><description>a <c>set</c> filter with empty <c>values</c> → <see cref="FilterOperator.In"/>
///   over the empty set (R4.2).</description></item>
///   <item><description><c>notContains</c>/<c>notBlank</c> compose via <see cref="FilterNot"/>
///   (R4.1).</description></item>
/// </list>
/// The parser is field-type-neutral (D134), so every case runs against an empty field map.
/// </summary>
public sealed class AgGridFilterModelParserEdgeCaseTests
{
    private static readonly IReadOnlyDictionary<string, FieldMetadata> NoFields =
        new Dictionary<string, FieldMetadata>();

    private const string Col = "Name";

    /// <summary>Builds a single-column <c>filterModel</c> map from a raw descriptor JSON string.</summary>
    private static IReadOnlyDictionary<string, JsonElement> Model(string descriptorJson, string colId = Col)
    {
        using var doc = JsonDocument.Parse(descriptorJson);
        return new Dictionary<string, JsonElement> { [colId] = doc.RootElement.Clone() };
    }

    // -- inRange: missing bounds --------------------------------------------------------------------

    /// <summary>R4.1: a number <c>inRange</c> missing its upper bound (<c>filterTo</c>) fails loudly.</summary>
    [Test]
    public async Task Number_InRange_Missing_To_Bound_Throws()
    {
        var model = Model("{\"filterType\":\"number\",\"type\":\"inRange\",\"filter\":10}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    /// <summary>R4.1: a number <c>inRange</c> missing its lower bound (<c>filter</c>) fails loudly.</summary>
    [Test]
    public async Task Number_InRange_Missing_From_Bound_Throws()
    {
        var model = Model("{\"filterType\":\"number\",\"type\":\"inRange\",\"filterTo\":100}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    /// <summary>R4.1: a date <c>inRange</c> missing its upper bound (<c>dateTo</c>) fails loudly.</summary>
    [Test]
    public async Task Date_InRange_Missing_To_Bound_Throws()
    {
        var model = Model("{\"filterType\":\"date\",\"type\":\"inRange\",\"dateFrom\":\"2020-01-01\"}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    /// <summary>R4.1: a valid <c>inRange</c> with both bounds maps to <see cref="FilterOperator.Between"/>.</summary>
    [Test]
    public async Task Number_InRange_With_Both_Bounds_Maps_To_Between()
    {
        var model = Model("{\"filterType\":\"number\",\"type\":\"inRange\",\"filter\":10,\"filterTo\":100}");

        var node = AgGridFilterModelParser.Parse(model, NoFields);

        var leaf = node as FilterLeaf;
        await Assert.That(leaf).IsNotNull();
        await Assert.That(leaf!.Field).IsEqualTo(Col);
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.Between);

        var bounds = leaf.Value as List<object?>;
        await Assert.That(bounds).IsNotNull();
        await Assert.That(bounds!.Count).IsEqualTo(2);
    }

    // -- unknown type / filterType ------------------------------------------------------------------

    /// <summary>R4.1: an unknown text <c>type</c> fails loudly (never silently dropped).</summary>
    [Test]
    public async Task Unknown_Text_Type_Throws()
    {
        var model = Model("{\"filterType\":\"text\",\"type\":\"soundsLike\",\"filter\":\"abc\"}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    /// <summary>R4.1: an unknown number <c>type</c> fails loudly.</summary>
    [Test]
    public async Task Unknown_Number_Type_Throws()
    {
        var model = Model("{\"filterType\":\"number\",\"type\":\"aboutEqual\",\"filter\":42}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    /// <summary>R4.7: an unknown <c>filterType</c> fails loudly.</summary>
    [Test]
    public async Task Unknown_FilterType_Throws()
    {
        var model = Model("{\"filterType\":\"geospatial\",\"type\":\"equals\",\"filter\":\"x\"}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    // -- Advanced Filter (deferred for v1) ----------------------------------------------------------

    /// <summary>R4.7: an explicit <c>filterType:"advanced"</c> payload is rejected (deferred for v1).</summary>
    [Test]
    public async Task Advanced_Filter_By_FilterType_Throws()
    {
        var model = Model("{\"filterType\":\"advanced\",\"type\":\"equals\",\"filter\":\"x\"}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    /// <summary>R4.7: an Advanced-Filter join node (<c>filterType:"join"</c>) is rejected.</summary>
    [Test]
    public async Task Advanced_Filter_Join_FilterType_Throws()
    {
        var model = Model(
            "{\"filterType\":\"join\",\"type\":\"AND\",\"conditions\":[" +
            "{\"filterType\":\"text\",\"colId\":\"Name\",\"type\":\"contains\",\"filter\":\"a\"}]}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    /// <summary>R4.7: an Advanced-Filter join node signalled via <c>type:"join"</c> is rejected.</summary>
    [Test]
    public async Task Advanced_Filter_Join_Type_Throws()
    {
        var model = Model("{\"type\":\"join\",\"filterModels\":[]}");

        await Assert.That(() => AgGridFilterModelParser.Parse(model, NoFields))
            .Throws<AdapterBindException>();
    }

    // -- set filter: empty values -------------------------------------------------------------------

    /// <summary>R4.2: a <c>set</c> filter with an empty <c>values</c> array maps to <c>In</c> over the empty set.</summary>
    [Test]
    public async Task Empty_Set_Values_Maps_To_In_Over_Empty_Set()
    {
        var model = Model("{\"filterType\":\"set\",\"values\":[]}");

        var node = AgGridFilterModelParser.Parse(model, NoFields);

        var leaf = node as FilterLeaf;
        await Assert.That(leaf).IsNotNull();
        await Assert.That(leaf!.Field).IsEqualTo(Col);
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.In);

        var values = leaf.Value as List<object?>;
        await Assert.That(values).IsNotNull();
        await Assert.That(values!.Count).IsEqualTo(0);
    }

    /// <summary>R4.2: a <c>set</c> filter with an absent <c>values</c> property also maps to <c>In</c> over the empty set.</summary>
    [Test]
    public async Task Set_Filter_Absent_Values_Maps_To_In_Over_Empty_Set()
    {
        var model = Model("{\"filterType\":\"set\"}");

        var node = AgGridFilterModelParser.Parse(model, NoFields);

        var leaf = node as FilterLeaf;
        await Assert.That(leaf).IsNotNull();
        await Assert.That(leaf!.Op).IsEqualTo(FilterOperator.In);

        var values = leaf.Value as List<object?>;
        await Assert.That(values).IsNotNull();
        await Assert.That(values!.Count).IsEqualTo(0);
    }

    // -- negations compose via FilterNot ------------------------------------------------------------

    /// <summary>R4.1: <c>notContains</c> composes as <see cref="FilterNot"/> over a <c>Contains</c> leaf.</summary>
    [Test]
    public async Task NotContains_Composes_Via_FilterNot_Over_Contains()
    {
        var model = Model("{\"filterType\":\"text\",\"type\":\"notContains\",\"filter\":\"xyz\"}");

        var node = AgGridFilterModelParser.Parse(model, NoFields);

        var not = node as FilterNot;
        await Assert.That(not).IsNotNull();

        var inner = not!.Child as FilterLeaf;
        await Assert.That(inner).IsNotNull();
        await Assert.That(inner!.Field).IsEqualTo(Col);
        await Assert.That(inner.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(inner.Value).IsEqualTo("xyz");
    }

    /// <summary>R4.1: <c>notBlank</c> composes as <see cref="FilterNot"/> over an <c>IsNull</c> leaf.</summary>
    [Test]
    public async Task NotBlank_Composes_Via_FilterNot_Over_IsNull()
    {
        var model = Model("{\"filterType\":\"text\",\"type\":\"notBlank\"}");

        var node = AgGridFilterModelParser.Parse(model, NoFields);

        var not = node as FilterNot;
        await Assert.That(not).IsNotNull();

        var inner = not!.Child as FilterLeaf;
        await Assert.That(inner).IsNotNull();
        await Assert.That(inner!.Field).IsEqualTo(Col);
        await Assert.That(inner.Op).IsEqualTo(FilterOperator.IsNull);
        await Assert.That(inner.Value).IsNull();
    }

    /// <summary>R4.1: a number <c>notBlank</c> also composes as <see cref="FilterNot"/> over <c>IsNull</c>.</summary>
    [Test]
    public async Task Number_NotBlank_Composes_Via_FilterNot_Over_IsNull()
    {
        var model = Model("{\"filterType\":\"number\",\"type\":\"notBlank\"}");

        var node = AgGridFilterModelParser.Parse(model, NoFields);

        var not = node as FilterNot;
        await Assert.That(not).IsNotNull();

        var inner = not!.Child as FilterLeaf;
        await Assert.That(inner).IsNotNull();
        await Assert.That(inner!.Op).IsEqualTo(FilterOperator.IsNull);
    }
}
