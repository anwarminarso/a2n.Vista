// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Unit tests for the presence-discriminated <c>FilterNode</c> modeling step (task 7.3; Requirements 2.2,
/// 2.3, and the 2.7-intent required-family behaviour). They assert the builder binds the family faithfully
/// from the real M18 document shape (the valid fixture), preserves document order for the union members and
/// the operator literals, threads the recursive by-name edges, and aborts with a fatal
/// <see cref="GenerationError.MissingSchema"/> when the union or a variant is absent.
/// </summary>
public sealed class FilterNodeModelBuilderTests
{
    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    // The FilterOperator literals in the exact document order M18 emits them (FilterLeaf.op enum).
    private static readonly string[] ExpectedOperatorOrder =
    {
        "Equals", "NotEquals", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual",
        "Contains", "StartsWith", "EndsWith", "In", "Between", "IsNull",
    };

    private static ResolvedDocument ResolveFixture()
    {
        var raw = File.ReadAllText(Path.Combine(FixturesDirectory, "valid-vista-document.json"));

        var parsed = OpenApiParser.Parse(raw);
        if (parsed.IsError)
        {
            throw new Exception($"Fixture failed to parse: {parsed.Error.Message}");
        }

        var resolved = RefResolver.Resolve(parsed.Value);
        if (resolved.IsError)
        {
            throw new Exception($"Fixture failed to resolve: {resolved.Error.Message}");
        }

        return resolved.Value;
    }

    private static FilterVariant Variant(FilterNodeModel model, string name) =>
        model.Members.Single(member => member.Name == name);

    private static string RenderedProperty(FilterVariant variant, string propertyName) =>
        variant.Properties.Single(property => property.Name == propertyName).Render();

    [Test]
    public async Task Binds_The_Union_Member_Type_Names_In_Document_Order()
    {
        var model = new FilterNodeModelBuilder().Build(ResolveFixture(), new NoticeCollector());

        await Assert.That(model.IsOk).IsTrue();
        await Assert.That(model.Value.UnionName).IsEqualTo("FilterNode");
        await Assert.That(model.Value.MemberTypeNames.ToArray())
            .IsEquivalentTo(new[] { "FilterLeaf", "FilterAnd", "FilterOr", "FilterNot" });
    }

    [Test]
    public async Task Derives_The_FilterOperator_Literal_Union_In_Document_Order()
    {
        var model = new FilterNodeModelBuilder().Build(ResolveFixture(), new NoticeCollector()).Value;

        await Assert.That(model.OperatorUnionName).IsEqualTo("FilterOperator");
        await Assert.That(model.OperatorLiterals.ToArray()).IsEquivalentTo(ExpectedOperatorOrder);

        // The rendered literal union preserves the document order verbatim (Requirement 3.2).
        var expectedRender = string.Join(" | ", ExpectedOperatorOrder.Select(name => $"\"{name}\""));
        await Assert.That(model.OperatorUnion.Render()).IsEqualTo(expectedRender);
    }

    [Test]
    public async Task Models_The_Leaf_Members_With_The_Named_Operator_And_Permissive_Value()
    {
        var notices = new NoticeCollector();
        var model = new FilterNodeModelBuilder().Build(ResolveFixture(), notices).Value;

        var leaf = Variant(model, "FilterLeaf");

        // field: string; op: FilterOperator; value?: unknown | null; (members sorted ordinally by name).
        await Assert.That(RenderedProperty(leaf, "field")).IsEqualTo("field: string;");
        await Assert.That(RenderedProperty(leaf, "op")).IsEqualTo("op: FilterOperator;");
        await Assert.That(RenderedProperty(leaf, "value")).IsEqualTo("value?: unknown | null;");

        // The permissive, typeless `value` member records a non-fatal degradation notice.
        await Assert.That(notices.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Threads_The_Recursive_FilterNode_Edges_By_Name()
    {
        var model = new FilterNodeModelBuilder().Build(ResolveFixture(), new NoticeCollector()).Value;

        // and/or are arrays of the union; not is a single union value — all by-name FilterNode edges.
        await Assert.That(RenderedProperty(Variant(model, "FilterAnd"), "and")).IsEqualTo("and: FilterNode[];");
        await Assert.That(RenderedProperty(Variant(model, "FilterOr"), "or")).IsEqualTo("or: FilterNode[];");
        await Assert.That(RenderedProperty(Variant(model, "FilterNot"), "not")).IsEqualTo("not: FilterNode;");

        await Assert.That(model.UnionReference.Render()).IsEqualTo("FilterNode");
    }

    [Test]
    public async Task Missing_FilterNode_Union_Aborts_With_MissingSchema_Naming_It()
    {
        // A resolved document with no FilterNode at all.
        var resolved = ResolvedFrom(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["SomethingElse"] = Scalar("string"),
        });

        var result = new FilterNodeModelBuilder().Build(resolved, new NoticeCollector());

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error is GenerationError.MissingSchema { SchemaName: "FilterNode" }).IsTrue();
    }

    [Test]
    public async Task Missing_Variant_Aborts_With_MissingSchema_Naming_The_Variant()
    {
        // A FilterNode union whose FilterAnd variant is absent from the resolved schema graph. (In the full
        // pipeline the resolve stage would flag this as a dangling ref first; the builder still guards it,
        // so this exercises the builder's defensive MissingSchema path directly.)
        var resolved = ResolvedFrom(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["FilterNode"] = OneOf(new[] { Ref("FilterLeaf"), Ref("FilterAnd") }),
            ["FilterLeaf"] = LeafSchema(),
            // FilterAnd intentionally omitted.
        });

        var result = new FilterNodeModelBuilder().Build(resolved, new NoticeCollector());

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error is GenerationError.MissingSchema { SchemaName: "FilterAnd" }).IsTrue();
    }

    // ---- Minimal model construction helpers (mirror the resolve-stage test style) ----

    // Builds a ResolvedDocument directly from a schema graph, bypassing the resolve stage so the builder's
    // own binding guards can be exercised in isolation.
    private static ResolvedDocument ResolvedFrom(IReadOnlyDictionary<string, OpenApiSchema> schemas) =>
        new(
            DocumentWithSchemas(schemas),
            schemas,
            new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal));

    private static OpenApiSchema Ref(string name) =>
        new("#/components/schemas/" + name, null, null, false, Array.Empty<string>(), null, null, null, null, false);

    private static OpenApiSchema Scalar(string type) =>
        new(null, type, null, false, Array.Empty<string>(), null, null, null, null, false);

    private static OpenApiSchema OneOf(IReadOnlyList<OpenApiSchema> variants) =>
        new(null, null, null, false, Array.Empty<string>(), null, null, variants, null, false);

    private static OpenApiSchema LeafSchema()
    {
        var op = new OpenApiSchema(
            null, "string", null, false, Array.Empty<string>(), null, null, null,
            new[] { "Equals", "NotEquals" }, false);

        var properties = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["field"] = Scalar("string"),
            ["op"] = op,
        };

        return new OpenApiSchema(
            null, "object", null, false, new[] { "field", "op" }, properties, null, null, null, false);
    }

    private static OpenApiDocument DocumentWithSchemas(IReadOnlyDictionary<string, OpenApiSchema> schemas) =>
        new(
            "3.0.4",
            new OpenApiInfo("a2n.Vista API", "1.0.0"),
            new Dictionary<string, OpenApiPathItem>(),
            new OpenApiComponents(schemas, new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal)),
            Array.Empty<OpenApiSecurityRequirement>());
}
