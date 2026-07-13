// Licensed to the a2n.Vista project. Published artifact — English only.
//
// OpenAPI emitter SCHEMA/WIRE-PARITY property test (spec openapi-emitter, task 8.4).
//
// Property 3: Schema/wire parity for per-view DTOs (master, model-based instance-against-schema).
//   For any covered view and for any value of its TRow (and, when writable, its TCrud) with arbitrary
//   member values — including nullables, enums, collections, and byte[] — the JSON produced by serializing
//   that value through the Serialization_Seam validates against the corresponding View_Dto_Schema in the
//   document: every serialized property name is present with a matching type/format, enums appear as the
//   schema's string members, and null members are permitted exactly where the schema is nullable.
//
// Validates: Requirements 4.1, 4.2, 4.3, 4.4, 14.1, 14.3.
//
// Oracle: the live serializer (the seam). The parity check is INSTANCE-AGAINST-SCHEMA — each iteration
// serializes a random value of a compile-once representative type (EmitterFixtures) through the seam
// options and validates that JSON against the schema the RUC DtoSchemaGenerator emits for the SAME type
// under the SAME options. Because schema generation and serialization share one JsonSerializerOptions
// instance, any mismatch reflects a real generator defect (the wire wins, R14.3), never a config skew.
//
// CsCheck-via-TUnit idiom: Gen<T>.Sample(action, iter: 100) at ≥100 iterations, matching the sibling
// property suites. DtoSchemaGenerator.GenerateSchema is [RequiresUnreferencedCode] (it reflects over the
// CLR type under the seam options, D96 asymmetry), so the driving members are RUC-annotated.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.OpenApi.Schema;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property 3 (task 8.4): instance-against-schema validation of the per-view DTO schemas against the real
/// wire. Quantifies over random VALUES of the compile-once representative TRow types
/// (<see cref="EmitterFixtures.CatalogItemRow"/>, <see cref="EmitterFixtures.GeoZoneRow"/>,
/// <see cref="EmitterFixtures.SubscriptionRow"/>) and the writable TCrud
/// (<see cref="EmitterFixtures.SubscriptionCrud"/>), for cost control — never over generated types.
/// </summary>
public sealed class DtoSchemaWireParityPropertyTests
{
    /// <summary>Minimum iterations per the design "Testing Strategy" (CsCheck via TUnit, ≥100).</summary>
    private const int Iterations = 100;

    // ===== Member-value generators (arbitrary values across the read/write DTO shape spectrum) =========

    /// <summary>A small character pool spanning ASCII, punctuation, whitespace, and non-Latin code points.</summary>
    private static readonly char[] TextChars = "abcXYZ019 .-_\"/中é".ToCharArray();

    /// <summary>A possibly-empty short string (non-null).</summary>
    private static readonly Gen<string> GenText =
        from len in Gen.Int[0, 8]
        from indices in Gen.Int[0, TextChars.Length - 1].Array[len]
        select new string(Array.ConvertAll(indices, i => TextChars[i]));

    /// <summary>A nullable reference member: a string or <see langword="null"/>.</summary>
    private static readonly Gen<string?> GenNullableText =
        Gen.OneOf(GenText.Select(s => (string?)s), Gen.Const((string?)null));

    /// <summary>A nullable value-type member: an int or <see langword="null"/>.</summary>
    private static readonly Gen<int?> GenNullableInt =
        Gen.OneOf(Gen.Int.Select(i => (int?)i), Gen.Const((int?)null));

    /// <summary>A byte[] of varying length (including empty) — a base64 string on the wire.</summary>
    private static readonly Gen<byte[]> GenBytes =
        from len in Gen.Int[0, 8]
        from vals in Gen.Int[0, 255].Array[len]
        select Array.ConvertAll(vals, v => (byte)v);

    /// <summary>A collection member of varying length (including empty), elements non-null.</summary>
    private static readonly Gen<List<string>> GenTags = GenText.List[0, 4];

