using System;
using System.Linq;
using a2n.Vista.Contracts;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.OpenApi.Schemas;
using a2n.Vista.OpenApi.Serialization;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Example coverage for the hand-authored polymorphic <c>FilterNode</c> schema descriptor (spec
/// openapi-emitter, task 2.2; Requirements 5.1, 5.2, 5.3, 5.4). These assert the <c>oneOf</c> of the four
/// variants, the presence-based discrimination the <c>FilterNodeJsonConverter</c> performs (each variant
/// requires its distinguishing wire property; no fabricated <c>discriminator</c>), the recursive
/// <c>FilterNode</c> <c>$ref</c> on <c>and</c>/<c>or</c>/<c>not</c>, and the <c>FilterLeaf</c> <c>op</c>
/// string enum matching the real <see cref="FilterOperator"/> single-operator member names. The
/// schema/wire-parity property test against real serialization lands with task 8.5.
/// </summary>
public sealed class OpenApiFilterNodeSchemaTests
{
    private const string FilterNodeRef = "#/components/schemas/FilterNode";

    private static OpenApiSchema PropertyOf(OpenApiSchema schema, string name)
    {
        if (schema.Properties is null || !schema.Properties.TryGetValue(name, out var property))
        {
            throw new KeyNotFoundException($"Schema has no property '{name}'.");
        }

        return property;
    }

    [Test]
    public async Task FilterNode_Is_OneOf_The_Four_Variants()
    {
        var node = FilterNodeSchema.FilterNode();

        await Assert.That(node.OneOf).IsNotNull();
        var refs = node.OneOf!.Select(s => s.Ref).ToArray();
        await Assert.That(refs).Contains("#/components/schemas/FilterLeaf");
        await Assert.That(refs).Contains("#/components/schemas/FilterAnd");
        await Assert.That(refs).Contains("#/components/schemas/FilterOr");
        await Assert.That(refs).Contains("#/components/schemas/FilterNot");
        await Assert.That(node.OneOf!.Count).IsEqualTo(4);
    }

    [Test]
    public async Task FilterNode_Emits_No_Fabricated_Discriminator()
    {
        // The FilterNodeJsonConverter discriminates by member presence (and/or/not/field), not by a shared
        // discriminator value, so no OpenAPI `discriminator` object is emitted (Requirement 5.3).
        var node = FilterNodeSchema.FilterNode();

        await Assert.That(node.Discriminator).IsNull();
    }

    [Test]
    public async Task Variants_Require_Their_Distinguishing_Wire_Property()
    {
        // Presence of `and`/`or`/`not` (or `field`+`op` for a leaf) is exactly how the converter branches.
        await Assert.That(FilterNodeSchema.FilterAnd().Required!).Contains("and");
        await Assert.That(FilterNodeSchema.FilterOr().Required!).Contains("or");
        await Assert.That(FilterNodeSchema.FilterNot().Required!).Contains("not");

        var leafRequired = FilterNodeSchema.FilterLeaf().Required!;
        await Assert.That(leafRequired).Contains("field");
        await Assert.That(leafRequired).Contains("op");
    }

    [Test]
    public async Task FilterAnd_And_FilterOr_Children_Recursively_Ref_FilterNode()
    {
        var and = FilterNodeSchema.FilterAnd();
        var andArray = PropertyOf(and, "and");
        await Assert.That(andArray.Type).IsEqualTo("array");
        await Assert.That(andArray.Items!.Ref).IsEqualTo(FilterNodeRef);

        var or = FilterNodeSchema.FilterOr();
        var orArray = PropertyOf(or, "or");
        await Assert.That(orArray.Type).IsEqualTo("array");
        await Assert.That(orArray.Items!.Ref).IsEqualTo(FilterNodeRef);
    }

    [Test]
    public async Task FilterNot_Child_Recursively_Refs_FilterNode()
    {
        var not = FilterNodeSchema.FilterNot();
        var child = PropertyOf(not, "not");
        await Assert.That(child.Ref).IsEqualTo(FilterNodeRef);
    }

    [Test]
    public async Task FilterLeaf_Has_Field_Op_Value_With_String_Op_Enum()
    {
        var leaf = FilterNodeSchema.FilterLeaf();

        await Assert.That(leaf.Type).IsEqualTo("object");
        await Assert.That(PropertyOf(leaf, "field").Type).IsEqualTo("string");

        var op = PropertyOf(leaf, "op");
        await Assert.That(op.Type).IsEqualTo("string");
        await Assert.That(op.Enum).IsNotNull();

        // value is permissive/nullable (coerced to the field's CLR type by the engine).
        var value = PropertyOf(leaf, "value");
        await Assert.That(value.Type).IsNull();
        await Assert.That(value.Nullable).IsTrue();
    }

    [Test]
    public async Task FilterLeaf_Op_Enum_Matches_Real_Single_Operator_Member_Names()
    {
        // The wire oracle: leaf.Op.ToString() writes the enum member name. Valid leaf operators are the
        // single-flag (power-of-two, non-None) members; the composite groupings None/Range/Text never
        // appear on the wire. Derive the expected set from the real enum and compare to the descriptor.
        var expected = Enum.GetValues<FilterOperator>()
            .Where(v => v != FilterOperator.None && ((long)v & ((long)v - 1)) == 0)
            .Select(v => v.ToString())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var actual = FilterNodeSchema.FilterLeaf().Properties!["op"].Enum!
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);

        // None, Range, and Text must NOT be offered as leaf operators.
        await Assert.That(actual).DoesNotContain("None");
        await Assert.That(actual).DoesNotContain("Range");
        await Assert.That(actual).DoesNotContain("Text");
    }

    [Test]
    public async Task All_Exposes_FilterNode_And_Four_Variants_Once_Each()
    {
        var all = FilterNodeSchema.All();

        await Assert.That(all.Keys).Contains("FilterNode");
        await Assert.That(all.Keys).Contains("FilterLeaf");
        await Assert.That(all.Keys).Contains("FilterAnd");
        await Assert.That(all.Keys).Contains("FilterOr");
        await Assert.That(all.Keys).Contains("FilterNot");
        await Assert.That(all.Count).IsEqualTo(5);
    }

    [Test]
    public async Task FilterNode_Schemas_Serialize_With_OneOf_And_Recursive_DollarRef()
    {
        var doc = new OpenApiDocument
        {
            Openapi = "3.0.4",
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Components = new OpenApiComponents { Schemas = FilterNodeSchema.All() },
        };

        var json = VistaOpenApiJson.Serialize(doc);

        await Assert.That(json).Contains("\"oneOf\"");
        await Assert.That(json).Contains("\"$ref\":\"#/components/schemas/FilterNode\"");
        await Assert.That(json).Contains("\"$ref\":\"#/components/schemas/FilterLeaf\"");
        await Assert.That(json).DoesNotContain("\"discriminator\"");
    }
}
