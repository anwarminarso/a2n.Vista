using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Adapters.DataTablesNet;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for the jQuery-QueryBuilder metadata-schema emitter (Decision Log D116): DynData-compatible
/// shape, filters limited to filterable fields, and operator mapping.
/// </summary>
public sealed class QueryBuilderSchemaTests
{
    [Test]
    public async Task BuildSchema_Emits_DynData_Compatible_Shape()
    {
        var schema = (IReadOnlyDictionary<string, object?>)new QueryBuilderSchemaAdapter()
            .BuildSchema(WidgetTestHarness.BuildView());

        await Assert.That(schema["viewName"]).IsEqualTo("Widgets");
        await Assert.That(schema.ContainsKey("metaData")).IsTrue();
        await Assert.That(schema.ContainsKey("queryBuilderOptions")).IsTrue();

        var metaData = (List<object?>)schema["metaData"]!;
        // Widgets view has three non-hidden fields: Id, Name, Price.
        await Assert.That(metaData.Count).IsEqualTo(3);

        var first = (IReadOnlyDictionary<string, object?>)metaData[0]!;
        await Assert.That(first.ContainsKey("FieldName")).IsTrue();
        await Assert.That(first.ContainsKey("IsPrimaryKey")).IsTrue();
    }

    [Test]
    public async Task BuildSchema_Filters_Expose_String_Operators()
    {
        var schema = (IReadOnlyDictionary<string, object?>)new QueryBuilderSchemaAdapter()
            .BuildSchema(WidgetTestHarness.BuildView());

        var options = (IReadOnlyDictionary<string, object?>)schema["queryBuilderOptions"]!;
        var filters = (List<object?>)options["filters"]!;

        var nameFilter = filters
            .Cast<IReadOnlyDictionary<string, object?>>()
            .Single(f => (string)f["id"]! == "Name");

        var operators = (List<string>)nameFilter["operators"]!;
        await Assert.That(operators).Contains("contains");   // Name is a searchable string with Text operators
        await Assert.That((string)nameFilter["type"]!).IsEqualTo("string");
    }
}
