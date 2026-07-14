// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Pipeline;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for the scalar mapper's <em>degradation</em> contract (task 6.3; Requirements 3.6,
/// 3.7). Some schema members cannot be mapped to a precise TypeScript type: a permissive/unconstrained
/// object (an <c>{}</c> schema or a bare <c>type: object</c>, Requirement 3.6) and a scalar
/// <c>type</c>/<c>format</c> combination the generator does not recognize (Requirement 3.7). For every such
/// member the mapper must behave the same way — degrade to <c>unknown</c>, record exactly one non-fatal
/// notice that identifies the owning view and property, never throw, and never omit the member.
/// </summary>
/// <remarks>
/// <para>
/// Both generators are constructed to land <em>strictly</em> inside the degrading space, so a passing run
/// proves the degradation path itself rather than an accidental map. The recognized surface is carefully
/// avoided: <c>integer</c>/<c>number</c>/<c>boolean</c> are recognized scalars; <c>string</c> with no format
/// or the <c>uuid</c>/<c>date-time</c>/<c>byte</c> formats is recognized; a <c>$ref</c>, an <c>array</c>, and
/// a <c>string</c> enum all take non-degrading branches. The generators emit none of those.
/// </para>
/// <para>
/// A degraded member's type collapses to the shared <c>unknown</c> singleton even when the schema is
/// <c>nullable</c> (<c>unknown</c> already subsumes <c>null</c>, so <see cref="TsType.NullableOf"/> is a
/// no-op there); the assertions accept either an <c>unknown</c> primitive or a nullable union whose inner is
/// <c>unknown</c> so the property stays robust to that modelling choice.
/// </para>
/// </remarks>
public sealed class UnmappableMemberDegradationPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The mapper under test is a pure function; one shared instance is safe across all cases.</summary>
    private static readonly TypeMapper Mapper = new();

    /// <summary>A short, non-empty lowercase identifier used for view and property names in a notice.</summary>
    private static readonly Gen<string> Identifier =
        Gen.Char['a', 'z'].Array[1, 16].Select(chars => new string(chars));

    /// <summary>
    /// Permissive/unconstrained object schemas that must degrade with a <c>PermissiveObjectMember</c> notice
    /// (Requirement 3.6), spanning the three shapes the mapper treats as permissive: a bare <c>{}</c> schema
    /// (no type, no <c>$ref</c>), an explicit <c>type: object</c>, and an <c>additionalProperties: true</c>
    /// schema. Each is optionally <c>nullable</c>. No <c>$ref</c>, <c>items</c>, <c>oneOf</c>, or <c>enum</c>
    /// is set, so none can slip into a non-degrading branch.
    /// </summary>
    private static readonly Gen<OpenApiSchema> PermissiveSchema =
        from nullable in Gen.Bool
        from shape in Gen.Int[0, 2]
        select shape switch
        {
            // A bare `{}` schema: neither a type nor a $ref — the mapper treats "no type, no ref" as permissive.
            0 => NewSchema(type: null, format: null, nullable: nullable, additionalPropertiesOpen: false),
            // An explicit object schema — structured expansion is out of the scalar mapper's scope, so it degrades.
            1 => NewSchema(type: "object", format: null, nullable: nullable, additionalPropertiesOpen: false),
            // `additionalProperties: true` — the open-object flag makes it permissive regardless of type.
            _ => NewSchema(type: null, format: null, nullable: nullable, additionalPropertiesOpen: true),
        };

    /// <summary>
    /// String <c>format</c> values that are NOT recognized by the mapper (recognized: none/empty, <c>uuid</c>,
    /// <c>date-time</c>, <c>byte</c>). A <c>string</c> carrying any of these degrades (Requirement 3.7).
    /// </summary>
    private static readonly Gen<string> UnrecognizedStringFormat =
        Gen.OneOfConst(
            "email", "uri", "hostname", "ipv4", "ipv6", "password", "binary", "binary-x",
            "int32", "int64", "double", "float", "date", "time", "duration", "decimal");

    /// <summary>
    /// <c>type</c> values the mapper does not recognize at all (recognized types: <c>integer</c>,
    /// <c>number</c>, <c>boolean</c>, <c>string</c>; <c>object</c>/<c>array</c> take their own branches). Any
    /// of these degrades to <c>unknown</c> with an <c>UnrecognizedScalar</c> notice (Requirement 3.7).
    /// </summary>
    private static readonly Gen<string> UnrecognizedType =
        Gen.OneOfConst("decimal128", "geo", "money", "bigint", "char", "text", "blob", "date", "timestamp");

    /// <summary>
    /// Scalar schemas with an unrecognized <c>type</c>/<c>format</c> combination that must degrade with an
    /// <c>UnrecognizedScalar</c> notice (Requirement 3.7): either a <c>string</c> with an unrecognized format,
    /// or an unknown scalar type (with an arbitrary optional format). Each is optionally <c>nullable</c>. No
    /// enum is set, so a <c>string</c> case never becomes a literal union.
    /// </summary>
    private static readonly Gen<OpenApiSchema> UnrecognizedScalarSchema =
        from nullable in Gen.Bool
        from kind in Gen.Int[0, 1]
        from badFormat in UnrecognizedStringFormat
        from unknownType in UnrecognizedType
        from carryFormat in Gen.Bool
        select kind == 0
            ? NewSchema(type: "string", format: badFormat, nullable: nullable, additionalPropertiesOpen: false)
            : NewSchema(
                type: unknownType,
                format: carryFormat ? badFormat : null,
                nullable: nullable,
                additionalPropertiesOpen: false);

    /// <summary>Pairs a degrading schema with the view and property names that a notice must name.</summary>
    private static Gen<(string View, string Property, OpenApiSchema Schema)> Cases(Gen<OpenApiSchema> schemas) =>
        from view in Identifier
        from property in Identifier
        from schema in schemas
        select (view, property, schema);

    // Feature: typescript-client, Property 4: Unmappable members degrade to unknown with a notice, never
    // omitted, never fatal
    //
    // For any permissive/unconstrained object member ({} schema, `type: object`, or
    // `additionalProperties: true`), mapping it degrades to `unknown`, records exactly one non-fatal
    // PermissiveObjectMember notice naming the view and property, never throws, and never omits the member
    // (Map returns a non-null type).
    //
    // Validates: Requirements 3.6
    [Test]
    public void Permissive_Object_Member_Degrades_To_Unknown_With_A_Notice()
    {
        Cases(PermissiveSchema).Sample(
            @case =>
            {
                var (view, property, schema) = @case;
                AssertDegradesWithNotice(view, property, schema, GenerationNoticeKind.PermissiveObjectMember);
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 4: Unmappable members degrade to unknown with a notice, never
    // omitted, never fatal
    //
    // For any scalar schema whose `type`/`format` the mapper does not recognize (a `string` with an
    // unrecognized format, or an unknown scalar type), mapping it degrades to `unknown`, records exactly one
    // non-fatal UnrecognizedScalar notice naming the view and property, never throws, and never omits the
    // member.
    //
    // Validates: Requirements 3.7
    [Test]
    public void Unrecognized_Scalar_Degrades_To_Unknown_With_A_Notice()
    {
        Cases(UnrecognizedScalarSchema).Sample(
            @case =>
            {
                var (view, property, schema) = @case;
                AssertDegradesWithNotice(view, property, schema, GenerationNoticeKind.UnrecognizedScalar);
            },
            iter: Iterations);
    }

    /// <summary>
    /// The shared oracle for both degradation kinds: mapping the schema must not throw (never fatal), must
    /// return a non-null <c>unknown</c>-bearing type (never omitted, degrades to <c>unknown</c>), and must
    /// record exactly one notice of the expected kind that names the view and property.
    /// </summary>
    private static void AssertDegradesWithNotice(
        string view,
        string property,
        OpenApiSchema schema,
        GenerationNoticeKind expectedKind)
    {
        var notices = new NoticeCollector();

        // Never fatal: an unmappable member must be reported, not thrown.
        TsType mapped;
        try
        {
            mapped = Mapper.Map(schema, view, property, notices);
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"Mapping a degrading schema (type='{schema.Type ?? "(null)"}', " +
                $"format='{schema.Format ?? "(null)"}', additionalPropertiesOpen={schema.AdditionalPropertiesOpen}) " +
                $"threw {ex.GetType().Name}: {ex.Message}. Degradation must never be fatal.");
        }

        // Never omitted: the mapper always yields a type for the member.
        if (mapped is null)
        {
            throw new Exception("Mapping a degrading schema returned null; the member must never be omitted.");
        }

        // Degrades to `unknown` (or a nullable union whose inner is `unknown`).
        if (!IsUnknownBearing(mapped))
        {
            throw new Exception(
                $"A degrading schema mapped to '{mapped.Render()}', expected 'unknown' (or an unknown-bearing " +
                "nullable union).");
        }

        // Exactly one notice was recorded for the single degraded member.
        if (notices.Count != 1)
        {
            throw new Exception(
                $"Expected exactly one notice for a single degraded member, but the collector holds {notices.Count}.");
        }

        var notice = notices.ToSortedList().Single();

        if (notice.Kind != expectedKind)
        {
            throw new Exception(
                $"Degraded member recorded a '{notice.Kind}' notice, expected '{expectedKind}'.");
        }

        // The notice must name the exact view and property so a human can locate the degraded member.
        if (!string.Equals(notice.View, view, StringComparison.Ordinal))
        {
            throw new Exception($"Notice named view '{notice.View}', expected '{view}'.");
        }

        if (!string.Equals(notice.Property, property, StringComparison.Ordinal))
        {
            throw new Exception($"Notice named property '{notice.Property}', expected '{property}'.");
        }
    }

    /// <summary>
    /// A schema record with only the members a degrading case needs; every collection member is left empty
    /// or null so no non-degrading branch (<c>$ref</c>, <c>array</c>, <c>enum</c>) can be taken.
    /// </summary>
    private static OpenApiSchema NewSchema(string? type, string? format, bool nullable, bool additionalPropertiesOpen) =>
        new(
            Ref: null,
            Type: type,
            Format: format,
            Nullable: nullable,
            Required: Array.Empty<string>(),
            Properties: null,
            Items: null,
            OneOf: null,
            Enum: null,
            AdditionalPropertiesOpen: additionalPropertiesOpen);

    /// <summary>
    /// True when the type is the <c>unknown</c> primitive or a nullable union whose inner is
    /// <c>unknown</c> — the two shapes a degraded member is allowed to take.
    /// </summary>
    private static bool IsUnknownBearing(TsType type) => type switch
    {
        TsPrimitive primitive => primitive.Kind == TsPrimitiveKind.Unknown,
        TsNullable nullable => IsUnknownBearing(nullable.Inner),
        _ => false,
    };
}