    /// <summary>A DateTime across the full tick range (constructed from ticks so no <c>Gen.DateTime</c> dependency).</summary>
    private static readonly Gen<DateTime> GenDateTime =
        Gen.Long[0L, DateTime.MaxValue.Ticks].Select(t => new DateTime(t));

    /// <summary>A nullable value-type date member: a DateTime or <see langword="null"/>.</summary>
    private static readonly Gen<DateTime?> GenNullableDateTime =
        Gen.OneOf(GenDateTime.Select(d => (DateTime?)d), Gen.Const((DateTime?)null));

    /// <summary>Every member of the read-row availability enum.</summary>
    private static readonly Gen<EmitterFixtures.CatalogItemStatus> GenStatus =
        Gen.Int[0, 2].Select(i => (EmitterFixtures.CatalogItemStatus)i);

    /// <summary>Every member of the subscription tier enum.</summary>
    private static readonly Gen<EmitterFixtures.SubscriptionTier> GenTier =
        Gen.Int[0, 2].Select(i => (EmitterFixtures.SubscriptionTier)i);

    // ===== DTO-value generators (arbitrary instances of each representative type) =======================

    private static readonly Gen<EmitterFixtures.CatalogItemRow> GenCatalogItem =
        from itemId in Gen.Int
        from name in GenText
        from ratingCount in GenNullableInt
        from nickname in GenNullableText
        from status in GenStatus
        from tags in GenTags
        from thumbnail in GenBytes
        select new EmitterFixtures.CatalogItemRow
        {
            ItemId = itemId,
            Name = name,
            RatingCount = ratingCount,
            Nickname = nickname,
            Status = status,
            Tags = tags,
            Thumbnail = thumbnail,
        };

    private static readonly Gen<EmitterFixtures.GeoZoneRow> GenGeoZone =
        from regionId in Gen.Int
        from zoneCode in GenText
        from description in GenText
        from isActive in Gen.Bool
        select new EmitterFixtures.GeoZoneRow
        {
            RegionId = regionId,
            ZoneCode = zoneCode,
            Description = description,
            IsActive = isActive,
        };

    private static readonly Gen<EmitterFixtures.SubscriptionRow> GenSubscriptionRow =
        from subscriptionId in Gen.Int
        from planName in GenText
        from seatCount in Gen.Int
        from renewsOn in GenNullableDateTime
        from tier in GenTier
        select new EmitterFixtures.SubscriptionRow
        {
            SubscriptionId = subscriptionId,
            PlanName = planName,
            SeatCount = seatCount,
            RenewsOn = renewsOn,
            Tier = tier,
        };

    private static readonly Gen<EmitterFixtures.SubscriptionCrud> GenSubscriptionCrud =
        from planName in GenText
        from seatCount in Gen.Int
        from renewsOn in GenNullableDateTime
        from tier in GenTier
        select new EmitterFixtures.SubscriptionCrud
        {
            PlanName = planName,
            SeatCount = seatCount,
            RenewsOn = renewsOn,
            Tier = tier,
        };

    // ===== The property, one test per representative type ==============================================

    /// <summary>Property 3 over the read-only single-key TRow spanning the full read-DTO shape spectrum.</summary>
    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator over a representative TRow.")]
    public void Wire_Validates_Against_Schema_For_CatalogItemRow()
    {
        // Feature: openapi-emitter, Property 3: Schema/wire parity for per-view DTOs (master, model-based instance-against-schema)
        RunParity(GenCatalogItem);
    }

    /// <summary>Property 3 over the read-only composite-key TRow.</summary>
    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator over a representative TRow.")]
    public void Wire_Validates_Against_Schema_For_GeoZoneRow()
    {
        // Feature: openapi-emitter, Property 3: Schema/wire parity for per-view DTOs (master, model-based instance-against-schema)
        RunParity(GenGeoZone);
    }

