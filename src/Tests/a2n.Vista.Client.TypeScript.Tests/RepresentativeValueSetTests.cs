// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using a2n.Vista.Client.TypeScript.Parity;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Resolve;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Unit tests for the <see cref="RepresentativeValueSet"/> builder (task 15.1; Requirement 11.1). They
/// assert, over the real M18 fixture document, that the derived value set satisfies the coverage criteria
/// the parity harnesses depend on: each declared property present, each nullable property in both its
/// present-and-null and absent forms, each collection in empty and non-empty forms, and each enum member of
/// the <c>FilterOperator</c> at least once. They also assert bounded recursion over the recursive
/// <c>FilterNode</c> union and deterministic (reproducible) output.
/// </summary>
public sealed class RepresentativeValueSetTests
{
    // The FilterOperator literals in the exact document order M18 emits them (FilterLeaf.op enum).
    private static readonly string[] ExpectedOperators =
    {
        "Equals", "NotEquals", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual",
        "Contains", "StartsWith", "EndsWith", "In", "Between", "IsNull",
    };

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

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

    // ---- Object coverage: present, present-null, and absent forms of a nullable property (CustomerRow) ----

    [Test]
    public async Task CustomerRow_Covers_Each_Property_Present_In_The_Canonical_Base()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet().Build("CustomerRow", document);

