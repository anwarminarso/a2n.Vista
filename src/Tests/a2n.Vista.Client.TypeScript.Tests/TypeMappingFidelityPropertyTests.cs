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
/// Property-based test for the scalar type mapper's <em>fidelity to the schema</em> (task 6.2). For any
/// schema drawn from the <em>recognized</em> mapping space, <see cref="TypeMapper.Map"/> and
/// <see cref="TypeMapper.MapProperty"/> reproduce exactly what the design's "Scalar type mapping table"
/// and its modifier rules prescribe — the correct primitive, a literal union in document order, a null
/// admission for nullable members, an array over the mapped element, verbatim/optional property modelling,
/// and a named reference for a <c>$ref</c> — never degrading a recognized member to <c>unknown</c>.
/// </summary>
/// <remarks>
/// Degradation to <c>unknown</c> (Requirements 3.6, 3.7) is the subject of the companion task 6.3, so every
/// generator here is constructed to stay strictly inside the recognized space: recognized scalar
/// <c>type</c>/<c>format</c> pairs only, string enums, arrays over recognized items, and local
/// <c>$ref</c>s. Each property therefore also asserts that <em>no</em> notice was recorded, which is the
/// structural guarantee that the case never left the recognized space.
///
/// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 2.4.
/// </remarks>
public sealed class TypeMappingFidelityPropertyTests
{
    /// <summary>Minimum generated cases required for each property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    private const string ViewContext = "SampleView";

    /// <summary>Characters used to build verbatim, mixed-case camelCase-style property/enum names.</summary>
    private static readonly char[] NameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    /// <summary>A non-empty verbatim identifier with mixed casing (never altered by the mapper, R3.1).</summary>
    private static readonly Gen<string> NameGen =
        Gen.Int[0, NameChars.Length - 1].Array[1, 12]
            .Select(idx => new string(Array.ConvertAll(idx, i => NameChars[i])));

    /// <summary>
    /// A recognized scalar schema paired with the primitive the design's table prescribes (R3.5):
    /// <c>integer</c>/<c>number</c> → <c>number</c>, <c>boolean</c> → <c>boolean</c>, and
    /// <c>string</c> with a recognized format (none/uuid/date-time/byte) → <c>string</c>.
    /// </summary>
    private static readonly Gen<(string Type, string? Format, TsType Expected)> RecognizedScalar =
        Gen.OneOf(
            from f in Gen.OneOfConst<string?>(null, "int32", "int64")
            select ("integer", f, TsType.Number),
            from f in Gen.OneOfConst<string?>(null, "double", "float")
            select ("number", f, TsType.Number),
            from _ in Gen.OneOfConst(0)
            select ("boolean", (string?)null, TsType.Boolean),
            from f in Gen.OneOfConst<string?>(null, "uuid", "date-time", "byte")
            select ("string", f, TsType.String));

    /// <summary>A non-empty list of distinct enum literal values, in the order generated (R3.2 order).</summary>
    private static readonly Gen<IReadOnlyList<string>> EnumValues =
        NameGen.Array[1, 6]
            .Select(a => (IReadOnlyList<string>)a.Distinct(StringComparer.Ordinal).ToArray());

    private static OpenApiSchema Scalar(string type, string? format, bool nullable) =>
        new(Ref: null, Type: type, Format: format, Nullable: nullable,
            Required: Array.Empty<string>(), Properties: null, Items: null,
            OneOf: null, Enum: null, AdditionalPropertiesOpen: false);

    private static OpenApiSchema StringEnum(IReadOnlyList<string> values, bool nullable) =>
        new(Ref: null, Type: "string", Format: null, Nullable: nullable,
            Required: Array.Empty<string>(), Properties: null, Items: null,
            OneOf: null, Enum: values, AdditionalPropertiesOpen: false);

    private static OpenApiSchema ArrayOf(OpenApiSchema item, bool nullable) =>
        new(Ref: null, Type: "array", Format: null, Nullable: nullable,
            Required: Array.Empty<string>(), Properties: null, Items: item,
            OneOf: null, Enum: null, AdditionalPropertiesOpen: false);

    private static OpenApiSchema RefSchema(string name) =>
        new(Ref: $"#/components/schemas/{name}", Type: null, Format: null, Nullable: false,
            Required: Array.Empty<string>(), Properties: null, Items: null,
            OneOf: null, Enum: null, AdditionalPropertiesOpen: false);