    /// <summary>Property 3 over the writable view's TRow (nullable date + enum members).</summary>
    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator over a representative TRow.")]
    public void Wire_Validates_Against_Schema_For_SubscriptionRow()
    {
        // Feature: openapi-emitter, Property 3: Schema/wire parity for per-view DTOs (master, model-based instance-against-schema)
        RunParity(GenSubscriptionRow);
    }

    /// <summary>Property 3 over the writable view's TCrud (a record with a required + init-only members).</summary>
    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator over a representative TCrud.")]
    public void Wire_Validates_Against_Schema_For_SubscriptionCrud()
    {
        // Feature: openapi-emitter, Property 3: Schema/wire parity for per-view DTOs (master, model-based instance-against-schema)
        RunParity(GenSubscriptionCrud);
    }

    // ===== Test driver =================================================================================

    /// <summary>
    /// Generates the schema for <typeparamref name="T"/> ONCE (cost control) via the RUC
    /// <see cref="DtoSchemaGenerator"/> under the seam options, then samples arbitrary values, serializing
    /// each through the SAME options and validating the wire JSON against the emitted schema.
    /// </summary>
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator over the representative type.")]
    private static void RunParity<T>(Gen<T> gen)
    {
        // ONE options instance drives both schema generation and serialization: schema and wire agree by
        // construction, so any failure is a real generator defect (R14.3), not a configuration mismatch.
        var options = EmitterFixtures.SeamOptions();
        var generator = new DtoSchemaGenerator(options);

        // A POCO root resolves to a $ref plus a registered component; the component set is the resolver.
        var rootSchema = generator.GenerateSchema(typeof(T));
        var resolver = generator.Components;

        gen.Sample(
            value =>
            {
                var json = JsonSerializer.Serialize(value, options);
                using var document = JsonDocument.Parse(json);
                ValidateValue(document.RootElement, rootSchema, resolver, typeof(T).Name);
            },
            iter: Iterations);
    }

    // ===== Focused instance-against-schema validator ===================================================
    // Not a full JSON Schema engine — only the checks Property 3 requires (property-name parity, matching
    // type/format, enum-as-string membership, and nullability). The wire is the oracle: any disagreement
    // throws a descriptive exception so CsCheck shrinks to a minimal counterexample.

    private static void ValidateValue(
        JsonElement value,
        OpenApiSchema schema,
        IReadOnlyDictionary<string, OpenApiSchema> resolver,
        string path)
    {
        // R4.3: a JSON null is permitted only where the (member) schema is nullable. Nullability lives on
        // the inline member schema (Nullable<T> and nullable references are decorated inline, never behind
        // a $ref), so it is checked BEFORE resolving any reference.
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (schema.Nullable == true || IsPermissive(schema))
            {
                return;
            }

            throw Fail(path, "JSON null", "the schema is not nullable (Requirement 4.3)");
        }

        var effective = schema.Ref is not null ? ResolveRef(schema, resolver, path) : schema;

        // A permissive {} schema (Requirement 4.6) accepts any non-null value.
        if (IsPermissive(effective))
        {
            return;
        }