        // The first value is the canonical base: every declared property present and non-null.
        var baseValue = (JsonObject)values[0];
        foreach (var name in new[] { "customerId", "companyName", "contactName", "country", "isActive" })
        {
            await Assert.That(baseValue.ContainsKey(name)).IsTrue();
            await Assert.That(baseValue[name] is not null).IsTrue();
        }
    }

    [Test]
    public async Task CustomerRow_Covers_Nullable_Property_In_Present_Null_And_Absent_Forms()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet().Build("CustomerRow", document);
        var objects = values.OfType<JsonObject>().ToArray();

        // contactName is nullable: there must be a value where it is present-and-null...
        var hasPresentNull = objects.Any(o => o.ContainsKey("contactName") && o["contactName"] is null);
        // ...and a value where it is absent entirely.
        var hasAbsent = objects.Any(o => !o.ContainsKey("contactName"));

        await Assert.That(hasPresentNull).IsTrue();
        await Assert.That(hasAbsent).IsTrue();

        // country is nullable too — same coverage.
        await Assert.That(objects.Any(o => o.ContainsKey("country") && o["country"] is null)).IsTrue();
        await Assert.That(objects.Any(o => !o.ContainsKey("country"))).IsTrue();
    }

    [Test]
    public async Task CustomerRow_Does_Not_Drop_Required_Non_Nullable_Properties_In_Any_Value()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet().Build("CustomerRow", document);

        // customerId is required and non-nullable; it must be present and non-null in every emitted value.
        foreach (var value in values.OfType<JsonObject>())
        {
            await Assert.That(value.ContainsKey("customerId")).IsTrue();
            await Assert.That(value["customerId"] is not null).IsTrue();
        }
    }

    // ---- Collection coverage: empty and non-empty forms (VistaListRequestBody.sort) ----

    [Test]
    public async Task VistaListRequestBody_Covers_A_Collection_In_Empty_And_Non_Empty_Forms()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet().Build("VistaListRequestBody", document);
        var objects = values.OfType<JsonObject>().ToArray();

        // sort is a nullable array: there must be a value where it is a non-empty array...
        var hasNonEmpty = objects.Any(o => o["sort"] is JsonArray { Count: > 0 });
        // ...and a value where it is an empty array.
        var hasEmpty = objects.Any(o => o["sort"] is JsonArray { Count: 0 });

        await Assert.That(hasNonEmpty).IsTrue();
        await Assert.That(hasEmpty).IsTrue();
    }

    [Test]
    public async Task VistaListRequestBody_Expands_The_Nested_FilterNode_Within_The_Depth_Bound()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet(maxUnionDepth: 3).Build("VistaListRequestBody", document);

        // The canonical base carries the nested filter/scope FilterNode expanded to a terminal leaf.
        var baseValue = (JsonObject)values[0];
        var filter = baseValue["filter"] as JsonObject;

        await Assert.That(filter is not null).IsTrue();
        // A FilterLeaf: field + op present, op is a valid FilterOperator literal.
        await Assert.That(filter!.ContainsKey("field")).IsTrue();
        await Assert.That(filter.ContainsKey("op")).IsTrue();
        await Assert.That(ExpectedOperators.Contains(filter["op"]!.GetValue<string>())).IsTrue();
    }

    // ---- Enum coverage: each FilterOperator member at least once (FilterNode / FilterLeaf) ----

    [Test]
    public async Task FilterNode_Covers_Each_FilterOperator_Enum_Member_At_Least_Once()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet().Build("FilterNode", document);

        // Collect every FilterLeaf.op literal that appears anywhere across the emitted leaf values.
        var seenOperators = values
            .OfType<JsonObject>()
            .Where(o => o.ContainsKey("op"))
            .Select(o => o["op"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var op in ExpectedOperators)
        {
            await Assert.That(seenOperators.Contains(op)).IsTrue();
        }
    }

    [Test]
    public async Task FilterNode_Covers_Each_Union_Variant()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet().Build("FilterNode", document).OfType<JsonObject>().ToArray();

        // Presence-discriminated union: a leaf (field+op), an and-node, an or-node, and a not-node.
        await Assert.That(values.Any(o => o.ContainsKey("field") && o.ContainsKey("op"))).IsTrue();
        await Assert.That(values.Any(o => o.ContainsKey("and"))).IsTrue();
        await Assert.That(values.Any(o => o.ContainsKey("or"))).IsTrue();
        await Assert.That(values.Any(o => o.ContainsKey("not"))).IsTrue();
    }

    [Test]
    public async Task FilterNode_Covers_The_And_Children_In_Empty_And_Non_Empty_Forms()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet().Build("FilterNode", document).OfType<JsonObject>().ToArray();

        var andNodes = values.Where(o => o["and"] is JsonArray).ToArray();

        await Assert.That(andNodes.Any(o => ((JsonArray)o["and"]!).Count == 0)).IsTrue();
        await Assert.That(andNodes.Any(o => ((JsonArray)o["and"]!).Count > 0)).IsTrue();
    }

    // ---- ProblemDetails: RFC 7807 members, each nullable, present-null and absent ----

    [Test]
    public async Task ProblemDetails_Covers_Each_Nullable_Member_In_Present_Null_And_Absent_Forms()
    {
        var document = ResolveFixture();

        var values = new RepresentativeValueSet().Build("ProblemDetails", document);
        var objects = values.OfType<JsonObject>().ToArray();

        foreach (var member in new[] { "type", "title", "status", "detail", "instance", "code" })
        {
            await Assert.That(objects.Any(o => o.ContainsKey(member) && o[member] is null)).IsTrue();
            await Assert.That(objects.Any(o => !o.ContainsKey(member))).IsTrue();
        }
    }

    // ---- Determinism: reproducible output ----

    [Test]
    public async Task Build_Is_Deterministic_Across_Repeated_Runs()
    {
        var document = ResolveFixture();
        var builder = new RepresentativeValueSet();

        var first = builder.Build("VistaListRequestBody", document);
        var second = builder.Build("VistaListRequestBody", document);

        await Assert.That(first.Count).IsEqualTo(second.Count);

        var firstJson = first.Select(v => v.ToJsonString()).ToArray();
        var secondJson = second.Select(v => v.ToJsonString()).ToArray();
        await Assert.That(firstJson).IsEquivalentTo(secondJson);
    }

    [Test]
    public async Task Build_Terminates_For_The_Recursive_FilterNode_Union()
    {
        var document = ResolveFixture();

        // A tighter and a wider bound must both terminate and produce non-empty sets.
        var shallow = new RepresentativeValueSet(maxUnionDepth: 0).Build("FilterNode", document);
        var deep = new RepresentativeValueSet(maxUnionDepth: 3).Build("FilterNode", document);

        await Assert.That(shallow.Count).IsGreaterThan(0);
        await Assert.That(deep.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Build_By_Unknown_Type_Name_Throws_Naming_The_Type()
    {
        var document = ResolveFixture();
        var builder = new RepresentativeValueSet();

        await Assert.That(() => builder.Build("NoSuchType", document)).Throws<ArgumentException>();
    }
}
