// Licensed to the a2n.Vista project. Published artifact — English only.
//
// OpenAPI emitter schema/wire-parity property test for the FIXED envelopes and the polymorphic FilterNode
// tree (spec openapi-emitter, task 8.5; Property 4).
//
// Property 4: Schema/wire parity for the fixed envelopes and FilterNode.
//   For any representative value of a Vista envelope (VistaListRequestBody, VistaMetadataResponse,
//   VistaWriteResponse, ViewListResult<TRow>) and for any FilterNode tree (arbitrary nesting of
//   FilterLeaf/FilterAnd/FilterOr/FilterNot), the JSON produced by serializing it through the
//   Serialization_Seam validates against the corresponding Vista_Envelope_Schema / FilterNode_Schema in
//   the document.
//
// Validates: Requirements 5.2, 5.3, 14.2, 14.3.
//
// Oracle: the REAL serialization seam (EmitterFixtures.Seam == VistaJson.Options), which registers the
// reflection-free FilterNodeJsonConverter and the JsonStringEnumConverter under the web-default
// (camelCase) naming policy. Each iteration serializes a representative value through that seam and
// validates the emitted JSON instance-against-schema, resolving $ref against a map built from the
// hand-authored descriptors (EnvelopeSchemas + FilterNodeSchema.All()) plus the RUC-generated row
// component (DtoSchemaGenerator over EmitterFixtures.CatalogItemRow). The wire is the oracle (R14.3): a
// disagreement on a property NAME, an ENUM representation, NULLABILITY, or STRUCTURE (including the
// FilterNode {and:[...]}/{or:[...]}/{not:{...}}/{field,op,value} member presence discrimination) fails
// the property.
//
// This file writes its OWN small instance-against-schema validator (it does not depend on the sibling
// task 8.4 DTO parity test, authored in parallel). CsCheck-via-TUnit idiom: Gen<...>.Sample(action,
// iter: 100) at >= 100 iterations, matching the sibling emitter property suites.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Contracts;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.OpenApi.Schema;
using a2n.Vista.OpenApi.Schemas;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property 4 (task 8.5): schema/wire parity for the fixed Vista envelopes and the polymorphic
/// <c>FilterNode</c> tree (Requirements 5.2, 5.3, 14.2, 14.3). Over representative generated values
/// serialized through the real seam, asserts the emitted JSON validates against the hand-authored
/// envelope/<c>FilterNode</c> descriptors (the wire is the oracle).
/// </summary>
/// <remarks>
/// The row-component schema is produced by the <c>[RequiresUnreferencedCode]</c>
/// <see cref="DtoSchemaGenerator"/> and values are serialized through the reflection fallback of the seam,
/// so the reflection trim/AOT warnings are suppressed at the class level (tests are never trimmed),
/// matching the sibling emitter property suites.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The parity oracle serializes representative values via the seam's reflection fallback and builds the row schema via the RUC DtoSchemaGenerator by design; trimming is not used for tests.")]
[SuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with RequiresDynamicCodeAttribute may break functionality when AOT compiling",
    Justification = "The parity oracle serializes representative values via the seam's reflection fallback by design; AOT compilation is not used for tests.")]
public sealed class EnvelopeFilterNodeWireParityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy: >= 100).</summary>
    private const int Iterations = 100;

    // ===== Generation input pools =====================================================================

    /// <summary>Identifier-safe field names used for leaf fields and sort fields.</summary>
    private static readonly string[] LeafFields = { "name", "amount", "status", "code", "createdAt" };

    /// <summary>
    /// The SINGLE-operator <see cref="FilterOperator"/> members a leaf may carry on the wire — exactly the
    /// values whose <c>ToString()</c> appears in <see cref="FilterNodeSchema.FilterOperatorNames"/>. The
    /// flags groupings (<c>None</c>/<c>Range</c>/<c>Text</c>) never appear on a leaf and are excluded.
    /// </summary>
    private static readonly FilterOperator[] LeafOps =
    {
        FilterOperator.Equals, FilterOperator.NotEquals, FilterOperator.GreaterThan,
        FilterOperator.GreaterThanOrEqual, FilterOperator.LessThan, FilterOperator.LessThanOrEqual,
        FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.EndsWith,
        FilterOperator.In, FilterOperator.Between, FilterOperator.IsNull,
    };

    /// <summary>A small pool of sample string values (including edge cases) used across generators.</summary>
    private static readonly string[] SampleStrings = { "", "abc", "Zürich", "a b c", "42", "value" };

    /// <summary>Export format ids for the list body's <c>format</c> slot.</summary>
    private static readonly string[] Formats = { "csv", "xlsx", "json" };

    // ===== FilterNode value generators (declared before consumers for init ordering) ==================

    /// <summary>
    /// A neutral leaf value in the exact CLR value space the converter's <c>WriteValue</c> supports:
    /// <see langword="null"/>, string, integer (<see cref="long"/>), floating-point, boolean, or a list of
    /// those. The <c>value</c> schema slot is permissive, so any of these validates; the generator exists
    /// to exercise every wire kind.
    /// </summary>
    private static readonly Gen<object?> GenLeafValue =
        from kind in Gen.Int[0, 5]
        from si in Gen.Int[0, SampleStrings.Length - 1]
        from n in Gen.Int[-1_000_000, 1_000_000]
        from b in Gen.Bool
        select kind switch
        {
            0 => (object?)null,
            1 => SampleStrings[si],
            2 => (long)n,
            3 => n + 0.5d,
            4 => b,
            _ => new List<object?> { (long)n, SampleStrings[si] },
        };

    /// <summary>A single <see cref="FilterLeaf"/>: a random field, a random single operator, a random value.</summary>
    private static readonly Gen<FilterNode> GenLeaf =
        from fi in Gen.Int[0, LeafFields.Length - 1]
        from oi in Gen.Int[0, LeafOps.Length - 1]
        from v in GenLeafValue
        select (FilterNode)new FilterLeaf(LeafFields[fi], LeafOps[oi], v);

    /// <summary>
    /// A depth-bounded arbitrary <see cref="FilterNode"/> tree: at depth 0 a leaf; otherwise a leaf,
    /// an <see cref="FilterAnd"/>/<see cref="FilterOr"/> with 0..3 recursive children, or a
    /// <see cref="FilterNot"/> wrapping a single recursive child. The depth bound keeps the generator
    /// finite.
    /// </summary>
    /// <param name="depth">The remaining recursion budget.</param>
    /// <returns>A generator of <see cref="FilterNode"/> trees.</returns>
    private static Gen<FilterNode> GenFilterNode(int depth)
    {
        if (depth <= 0)
        {
            return GenLeaf;
        }

        return
            from kind in Gen.Int[0, 3]
            from node in kind switch
            {
                0 => GenLeaf,
                1 => GenChildren(depth).Select(c => (FilterNode)new FilterAnd(c)),
                2 => GenChildren(depth).Select(c => (FilterNode)new FilterOr(c)),
                _ => GenFilterNode(depth - 1).Select(ch => (FilterNode)new FilterNot(ch)),
            }
            select node;
    }

    /// <summary>Generates 0..3 recursive children for an <c>and</c>/<c>or</c> node.</summary>
    private static Gen<IReadOnlyList<FilterNode>> GenChildren(int depth) =>
        from count in Gen.Int[0, 3]
        from children in GenFilterNode(depth - 1).Array[count]
        select (IReadOnlyList<FilterNode>)children;

    /// <summary>A <see cref="FilterNode"/> that is present (a real tree) about 3/4 of the time, else null.</summary>
    private static readonly Gen<FilterNode?> GenOptionalFilter =
        from choice in Gen.Int[0, 3]
        from node in GenFilterNode(3)
        select choice == 0 ? (FilterNode?)null : node;

    // ===== Envelope generators ========================================================================

    /// <summary>A single sort instruction with a random field and direction.</summary>
    private static readonly Gen<VistaSortBody> GenSort =
        from fi in Gen.Int[0, LeafFields.Length - 1]
        from desc in Gen.Bool
        select new VistaSortBody { Field = LeafFields[fi], Desc = desc };

    /// <summary>An optional sort list (null about half the time, else 0..3 instructions).</summary>
    private static readonly Gen<List<VistaSortBody>?> GenSortList =
        from present in Gen.Bool
        from count in Gen.Int[0, 3]
        from specs in GenSort.Array[count]
        select present ? specs.ToList() : (List<VistaSortBody>?)null;

    /// <summary>
    /// A representative <see cref="VistaListRequestBody"/>: optional filter/scope <see cref="FilterNode"/>
    /// (possibly null), optional search/format, an optional sort list, and paging integers.
    /// </summary>
    private static readonly Gen<VistaListRequestBody> GenListBody =
        from filter in GenOptionalFilter
        from scope in GenOptionalFilter
        from hasSearch in Gen.Bool
        from searchIdx in Gen.Int[0, SampleStrings.Length - 1]
        from sort in GenSortList
        from page in Gen.Int[0, 10_000]
        from pageSize in Gen.Int[0, 500]
        from hasFormat in Gen.Bool
        from formatIdx in Gen.Int[0, Formats.Length - 1]
        select new VistaListRequestBody
        {
            Filter = filter,
            Search = hasSearch ? SampleStrings[searchIdx] : null,
            Scope = scope,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
            Format = hasFormat ? Formats[formatIdx] : null,
        };

    /// <summary>A single projected field-metadata row for the metadata response.</summary>
    private static readonly Gen<VistaFieldMetadataResponse> GenFieldMeta =
        from ni in Gen.Int[0, LeafFields.Length - 1]
        from flags in Gen.Bool.Array[6]
        from oi in Gen.Int[0, LeafOps.Length - 1]
        select new VistaFieldMetadataResponse(
            Name: LeafFields[ni],
            Label: LeafFields[ni],
            ClrType: "String",
            IsFilterable: flags[0],
            IsSortable: flags[1],
            IsSearchable: flags[2],
            IsScopable: flags[3],
            IsHidden: flags[4],
            IsPrimaryKey: flags[5],
            AllowedOperators: LeafOps[oi].ToString());

    /// <summary>A representative <see cref="VistaMetadataResponse"/> with a random field list.</summary>
    private static readonly Gen<VistaMetadataResponse> GenMetadataResponse =
        from ni in Gen.Int[0, 999]
        from readOnly in Gen.Bool
        from composite in Gen.Bool
        from maxPageSize in Gen.Int[1, 1_000]
        from maxExportRows in Gen.Int[1, 100_000]
        from fieldCount in Gen.Int[0, 4]
        from fields in GenFieldMeta.Array[fieldCount]
        select new VistaMetadataResponse(
            Name: "view" + ni.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Route: "/api/views/view" + ni.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IsReadOnly: readOnly,
            KeyFields: composite ? new[] { "keyA", "keyB" } : new[] { "id" },
            MaxPageSize: maxPageSize,
            MaxExportRows: maxExportRows,
            Fields: fields.ToList());

    /// <summary>
    /// A representative <see cref="VistaWriteResponse"/> whose <c>Key</c> is a scalar (int/string) or a
    /// composite field-name→value map — the shapes the executor produces (Requirement 3.5). The <c>key</c>
    /// schema slot is permissive, so all three validate.
    /// </summary>
    private static readonly Gen<VistaWriteResponse> GenWriteResponse =
        from kind in Gen.Int[0, 2]
        from n in Gen.Int[0, 100_000]
        from si in Gen.Int[0, SampleStrings.Length - 1]
        select new VistaWriteResponse(kind switch
        {
            0 => (object)n,
            1 => SampleStrings[si],
            _ => new Dictionary<string, object?> { ["regionId"] = n, ["zoneCode"] = SampleStrings[si] },
        });

    /// <summary>A representative <see cref="EmitterFixtures.CatalogItemRow"/> spanning the read-DTO shapes.</summary>
    private static readonly Gen<EmitterFixtures.CatalogItemRow> GenCatalogRow =
        from id in Gen.Int[0, 1_000_000]
        from ni in Gen.Int[0, SampleStrings.Length - 1]
        from hasRating in Gen.Bool
        from rating in Gen.Int[0, 1_000]
        from hasNick in Gen.Bool
        from statusIdx in Gen.Int[0, 2]
        from tagCount in Gen.Int[0, 3]
        from tagIdx in Gen.Int[0, SampleStrings.Length - 1].Array[tagCount]
        from thumbLen in Gen.Int[0, 8]
        select new EmitterFixtures.CatalogItemRow
        {
            ItemId = id,
            Name = SampleStrings[ni],
            RatingCount = hasRating ? rating : (int?)null,
            Nickname = hasNick ? SampleStrings[ni] : null,
            Status = (EmitterFixtures.CatalogItemStatus)statusIdx,
            Tags = tagIdx.Select(i => SampleStrings[i]).ToList(),
            Thumbnail = MakeBytes(thumbLen),
        };

    /// <summary>A representative <see cref="ViewListResult{TRow}"/> over the catalog row.</summary>
    private static readonly Gen<ViewListResult<EmitterFixtures.CatalogItemRow>> GenViewListResult =
        from rowCount in Gen.Int[0, 3]
        from rows in GenCatalogRow.Array[rowCount]
        from totalRows in Gen.Int[0, 100_000]
        from pageIndex in Gen.Int[0, 100]
        from pageSize in Gen.Int[1, 100]
        from totalPages in Gen.Int[0, 1_000]
        from unfiltered in Gen.Int[0, 100_000]
        select new ViewListResult<EmitterFixtures.CatalogItemRow>(
            new PagedResult<EmitterFixtures.CatalogItemRow>(
                rows.ToList(), totalRows, pageIndex, pageSize, totalPages),
            unfiltered);

    // ===== $ref resolver (descriptors + the RUC-generated row component) ==============================

    /// <summary>
    /// The component resolver map plus the catalog row's <c>$ref</c> string. Built once from
    /// <see cref="FilterNodeSchema.All()"/>, the referenced envelope sub-descriptors
    /// (<c>VistaSortBody</c>, <c>VistaFieldMetadataResponse</c>), and the row component produced by the
    /// RUC <see cref="DtoSchemaGenerator"/> over <see cref="EmitterFixtures.CatalogItemRow"/>.
    /// </summary>
    private static readonly (IReadOnlyDictionary<string, OpenApiSchema> Map, string RowRef) Env = BuildEnv();

    /// <summary>The <c>ViewListResult</c> schema bound to the generated catalog-row <c>$ref</c>.</summary>
    private static readonly OpenApiSchema ViewListSchema = EnvelopeSchemas.ViewListResult(Env.RowRef);

    private static (IReadOnlyDictionary<string, OpenApiSchema>, string) BuildEnv()
    {
        var map = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);

        // The polymorphic FilterNode schema + its four variant schemas (task 2.2).
        foreach (var pair in FilterNodeSchema.All())
        {
            map[pair.Key] = pair.Value;
        }

        // Envelope sub-descriptors referenced by $ref from the list/metadata bodies (task 2.1).
        map["VistaSortBody"] = EnvelopeSchemas.VistaSortBody();
        map["VistaFieldMetadataResponse"] = EnvelopeSchemas.VistaFieldMetadataResponse();

        // The per-view row component (the RUC reflection branch, authored under the real seam options so
        // the property names/nullability/enum/format match the wire the same seam emits).
        var generator = new DtoSchemaGenerator(EmitterFixtures.Seam);
        var rowSchema = generator.GenerateSchema(typeof(EmitterFixtures.CatalogItemRow));
        foreach (var pair in generator.Components)
        {
            map[pair.Key] = pair.Value;
        }

        return (map, rowSchema.Ref ?? DtoSchemaGenerator.ComponentRef(nameof(EmitterFixtures.CatalogItemRow)));
    }

    private static byte[] MakeBytes(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 7) & 0xFF);
        }

        return bytes;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, EmitterFixtures.Seam);

    // ===== Properties =================================================================================

    /// <summary>
    /// Property 4 (FilterNode): any arbitrary <see cref="FilterNode"/> tree serialized through the seam
    /// (driving <c>FilterNodeJsonConverter</c>) validates against the polymorphic <c>FilterNode</c>
    /// descriptor — the presence-discriminated <c>oneOf</c> of leaf/and/or/not, with recursive children
    /// and the <c>op</c> enum matching the converter (Requirements 5.2, 5.3, 14.2, 14.3).
    /// </summary>
    [Test]
    public void FilterNode_Trees_Validate_Against_The_FilterNode_Schema()
    {
        var schema = FilterNodeSchema.FilterNode();

        // Feature: openapi-emitter, Property 4: Schema/wire parity for the fixed envelopes and FilterNode
        GenFilterNode(3).Sample(
            node =>
            {
                var json = Serialize(node);
                using var document = JsonDocument.Parse(json);
                var errors = new List<string>();
                ValidateNonNull(document.RootElement, schema, "$", errors);
                if (errors.Count > 0)
                {
                    throw new Exception(BuildFailure("FilterNode", json, errors));
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Property 4 (list body): any representative <see cref="VistaListRequestBody"/> serialized through the
    /// seam validates against the hand-authored <c>VistaListRequestBody</c> descriptor, including the
    /// <c>filter</c>/<c>scope</c> <c>FilterNode</c> <c>$ref</c> slots and the <c>sort</c> array of
    /// <c>VistaSortBody</c> (Requirements 5.2, 14.2, 14.3).
    /// </summary>
    [Test]
    public void VistaListRequestBody_Validates_Against_Its_Envelope_Schema()
    {
        var schema = EnvelopeSchemas.VistaListRequestBody();

        // Feature: openapi-emitter, Property 4: Schema/wire parity for the fixed envelopes and FilterNode
        GenListBody.Sample(
            body =>
            {
                var json = Serialize(body);
                using var document = JsonDocument.Parse(json);
                var errors = new List<string>();
                ValidateNonNull(document.RootElement, schema, "$", errors);
                if (errors.Count > 0)
                {
                    throw new Exception(BuildFailure("VistaListRequestBody", json, errors));
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Property 4 (list result): any representative <see cref="ViewListResult{TRow}"/> serialized through
    /// the seam validates against the row-bound <c>ViewListResult</c> descriptor — the nested
    /// <c>page.items</c> array of the generated row schema plus the paging totals (Requirements 14.2,
    /// 14.3).
    /// </summary>
    [Test]
    public void ViewListResult_Validates_Against_Its_Envelope_Schema()
    {
        // Feature: openapi-emitter, Property 4: Schema/wire parity for the fixed envelopes and FilterNode
        GenViewListResult.Sample(
            result =>
            {
                var json = Serialize(result);
                using var document = JsonDocument.Parse(json);
                var errors = new List<string>();
                ValidateNonNull(document.RootElement, ViewListSchema, "$", errors);
                if (errors.Count > 0)
                {
                    throw new Exception(BuildFailure("ViewListResult", json, errors));
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Property 4 (metadata response): any representative <see cref="VistaMetadataResponse"/> serialized
    /// through the seam validates against the <c>VistaMetadataResponse</c> descriptor, including the
    /// <c>fields</c> array of <c>VistaFieldMetadataResponse</c> (Requirements 14.2, 14.3).
    /// </summary>
    [Test]
    public void VistaMetadataResponse_Validates_Against_Its_Envelope_Schema()
    {
        var schema = EnvelopeSchemas.VistaMetadataResponse();

        // Feature: openapi-emitter, Property 4: Schema/wire parity for the fixed envelopes and FilterNode
        GenMetadataResponse.Sample(
            response =>
            {
                var json = Serialize(response);
                using var document = JsonDocument.Parse(json);
                var errors = new List<string>();
                ValidateNonNull(document.RootElement, schema, "$", errors);
                if (errors.Count > 0)
                {
                    throw new Exception(BuildFailure("VistaMetadataResponse", json, errors));
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Property 4 (write response): any representative <see cref="VistaWriteResponse"/> (scalar or
    /// composite key) serialized through the seam validates against the <c>VistaWriteResponse</c>
    /// descriptor (Requirements 14.2, 14.3).
    /// </summary>
    [Test]
    public void VistaWriteResponse_Validates_Against_Its_Envelope_Schema()
    {
        var schema = EnvelopeSchemas.VistaWriteResponse();

        // Feature: openapi-emitter, Property 4: Schema/wire parity for the fixed envelopes and FilterNode
        GenWriteResponse.Sample(
            response =>
            {
                var json = Serialize(response);
                using var document = JsonDocument.Parse(json);
                var errors = new List<string>();
                ValidateNonNull(document.RootElement, schema, "$", errors);
                if (errors.Count > 0)
                {
                    throw new Exception(BuildFailure("VistaWriteResponse", json, errors));
                }
            },
            iter: Iterations);
    }

    // ===== Instance-against-schema validator ==========================================================

    /// <summary>
    /// Validates a JSON property value against its (possibly <c>$ref</c>) schema, honoring nullability. A
    /// JSON <c>null</c> is accepted only when the schema is nullable, permissive (<c>{}</c> admits any type
    /// including null), or an OPTIONAL (<c>required</c>-absent) reference slot — the descriptor's documented
    /// "optional" contract for the <c>filter</c>/<c>scope</c> <c>FilterNode</c> refs, where present-null is
    /// equivalent to absence. A <c>null</c> against a non-nullable typed/required member is a parity failure
    /// (R14.3 nullability).
    /// </summary>
    private static void ValidateProperty(
        JsonElement value, OpenApiSchema schema, string path, List<string> errors, bool required)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (schema.Nullable == true || IsPermissive(schema))
            {
                return;
            }

            if (schema.Ref is not null && !required)
            {
                // Optional reference slot: the wire's present-null is equivalent to the property's absence.
                return;
            }

            errors.Add($"{path}: null value is not permitted by a non-nullable schema (nullability parity).");
            return;
        }

        ValidateNonNull(value, schema, path, errors);
    }

    /// <summary>
    /// Validates a non-null JSON value against its schema, resolving a <c>$ref</c> to a component in
    /// <see cref="Env"/> and dispatching on <c>oneOf</c> / permissive / type.
    /// </summary>
    private static void ValidateNonNull(JsonElement value, OpenApiSchema schema, string path, List<string> errors)
    {
        var resolved = Resolve(schema, path, errors);
        if (resolved is null)
        {
            return;
        }

        if (resolved.OneOf is not null)
        {
            ValidateOneOf(value, resolved.OneOf, path, errors);
            return;
        }

        if (IsPermissive(resolved))
        {
            return;
        }

        switch (resolved.Type)
        {
            case "object":
                ValidateObject(value, resolved, path, errors);
                break;

            case "array":
                ValidateArray(value, resolved, path, errors);
                break;

            case "string":
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors.Add($"{path}: expected a string, but the wire kind was '{value.ValueKind}'.");
                }
                else if (resolved.Enum is not null && !resolved.Enum.Contains(value.GetString(), StringComparer.Ordinal))
                {
                    errors.Add(
                        $"{path}: string '{value.GetString()}' is not one of the schema enum values [" +
                        string.Join(", ", resolved.Enum) + "] (enum parity).");
                }

                break;

            case "integer":
            case "number":
                if (value.ValueKind != JsonValueKind.Number)
                {
                    errors.Add($"{path}: expected a {resolved.Type}, but the wire kind was '{value.ValueKind}'.");
                }

                break;

            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors.Add($"{path}: expected a boolean, but the wire kind was '{value.ValueKind}'.");
                }

                break;

            case null:
                // No `type` but some constraint (for example a bare enum): only the enum is checked.
                if (resolved.Enum is not null
                    && value.ValueKind == JsonValueKind.String
                    && !resolved.Enum.Contains(value.GetString(), StringComparer.Ordinal))
                {
                    errors.Add(
                        $"{path}: string '{value.GetString()}' is not one of the schema enum values (enum parity).");
                }

                break;

            default:
                errors.Add($"{path}: unsupported schema type '{resolved.Type}'.");
                break;
        }
    }

    /// <summary>
    /// Validates an object value: every wire property must be described by <c>Properties</c> (or admitted
    /// by <c>additionalProperties: true</c>) — an undescribed wire property is a property-name parity gap
    /// (R14.3) — and every <c>required</c> property must be present.
    /// </summary>
    private static void ValidateObject(JsonElement value, OpenApiSchema schema, string path, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: expected an object, but the wire kind was '{value.ValueKind}'.");
            return;
        }

        var properties = schema.Properties;
        var required = schema.Required ?? Array.Empty<string>();
        var additionalAllowed = schema.AdditionalProperties == true;

        foreach (var member in value.EnumerateObject())
        {
            if (properties is not null && properties.TryGetValue(member.Name, out var memberSchema))
            {
                var isRequired = required.Contains(member.Name, StringComparer.Ordinal);
                ValidateProperty(member.Value, memberSchema, path + "." + member.Name, errors, isRequired);
            }
            else if (!additionalAllowed)
            {
                errors.Add(
                    $"{path}: wire property '{member.Name}' is not described by the schema (property-name parity).");
            }
        }

        foreach (var name in required)
        {
            if (!value.TryGetProperty(name, out _))
            {
                errors.Add($"{path}: required property '{name}' is missing on the wire.");
            }
        }
    }

    /// <summary>Validates an array value: every item is validated against the <c>items</c> schema.</summary>
    private static void ValidateArray(JsonElement value, OpenApiSchema schema, string path, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{path}: expected an array, but the wire kind was '{value.ValueKind}'.");
            return;
        }

        if (schema.Items is null)
        {
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateProperty(item, schema.Items, $"{path}[{index}]", errors, required: false);
            index++;
        }
    }

    /// <summary>
    /// Validates a value against a <c>oneOf</c> union: it must validate cleanly against at least one
    /// variant. The <c>FilterNode</c> variants are presence-discriminated (each marks its distinguishing
    /// member <c>required</c>), so a well-formed node matches exactly one variant; matching none is a
    /// structural parity failure (R5.2/R5.3).
    /// </summary>
    private static void ValidateOneOf(
        JsonElement value, IReadOnlyList<OpenApiSchema> variants, string path, List<string> errors)
    {
        var variantFailures = new List<string>();
        foreach (var variant in variants)
        {
            var subErrors = new List<string>();
            ValidateNonNull(value, variant, path, subErrors);
            if (subErrors.Count == 0)
            {
                return;
            }

            variantFailures.AddRange(subErrors);
        }

        errors.Add(
            $"{path}: value matched none of the {variants.Count} oneOf variants (FilterNode structural parity). " +
            "Variant failures: " + string.Join(" | ", variantFailures));
    }

    private static OpenApiSchema? Resolve(OpenApiSchema schema, string path, List<string> errors)
    {
        if (schema.Ref is null)
        {
            return schema;
        }

        var name = schema.Ref[(schema.Ref.LastIndexOf('/') + 1)..];
        if (Env.Map.TryGetValue(name, out var target))
        {
            return target;
        }

        errors.Add($"{path}: unresolved $ref '{schema.Ref}' (no component named '{name}').");
        return null;
    }

    /// <summary>
    /// A schema imposes no type constraint (the permissive <c>{}</c>) when it declares no type, ref,
    /// oneOf, enum, properties, or items. Such a schema admits any JSON value.
    /// </summary>
    private static bool IsPermissive(OpenApiSchema schema) =>
        schema.Type is null
        && schema.Ref is null
        && schema.OneOf is null
        && schema.Enum is null
        && schema.Properties is null
        && schema.Items is null;

    private static string BuildFailure(string subject, string json, List<string> errors) =>
        $"Schema/wire parity violated for {subject} (the wire is the oracle, R14.3).\n" +
        $"  Wire JSON: {json}\n" +
        $"  Discrepancies [{errors.Count}]:\n    - " + string.Join("\n    - ", errors);
}
