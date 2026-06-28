using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Contracts;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for the http-surface-redesign spec (Decision Log D110): the polymorphic
/// <see cref="FilterNodeJsonConverter"/>, the <see cref="VistaKeyReader"/>, global-search merging, the
/// metadata response DTO, and the Metadata facet glue.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests exercise the runtime reflection path by design.")]
public sealed class HttpSurfaceTests
{
    [Test]
    public async Task FilterNode_Json_Roundtrips_Polymorphic_Tree()
    {
        const string json = """
        {"and":[
            {"field":"Name","op":"Contains","value":"abc"},
            {"or":[
                {"field":"Id","op":"In","value":[1,2,3]},
                {"not":{"field":"Price","op":"GreaterThan","value":10}}
            ]}
        ]}
        """;

        var node = JsonSerializer.Deserialize<FilterNode>(json, VistaJson.Options);

        var and = node as FilterAnd;
        await Assert.That(and).IsNotNull();
        await Assert.That(and!.Children.Count).IsEqualTo(2);

        var leaf = and.Children[0] as FilterLeaf;
        await Assert.That(leaf!.Field).IsEqualTo("Name");
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(leaf.Value as string).IsEqualTo("abc");

        var or = and.Children[1] as FilterOr;
        await Assert.That(or!.Children.Count).IsEqualTo(2);
        var inLeaf = or.Children[0] as FilterLeaf;
        await Assert.That((inLeaf!.Value as System.Collections.IEnumerable)!.Cast<object>().Count()).IsEqualTo(3);
        await Assert.That(or.Children[1] is FilterNot).IsTrue();
    }

    [Test]
    public async Task KeyReader_Reads_Scalar_And_Object()
    {
        using var scalarDoc = JsonDocument.Parse("5");
        await Assert.That(System.Convert.ToInt64(VistaKeyReader.Read(scalarDoc.RootElement))).IsEqualTo(5L);

        using var objDoc = JsonDocument.Parse("""{"OrderId":10248,"ProductId":11}""");
        var map = (IReadOnlyDictionary<string, object?>)VistaKeyReader.Read(objDoc.RootElement);
        await Assert.That(System.Convert.ToInt64(map["OrderId"])).IsEqualTo(10248L);
        await Assert.That(System.Convert.ToInt64(map["ProductId"])).IsEqualTo(11L);
    }
}

[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests exercise the runtime reflection path by design.")]
public sealed class HttpSurfaceGlueTests
{
    [Test]
    public async Task SearchMerge_Builds_Contains_Over_Searchable_String_Fields()
    {
        var view = WidgetTestHarness.BuildView();
        var request = new ViewQueryRequest(Filter: null, Sort: System.Array.Empty<SortSpec>(), Page: 0, PageSize: 10);

        var merged = VistaSearchMerge.Apply(view, request, "5");

        // Only the string field (Name) is searchable → a single Contains leaf, now placed in the
        // Search slot (Decision Log D111), not folded into Filter.
        await Assert.That(merged.Filter).IsNull();
        var leaf = merged.Search as FilterLeaf;
        await Assert.That(leaf).IsNotNull();
        await Assert.That(leaf!.Field).IsEqualTo(nameof(WidgetRow.Name));
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.Contains);
    }

    [Test]
    public async Task SearchMerge_Empty_Search_Is_NoOp()
    {
        var view = WidgetTestHarness.BuildView();
        var request = new ViewQueryRequest(Filter: null, Sort: System.Array.Empty<SortSpec>(), Page: 0, PageSize: 10);

        var merged = VistaSearchMerge.Apply(view, request, "   ");

        await Assert.That(merged.Search).IsNull();
    }

    [Test]
    public async Task MetadataResponse_Projects_KeyFields_And_Limits()
    {
        var response = VistaMetadataResponse.From(WidgetTestHarness.BuildView());

        await Assert.That(response.Name).IsEqualTo("Widgets");
        await Assert.That(response.KeyFields).IsEquivalentTo(new[] { nameof(WidgetRow.Id) });
        await Assert.That(response.Fields.Any(f => f.Name == nameof(WidgetRow.Name))).IsTrue();
    }

    [Test]
    public async Task MetadataAsync_Returns_View_Metadata()
    {
        using var harness = WidgetTestHarness.Create();
        var (glue, http) = BuildGlue(harness);

        var response = await glue.MetadataAsync(http, "Widgets");

        await Assert.That(response.Name).IsEqualTo("Widgets");
        await Assert.That(response.KeyFields).IsEquivalentTo(new[] { nameof(WidgetRow.Id) });
    }

    private static (ViewRequestExecutor Glue, HttpContext Http) BuildGlue(WidgetTestHarness harness)
    {
        var registry = new ViewRegistry();
        registry.Add(WidgetTestHarness.BuildView());

        var services = new ServiceCollection();
        services.AddSingleton<IViewRegistry>(registry);
        services.AddSingleton<IViewExecutor>(harness.Executor);
        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };

        return (new ViewRequestExecutor(registry), http);
    }
}