        // R4.2/R4.4: an enum member is a string that is one of the schema's declared member names.
        if (effective.Enum is not null)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw Fail(path, KindOf(value), "an enum string (Requirement 4.2)");
            }

            var text = value.GetString();
            if (text is null || !effective.Enum.Contains(text, StringComparer.Ordinal))
            {
                throw Fail(
                    path,
                    text ?? "(null)",
                    "one of the schema enum members [" + string.Join(", ", effective.Enum) + "] (Requirement 4.2)");
            }

            return;
        }

        switch (effective.Type)
        {
            case "string":
                // string / Guid / DateTime / byte[] (format "byte", base64) all ride as a JSON string.
                Require(value.ValueKind == JsonValueKind.String, path, value, "a JSON string");
                break;

            case "integer":
            case "number":
                Require(value.ValueKind == JsonValueKind.Number, path, value, "a JSON number");
                break;

            case "boolean":
                Require(
                    value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    path,
                    value,
                    "a JSON boolean");
                break;

            case "array":
                Require(value.ValueKind == JsonValueKind.Array, path, value, "a JSON array");
                var itemSchema = effective.Items
                    ?? throw Fail(path, "array on the wire", "an array schema with an 'items' member");
                var index = 0;
                foreach (var element in value.EnumerateArray())
                {
                    ValidateValue(element, itemSchema, resolver, path + "[" + index++ + "]");
                }

                break;

            case "object":
                ValidateObject(value, effective, resolver, path);
                break;

            default:
                // An object component may be typeless but carry Properties; otherwise treat as permissive.
                if (effective.Properties is not null)
                {
                    ValidateObject(value, effective, resolver, path);
                }

                break;
        }
    }

    private static void ValidateObject(
        JsonElement value,
        OpenApiSchema schema,
        IReadOnlyDictionary<string, OpenApiSchema> resolver,
        string path)
    {
        Require(value.ValueKind == JsonValueKind.Object, path, value, "a JSON object");

        // An object schema with no declared properties but additionalProperties allowed accepts any object.
        if (schema.Properties is null)
        {
            if (schema.AdditionalProperties == true)
            {
                return;
            }

            // No properties described at all: any wire member would be undescribed.
            foreach (var undescribed in value.EnumerateObject())
            {
                throw Fail(
                    path + "." + undescribed.Name,
                    "present on the wire",
                    "the object schema declares no properties (Requirement 4.1)");
            }

            return;
        }

        foreach (var member in value.EnumerateObject())
        {
            // R4.1: every serialized property name must be present in schema.Properties (no undescribed
            // wire property), matching the seam's naming policy exactly.
            if (!schema.Properties.TryGetValue(member.Name, out var memberSchema))
            {
                throw Fail(
                    path + "." + member.Name,
                    "serialized property '" + member.Name + "' present on the wire",
                    "a matching property name in schema.Properties [" + string.Join(", ", schema.Properties.Keys)
                        + "] (Requirement 4.1)");
            }

            ValidateValue(member.Value, memberSchema, resolver, path + "." + member.Name);
        }
    }

    private static OpenApiSchema ResolveRef(
        OpenApiSchema schema,
        IReadOnlyDictionary<string, OpenApiSchema> resolver,
        string path)
    {
        const string prefix = "#/components/schemas/";
        var reference = schema.Ref!;
        var name = reference.StartsWith(prefix, StringComparison.Ordinal)
            ? reference[prefix.Length..]
            : reference;

        if (!resolver.TryGetValue(name, out var target))
        {
            throw Fail(path, "$ref '" + reference + "'", "a component schema present in the generator's component set");
        }

        return target;
    }

    /// <summary>An all-empty schema (<c>{}</c>) — the permissive shape emitted for an unresolvable member.</summary>
    private static bool IsPermissive(OpenApiSchema schema) =>
        schema.Type is null
        && schema.Ref is null
        && schema.Enum is null
        && schema.OneOf is null
        && schema.Properties is null
        && schema.Items is null
        && schema.AdditionalProperties is null;

    private static string KindOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "a JSON string",
        JsonValueKind.Number => "a JSON number",
        JsonValueKind.True or JsonValueKind.False => "a JSON boolean",
        JsonValueKind.Array => "a JSON array",
        JsonValueKind.Object => "a JSON object",
        JsonValueKind.Null => "JSON null",
        _ => value.ValueKind.ToString(),
    };

    private static void Require(bool condition, string path, JsonElement value, string expected)
    {
        if (!condition)
        {
            throw Fail(path, KindOf(value) + " (" + value.GetRawText() + ")", expected);
        }
    }

    private static Exception Fail(string path, string actual, string expected) =>
        new InvalidOperationException(
            "Property 3 (schema/wire parity) violated at '" + path + "': the wire had " + actual
            + " but the schema expected " + expected + ". The wire is the oracle (Requirement 14.3).");
}
