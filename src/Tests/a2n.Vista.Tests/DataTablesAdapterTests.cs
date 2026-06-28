using System;
using System.Collections.Generic;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.DataTablesNet;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for the DataTables.NET adapter (Spec 04 §7, Decision Log D111/D112): pure
/// <c>BindRequest</c>/<c>ToQuery</c>/<c>ToResponse</c> mapping, the three filter channels, and the
/// QueryBuilder/externalFilter parsers. No HTTP or EF.
/// </summary>
public sealed class DataTablesAdapterTests
{
    private static readonly DataTablesAdapter Adapter = new();

    private static ViewMetadata View() => WidgetTestHarness.BuildView();

    private static AdapterRequest Raw(Dictionary<string, IReadOnlyList<string>> values) =>
        new("Widgets", values, JsonBody: null);

    [Test]
    public async Task BindRequest_Parses_Bracket_Keys()
    {
        var query = Adapter.BindRequest(Raw(new(StringComparer.OrdinalIgnoreCase)
        {
            ["draw"] = new[] { "3" },
            ["start"] = new[] { "10" },
            ["length"] = new[] { "5" },
            ["search[value]"] = new[] { "abc" },
            ["columns[0][data]"] = new[] { "Name" },
            ["columns[0][orderable]"] = new[] { "true" },
            ["columns[1][data]"] = new[] { "Price" },
            ["order[0][column]"] = new[] { "0" },
            ["order[0][dir]"] = new[] { "desc" },
        }));

        await Assert.That(query.Draw).IsEqualTo(3);
        await Assert.That(query.Start).IsEqualTo(10);
        await Assert.That(query.Length).IsEqualTo(5);
        await Assert.That(query.Search.Value).IsEqualTo("abc");
        await Assert.That(query.Columns.Count).IsEqualTo(2);
        await Assert.That(query.Order.Count).IsEqualTo(1);
        await Assert.That(query.Order[0].Dir).IsEqualTo("desc");
    }

    [Test]
    public async Task BindRequest_NonInteger_Length_Throws()
    {
        await Assert.That(Capture(() => Adapter.BindRequest(Raw(new(StringComparer.OrdinalIgnoreCase)
        {
            ["length"] = new[] { "not-a-number" },
        })))).IsNotNull();
    }

    [Test]
    public async Task ToQuery_Builds_Three_Channels()
    {
        var query = new DataTablesQuery
        {
            Draw = 1,
            Start = 0,
            Length = 10,
            Search = new DtSearch { Value = "wid" },
            JsonQB = "{\"condition\":\"AND\",\"rules\":[{\"field\":\"Price\",\"operator\":\"greater_or_equal\",\"value\":20}]}",
            ExternalFilter = "{\"Id\":1}",
        };

        var request = Adapter.ToQuery(query, View());

        await Assert.That(request.Search).IsNotNull();   // global search → Search slot (Name is searchable)
        await Assert.That(request.Filter).IsNotNull();   // jsonQB → Filter slot
        await Assert.That(request.Scope).IsNotNull();    // externalFilter → Scope slot
        await Assert.That(request.PageSize).IsEqualTo(10);
        await Assert.That(request.Page).IsEqualTo(0);
    }

    [Test]
    public async Task ToQuery_Passes_Negative_Length_Through()
    {
        var query = new DataTablesQuery { Length = -1, Start = 0 };
        var request = Adapter.ToQuery(query, View());

        await Assert.That(request.PageSize).IsEqualTo(-1);
        await Assert.That(request.Page).IsEqualTo(0);
    }

    [Test]
    public async Task ToQuery_Skips_NonField_Columns_For_Sort()
    {
        var query = new DataTablesQuery
        {
            Length = 10,
            Columns = { new DtColumn { Data = string.Empty }, new DtColumn { Data = "Name" } },
            Order = { new DtOrder { Column = 0, Dir = "asc" } }, // column 0 is a non-field (Action) column
        };

        var request = Adapter.ToQuery(query, View());
        await Assert.That(request.Sort.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ToResponse_Echoes_Draw_And_Maps_Totals()
    {
        var query = new DataTablesQuery { Draw = 42 };
        var result = new AdapterListResult(new object?[] { new { Id = 1 } }, RecordsFiltered: 7, RecordsTotal: 25);

        var response = Adapter.ToResponse(result, query, View());

        await Assert.That(response.Draw).IsEqualTo(42);
        await Assert.That(response.RecordsFiltered).IsEqualTo(7L);
        await Assert.That(response.RecordsTotal).IsEqualTo(25L);
        await Assert.That(response.Data.Count).IsEqualTo(1);
    }

    [Test]
    public async Task QueryBuilder_IsEmpty_On_String_Builds_IsNull_Or_Empty()
    {
        var fields = BuildFieldLookup(View());
        var node = QueryBuilderParser.Parse(
            "{\"condition\":\"AND\",\"rules\":[{\"field\":\"Name\",\"operator\":\"is_empty\"}]}", fields);

        // FilterAnd([ FilterOr([ IsNull, Equals "" ]) ])
        var and = node as FilterAnd;
        await Assert.That(and).IsNotNull();
        var or = and!.Children[0] as FilterOr;
        await Assert.That(or).IsNotNull();
        await Assert.That(or!.Children.Count).IsEqualTo(2);
    }

    [Test]
    public async Task QueryBuilder_Unknown_Operator_Throws()
    {
        var fields = BuildFieldLookup(View());
        await Assert.That(Capture(() => QueryBuilderParser.Parse(
            "{\"condition\":\"AND\",\"rules\":[{\"field\":\"Name\",\"operator\":\"made_up\",\"value\":1}]}", fields))).IsNotNull();
    }

    [Test]
    public async Task ExternalFilter_Array_With_Operators_Builds_Range()
    {
        var node = ExternalFilterParser.Parse("{\"Price\":[\">=10\",\"<=100\"]}");

        var and = node as FilterAnd;
        await Assert.That(and).IsNotNull();
        await Assert.That(and!.Children.Count).IsEqualTo(2);
        await Assert.That(((FilterLeaf)and.Children[0]).Op).IsEqualTo(FilterOperator.GreaterThanOrEqual);
        await Assert.That(((FilterLeaf)and.Children[1]).Op).IsEqualTo(FilterOperator.LessThanOrEqual);
    }

    [Test]
    public async Task ExternalFilter_Wildcard_Builds_Contains()
    {
        var node = ExternalFilterParser.Parse("{\"Name\":\"%a%\"}");

        var leaf = node as FilterLeaf;
        await Assert.That(leaf).IsNotNull();
        await Assert.That(leaf!.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(leaf.Value).IsEqualTo("a");
    }

    private static Dictionary<string, FieldMetadata> BuildFieldLookup(ViewMetadata view)
    {
        var lookup = new Dictionary<string, FieldMetadata>(StringComparer.Ordinal);
        foreach (var field in view.Fields)
        {
            lookup[field.Name] = field;
        }

        return lookup;
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
