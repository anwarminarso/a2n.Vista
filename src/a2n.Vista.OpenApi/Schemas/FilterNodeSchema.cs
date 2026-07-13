using System.Collections.Generic;
using a2n.Vista.OpenApi.Model;

namespace a2n.Vista.OpenApi.Schemas;

/// <summary>
/// Hand-authored, reflection-free <see cref="OpenApiSchema"/> descriptor for the polymorphic
/// <c>FilterNode</c> tree (spec openapi-emitter, task 2.2; Requirements 5.1, 5.2, 5.3, 5.4).
/// </summary>
/// <remarks>
/// <para>
/// These descriptors are authored <b>by hand</b> from the real Core hierarchy
/// (<see cref="a2n.Vista.Contracts.FilterNode"/> and its sealed variants <c>FilterLeaf</c>,
/// <c>FilterAnd</c>, <c>FilterOr</c>, <c>FilterNot</c>, plus the <see cref="a2n.Vista.Contracts.FilterOperator"/>
/// enum) and — critically — from the <b>wire shape</b> that
/// <c>a2n.Vista.AspNetCore.Serialization.FilterNodeJsonConverter</c> actually emits, so no reflection is
/// used and the <c>FilterNode</c> portion of a document is AOT-clean (Requirement 13.4).
/// </para>
/// <para>
/// <b>Discrimination is by member presence, not by a discriminator value.</b> The converter distinguishes
/// node kinds purely by which property is present on the object:
/// <list type="bullet">
///   <item><description><c>{ "and": [ ... ] }</c> → <c>FilterAnd</c></description></item>
///   <item><description><c>{ "or": [ ... ] }</c> → <c>FilterOr</c></description></item>
///   <item><description><c>{ "not": { ... } }</c> → <c>FilterNot</c></description></item>
///   <item><description><c>{ "field": "...", "op": "...", "value": ... }</c> → <c>FilterLeaf</c></description></item>
/// </list>
/// There is <b>no</b> single property carrying a type-name value shared across all four variants, so an
/// OpenAPI <c>discriminator</c> object (which mandates a <c>propertyName</c> present in every variant that
/// selects the concrete schema) cannot faithfully model it. Emitting a fabricated discriminator would
/// mislead code generators that expect the discriminator property to be a required member of each variant.
/// To stay <b>consistent with the converter</b> (Requirement 5.3), this descriptor therefore expresses the
/// discrimination structurally: <c>FilterNode</c> is a <c>oneOf</c> of the four variants, and each variant
/// marks its distinguishing wire property as <c>required</c> (<c>and</c>/<c>or</c>/<c>not</c>, or
/// <c>field</c>+<c>op</c> for a leaf), which is exactly the signal the converter branches on. No
/// <see cref="OpenApiDiscriminator"/> is emitted.
/// </para>
/// <para>
/// The recursive children reference the single <c>FilterNode</c> schema by <c>$ref</c>
/// (<see cref="EnvelopeSchemas.FilterNodeRef"/>): <c>and</c>/<c>or</c> are arrays of <c>FilterNode</c> and
/// <c>not</c> is a single <c>FilterNode</c>, so the tree is emitted once and referenced recursively
/// (Requirement 5.4). The <c>VistaListRequestBody</c> <c>filter</c>/<c>scope</c> slots already reference the
/// same schema (see <see cref="EnvelopeSchemas.VistaListRequestBody"/>).
/// </para>
/// </remarks>
public static class FilterNodeSchema
{
    /// <summary>The component name of the polymorphic <c>FilterNode</c> schema.</summary>
    public const string FilterNodeName = "FilterNode";

    /// <summary>The component name of the <c>FilterLeaf</c> variant schema.</summary>
    public const string FilterLeafName = "FilterLeaf";

    /// <summary>The component name of the <c>FilterAnd</c> variant schema.</summary>
    public const string FilterAndName = "FilterAnd";

    /// <summary>The component name of the <c>FilterOr</c> variant schema.</summary>
    public const string FilterOrName = "FilterOr";

    /// <summary>The component name of the <c>FilterNot</c> variant schema.</summary>
    public const string FilterNotName = "FilterNot";

    /// <summary>
    /// The wire values a <c>FilterLeaf</c> <c>op</c> may take: the member names of the single-operator
    /// <see cref="a2n.Vista.Contracts.FilterOperator"/> values, matching <c>leaf.Op.ToString()</c> in the
    /// converter's <c>Write</c> path.
    /// </summary>
    /// <remarks>
    /// The flags value <c>None</c> is rejected by the converter's reader and the composite groupings
    /// <c>Range</c>/<c>Text</c> are authoring conveniences for field whitelists — a leaf always carries
    /// exactly one operator — so none of the three appear on the wire and none are listed here. The list is
    /// authored by hand (reflection-free); task 2.2's test validates it against the real enum members.
    /// </remarks>
    public static readonly IReadOnlyList<string> FilterOperatorNames = new[]
    {
        "Equals",
        "NotEquals",
        "GreaterThan",
        "GreaterThanOrEqual",
        "LessThan",
        "LessThanOrEqual",
        "Contains",
        "StartsWith",
        "EndsWith",
        "In",
        "Between",
        "IsNull",
    };