    // A type that "includes null": either a nullable union wrapper, or a primitive that already subsumes
    // null (unknown/null). For the recognized space exercised here, the base is always number/boolean/
    // string/literal-union/array, so a nullable case must be wrapped in TsNullable.
    private static bool AdmitsNull(TsType type) => type switch
    {
        TsNullable => true,
        TsPrimitive { Kind: TsPrimitiveKind.Unknown } => true,
        TsPrimitive { Kind: TsPrimitiveKind.Null } => true,
        _ => false,
    };

    // Feature: typescript-client, Property 3: Type-mapping fidelity to the schema
    //
    // A recognized scalar schema maps to exactly the TypeScript primitive the design's table prescribes
    // (integer/number → number, boolean → boolean, string with a recognized format → string), with no
    // notice recorded (the case stays in the recognized space). Validates: Requirement 3.5.
    [Test]
    public void Recognized_Scalar_Maps_To_The_Prescribed_Primitive()
    {
        RecognizedScalar.Sample(
            testCase =>
            {
                var (type, format, expected) = testCase;
                var notices = new NoticeCollector();
                var mapper = new TypeMapper();

                var result = mapper.Map(Scalar(type, format, nullable: false), ViewContext, "prop", notices);

                if (!result.Equals(expected))
                {
                    throw new Exception(
                        $"Scalar type='{type}', format='{format ?? "<none>"}' mapped to " +
                        $"'{result.Render()}', expected '{expected.Render()}'.");
                }

                if (notices.Count != 0)
                {
                    throw new Exception(
                        $"Recognized scalar type='{type}', format='{format ?? "<none>"}' recorded " +
                        $"{notices.Count} notice(s); a recognized member must not degrade.");
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 3: Type-mapping fidelity to the schema
    //
    // A string enum maps to a string-literal union whose literals equal the schema's values in the SAME
    // order, with no extra members. Validates: Requirements 3.2, 2.4.
    [Test]
    public void String_Enum_Maps_To_Literal_Union_In_Document_Order()
    {
        EnumValues.Sample(
            values =>
            {
                var notices = new NoticeCollector();
                var mapper = new TypeMapper();

                var result = mapper.Map(StringEnum(values, nullable: false), ViewContext, "prop", notices);

                if (result is not TsLiteralUnion union)
                {
                    throw new Exception(
                        $"String enum [{string.Join(", ", values)}] mapped to '{result.Render()}', " +
                        "expected a string-literal union.");
                }

                if (!union.Literals.SequenceEqual(values, StringComparer.Ordinal))
                {
                    throw new Exception(
                        $"Literal union was [{string.Join(", ", union.Literals)}], expected the document " +
                        $"order [{string.Join(", ", values)}] with no extra or reordered members.");
                }

                if (notices.Count != 0)
                {
                    throw new Exception($"A recognized string enum recorded {notices.Count} notice(s).");
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 3: Type-mapping fidelity to the schema
    //
    // A nullable recognized scalar (or nullable string enum) maps to a type that includes null: it is
    // wrapped in TsNullable whose inner is the non-nullable mapping. Validates: Requirement 3.3.
    [Test]
    public void Nullable_Recognized_Member_Includes_Null()
    {
        Gen.OneOf(
                RecognizedScalar.Select(s => (Schema: Scalar(s.Type, s.Format, nullable: true), Base: s.Expected)),
                EnumValues.Select(v => (Schema: StringEnum(v, nullable: true), Base: (TsType)TsType.LiteralUnion(v))))
            .Sample(
                testCase =>
                {
                    var (schema, expectedBase) = testCase;
                    var notices = new NoticeCollector();
                    var mapper = new TypeMapper();

                    var result = mapper.Map(schema, ViewContext, "prop", notices);

                    if (!AdmitsNull(result))
                    {
                        throw new Exception(
                            $"Nullable member mapped to '{result.Render()}', which does not include null.");
                    }

                    if (result is not TsNullable nullable || !nullable.Inner.Equals(expectedBase))
                    {
                        throw new Exception(
                            $"Nullable member mapped to '{result.Render()}', expected a nullable union over " +
                            $"'{expectedBase.Render()}'.");
                    }

                    if (notices.Count != 0)
                    {
                        throw new Exception($"A recognized nullable member recorded {notices.Count} notice(s).");
                    }
                },
                iter: Iterations);
    }

    // Feature: typescript-client, Property 3: Type-mapping fidelity to the schema
    //
    // An array over a recognized item maps to TsArray of the mapped element; a nullable array is wrapped in
    // a nullable union over that array. Validates: Requirements 3.5, 3.3.
    [Test]
    public void Array_Over_Recognized_Item_Maps_To_Array_Of_Mapped_Element()
    {
        (from scalar in RecognizedScalar
         from nullable in Gen.Bool
         select (scalar, nullable))
            .Sample(
                testCase =>
                {
                    var (scalar, nullable) = testCase;
                    var notices = new NoticeCollector();
                    var mapper = new TypeMapper();

                    var schema = ArrayOf(Scalar(scalar.Type, scalar.Format, nullable: false), nullable);
                    var result = mapper.Map(schema, ViewContext, "prop", notices);

                    // Peel the optional nullable wrapper introduced by the array's own `nullable` flag.
                    TsType arrayPart = result;
                    if (nullable)
                    {
                        if (result is not TsNullable wrapper)
                        {
                            throw new Exception(
                                $"Nullable array mapped to '{result.Render()}', expected a nullable union.");
                        }

                        arrayPart = wrapper.Inner;
                    }

                    if (arrayPart is not TsArray array)
                    {
                        throw new Exception(
                            $"Array schema mapped to '{result.Render()}', expected an array type.");
                    }

                    if (!array.Element.Equals(scalar.Expected))
                    {
                        throw new Exception(
                            $"Array element mapped to '{array.Element.Render()}', expected " +
                            $"'{scalar.Expected.Render()}'.");
                    }

                    if (notices.Count != 0)
                    {
                        throw new Exception($"A recognized array recorded {notices.Count} notice(s).");
                    }
                },
                iter: Iterations);
    }

    // Feature: typescript-client, Property 3: Type-mapping fidelity to the schema
    //
    // MapProperty models optionality as the negation of required (Optional == !required, R3.4) and carries
    // the property name verbatim and case-sensitively (R3.1). Validates: Requirements 3.4, 3.1.
    [Test]
    public void MapProperty_Sets_Optional_As_Not_Required_And_Preserves_The_Name_Verbatim()
    {
        (from name in NameGen
         from scalar in RecognizedScalar
         from required in Gen.Bool
         select (name, scalar, required))
            .Sample(
                testCase =>
                {
                    var (name, scalar, required) = testCase;
                    var notices = new NoticeCollector();
                    var mapper = new TypeMapper();

                    var property = mapper.MapProperty(
                        name, Scalar(scalar.Type, scalar.Format, nullable: false), required, ViewContext, notices);

                    if (property.Optional != !required)
                    {
                        throw new Exception(
                            $"Property '{name}' (required={required}) had Optional={property.Optional}, " +
                            $"expected {!required}.");
                    }

                    if (!string.Equals(property.Name, name, StringComparison.Ordinal))
                    {
                        throw new Exception(
                            $"Property name was '{property.Name}', expected the verbatim, case-sensitive " +
                            $"'{name}'.");
                    }

                    if (!property.Type.Equals(scalar.Expected))
                    {
                        throw new Exception(
                            $"Property '{name}' type was '{property.Type.Render()}', expected " +
                            $"'{scalar.Expected.Render()}'.");
                    }
                },
                iter: Iterations);
    }

    // Feature: typescript-client, Property 3: Type-mapping fidelity to the schema
    //
    // A local component $ref maps to a named reference whose name is the ref's trailing segment, with no
    // notice recorded. Validates: Requirement 2.4 (referenced-by-name fidelity), 3.5.
    [Test]
    public void Ref_Maps_To_Named_Type_With_The_Trailing_Name()
    {
        NameGen.Sample(
            name =>
            {
                var notices = new NoticeCollector();
                var mapper = new TypeMapper();

                var result = mapper.Map(RefSchema(name), ViewContext, "prop", notices);

                if (result is not TsNamed named)
                {
                    throw new Exception(
                        $"$ref to '{name}' mapped to '{result.Render()}', expected a named type.");
                }

                if (!string.Equals(named.Name, name, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"$ref named type was '{named.Name}', expected the trailing '{name}'.");
                }

                if (notices.Count != 0)
                {
                    throw new Exception($"A $ref recorded {notices.Count} notice(s); it must not degrade.");
                }
            },
            iter: Iterations);
    }
}