    private static OpenApiSchema FilterNodeReference() => new() { Ref = EnvelopeSchemas.FilterNodeRef };

    private static IReadOnlyDictionary<string, OpenApiSchema> Props(
        params (string Name, OpenApiSchema Schema)[] members)
    {
        var map = OpenApiCollections.CreateMap<OpenApiSchema>();
        foreach (var (name, schema) in members)
        {
            map[name] = schema;
        }

        return map;
    }

    /// <summary>
    /// The <c>FilterLeaf</c> variant: <c>{ "field": &lt;string&gt;, "op": &lt;operator&gt;, "value": &lt;any&gt; }</c>.
    /// <c>op</c> is a JSON <c>string</c> constrained to the <see cref="FilterOperatorNames"/>, matching the
    /// converter writing <c>leaf.Op.ToString()</c>. <c>value</c> is permissive (a scalar, a list, or
    /// <c>null</c>) because the engine coerces it to the field's CLR type. <c>field</c> and <c>op</c> are
    /// required — their presence is how the converter recognizes a leaf.
    /// </summary>
    public static OpenApiSchema FilterLeaf() => new()
    {
        Type = "object",
        Properties = Props(
            ("field", new OpenApiSchema { Type = "string" }),
            ("op", new OpenApiSchema { Type = "string", Enum = FilterOperatorNames }),
            ("value", new OpenApiSchema
            {
                Nullable = true,
                Description = "The comparison value: a scalar, a list of scalars (for 'In'/'Between'), or null (for 'IsNull').",
            })),
        Required = new[] { "field", "op" },
    };

    /// <summary>
    /// The <c>FilterAnd</c> variant: <c>{ "and": [ &lt;FilterNode&gt;, ... ] }</c> — a conjunction whose
    /// <c>and</c> array items recursively reference <c>FilterNode</c> (Requirement 5.4). The <c>and</c>
    /// member is required (its presence selects this variant).
    /// </summary>
    public static OpenApiSchema FilterAnd() => new()
    {
        Type = "object",
        Properties = Props(
            ("and", new OpenApiSchema { Type = "array", Items = FilterNodeReference() })),
        Required = new[] { "and" },
    };

    /// <summary>
    /// The <c>FilterOr</c> variant: <c>{ "or": [ &lt;FilterNode&gt;, ... ] }</c> — a disjunction whose
    /// <c>or</c> array items recursively reference <c>FilterNode</c> (Requirement 5.4). The <c>or</c>
    /// member is required (its presence selects this variant).
    /// </summary>
    public static OpenApiSchema FilterOr() => new()
    {
        Type = "object",
        Properties = Props(
            ("or", new OpenApiSchema { Type = "array", Items = FilterNodeReference() })),
        Required = new[] { "or" },
    };

    /// <summary>
    /// The <c>FilterNot</c> variant: <c>{ "not": &lt;FilterNode&gt; }</c> — a negation whose single
    /// <c>not</c> child recursively references <c>FilterNode</c> (Requirement 5.4). The <c>not</c> member is
    /// required (its presence selects this variant).
    /// </summary>
    public static OpenApiSchema FilterNot() => new()
    {
        Type = "object",
        Properties = Props(
            ("not", FilterNodeReference())),
        Required = new[] { "not" },
    };

    /// <summary>
    /// The polymorphic <c>FilterNode</c> schema: a <c>oneOf</c> of the four variants
    /// (<c>FilterLeaf</c>/<c>FilterAnd</c>/<c>FilterOr</c>/<c>FilterNot</c>), referenced by <c>$ref</c>
    /// (Requirement 5.1). No <see cref="OpenApiDiscriminator"/> is emitted because the converter
    /// discriminates by member presence rather than by a shared discriminator value (Requirement 5.3);
    /// each variant marks its distinguishing wire property as required instead.
    /// </summary>
    public static OpenApiSchema FilterNode() => new()
    {
        OneOf = new[]
        {
            new OpenApiSchema { Ref = "#/components/schemas/" + FilterLeafName },
            new OpenApiSchema { Ref = "#/components/schemas/" + FilterAndName },
            new OpenApiSchema { Ref = "#/components/schemas/" + FilterOrName },
            new OpenApiSchema { Ref = "#/components/schemas/" + FilterNotName },
        },
    };

    /// <summary>
    /// All <c>FilterNode</c>-related component schemas keyed by component name, for the document builder
    /// (task 5.x) to register once under <c>components.schemas</c>: <c>FilterNode</c> plus its four
    /// variants. The map uses an ordinal-ordered dictionary for deterministic output (Requirement 9.2).
    /// </summary>
    public static IReadOnlyDictionary<string, OpenApiSchema> All()
    {
        var map = OpenApiCollections.CreateMap<OpenApiSchema>();
        map[FilterNodeName] = FilterNode();
        map[FilterLeafName] = FilterLeaf();
        map[FilterAndName] = FilterAnd();
        map[FilterOrName] = FilterOr();
        map[FilterNotName] = FilterNot();
        return map;
    }
}
