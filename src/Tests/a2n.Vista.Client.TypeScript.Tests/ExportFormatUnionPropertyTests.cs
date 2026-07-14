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
/// Property-based test for the <em>generator side</em> of the export format union (task 9.4). It asserts
/// that when the document declares an export request whose <c>format</c> property is a string enum of the
/// formats the view supports, the generated TypeScript for that <c>format</c> member is a string-literal
/// union containing <em>exactly</em> those values, in the document's order — no extra members, none
/// omitted, and never reordered (Requirement 4.7, built on the string-enum → literal-union rule R2.2/R3.2
/// and the deterministic document order R9.2). The runtime raw-payload half of Property 19 (an export
/// success preserves the response body as raw, unparsed bytes/text) is covered separately by task 14.7.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the export format union lives.</b> The export operation posts a request body whose
/// <c>format</c> property selects the export format. In the canonical fixture that property is a plain
/// <em>nullable string</em> (<c>VistaListRequestBody.format</c>), so no literal union is emitted there; the
/// union only materialises when the document constrains <c>format</c> to a string <c>enum</c>. The literal
/// union is produced by the pure modeling layer: <see cref="DtoModelBuilder.BuildDecl(string,
/// OpenApiSchema, NoticeCollector)"/> maps the request object's properties through
/// <see cref="TypeMapper"/>, which turns a string enum into a <see cref="TsLiteralUnion"/> in document
/// order (<see cref="TypeMapper.Map"/> step 4). This test drives that deterministic layer directly: it
/// builds an export request object with a <c>format</c> string-enum property (plus the realistic
/// <c>page</c>/<c>pageSize</c> paging scalars), models it, and asserts the emitted <c>format</c> member's
/// type equals the declared formats, sequence-for-sequence.
/// </para>
/// <para>
/// Validates: Requirement 4.7.
/// </para>
/// </remarks>
public sealed class ExportFormatUnionPropertyTests
{
    /// <summary>Minimum generated cases required for each property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>Characters used to build verbatim format identifiers (alphanumeric — no escaping needed).</summary>
    private static readonly char[] IdentChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    /// <summary>A non-empty verbatim format identifier (e.g. <c>csv</c>, <c>xlsx3</c>).</summary>
    private static readonly Gen<string> FormatIdGen =
        Gen.Int[0, IdentChars.Length - 1].Array[1, 10]
            .Select(idx => new string(Array.ConvertAll(idx, i => IdentChars[i])));

    /// <summary>
    /// A non-empty list of <em>distinct</em> format identifiers in the order generated — an arbitrary set
    /// of declared export formats in an arbitrary document order.
    /// </summary>
    private static readonly Gen<IReadOnlyList<string>> RandomFormatSets =
        FormatIdGen.Array[1, 6]
            .Select(a => (IReadOnlyList<string>)a.Distinct(StringComparer.Ordinal).ToArray());

    /// <summary>The realistic export formats, used to generate random-order distinct subsets.</summary>
    private static readonly string[] KnownFormats = { "csv", "xlsx", "json", "pdf", "tsv", "xml" };

    /// <summary>
    /// A non-empty random-order, distinct subset of the realistic export formats (<c>csv</c>/<c>xlsx</c>/
    /// <c>json</c>/<c>pdf</c>/…). Distinct-on-<c>int[]</c> preserves first-occurrence order, so the mapped
    /// document order is whatever order the indices were drawn in.
    /// </summary>
    private static readonly Gen<IReadOnlyList<string>> KnownFormatSets =
        Gen.Int[0, KnownFormats.Length - 1].Array[1, KnownFormats.Length]
            .Select(idx => (IReadOnlyList<string>)idx.Distinct().Select(i => KnownFormats[i]).ToArray());

    /// <summary>Both flavours of declared-format sets: arbitrary identifiers and realistic subsets.</summary>
    private static readonly Gen<IReadOnlyList<string>> FormatSets =
        Gen.OneOf(RandomFormatSets, KnownFormatSets);

    // A plain int32 paging scalar, so the export request object is realistic (format + page + pageSize).
    private static OpenApiSchema Int32 =>
        new(Ref: null, Type: "integer", Format: "int32", Nullable: false,
            Required: Array.Empty<string>(), Properties: null, Items: null,
            OneOf: null, Enum: null, AdditionalPropertiesOpen: false);

    // An export request object whose `format` property is a string enum of `formats`, in document order.
    private static OpenApiSchema ExportRequest(IReadOnlyList<string> formats, bool nullableFormat) =>
        new(Ref: null,
            Type: "object",
            Format: null,
            Nullable: false,
            Required: new[] { "format" },
            Properties: new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["format"] = new(Ref: null, Type: "string", Format: null, Nullable: nullableFormat,
                    Required: Array.Empty<string>(), Properties: null, Items: null,
                    OneOf: null, Enum: formats, AdditionalPropertiesOpen: false),
                ["page"] = Int32,
                ["pageSize"] = Int32,
            },
            Items: null,
            OneOf: null,
            Enum: null,
            AdditionalPropertiesOpen: false);

    // Models the export request and returns the mapped `format` member's type expression.
    private static TsType MapFormatMember(IReadOnlyList<string> formats, bool nullableFormat)
    {
        var notices = new NoticeCollector();
        var decl = new DtoModelBuilder().BuildDecl("CustomersExportRequest", ExportRequest(formats, nullableFormat), notices);
        return decl.Members.Single(member => member.Name == "format").Type;
    }

    // The TypeScript literal-union rendering the design prescribes for a set of identifier formats.
    private static string ExpectedUnion(IReadOnlyList<string> formats) =>
        string.Join(" | ", formats.Select(value => $"\"{value}\""));

    // Feature: typescript-client, Property 19: Export format union and raw payload preservation
    //
    // The emitted `format` literal union equals EXACTLY the document's declared export formats, in document
    // order — no extra members, none omitted, none reordered. Validates: Requirement 4.7.
    [Test]
    public void Format_Enum_Maps_To_Literal_Union_Equal_To_Declared_Formats_In_Document_Order()
    {
        FormatSets.Sample(
            formats =>
            {
                var type = MapFormatMember(formats, nullableFormat: false);

                if (type is not TsLiteralUnion union)
                {
                    throw new Exception(
                        $"Export `format` enum [{string.Join(", ", formats)}] mapped to '{type.Render()}', " +
                        "expected a string-literal union.");
                }

                if (!union.Literals.SequenceEqual(formats, StringComparer.Ordinal))
                {
                    throw new Exception(
                        $"Export `format` union was [{string.Join(", ", union.Literals)}], expected exactly the " +
                        $"declared formats in document order [{string.Join(", ", formats)}] — no extra, omitted, " +
                        "or reordered members.");
                }

                var rendered = union.Render();
                var expected = ExpectedUnion(formats);
                if (!string.Equals(rendered, expected, StringComparison.Ordinal))
                {
                    throw new Exception($"Export `format` union rendered as '{rendered}', expected '{expected}'.");
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 19: Export format union and raw payload preservation
    //
    // A nullable `format` enum maps to `<union> | null` whose inner literal union still equals exactly the
    // declared formats in document order (the fixture's `format` is nullable). Validates: Requirement 4.7.
    [Test]
    public void Nullable_Format_Enum_Maps_To_Nullable_Union_Over_The_Declared_Formats()
    {
        FormatSets.Sample(
            formats =>
            {
                var type = MapFormatMember(formats, nullableFormat: true);

                if (type is not TsNullable { Inner: TsLiteralUnion union })
                {
                    throw new Exception(
                        $"Nullable export `format` enum [{string.Join(", ", formats)}] mapped to '{type.Render()}', " +
                        "expected a nullable union over a string-literal union.");
                }

                if (!union.Literals.SequenceEqual(formats, StringComparer.Ordinal))
                {
                    throw new Exception(
                        $"Nullable export `format` union was [{string.Join(", ", union.Literals)}], expected exactly " +
                        $"the declared formats in document order [{string.Join(", ", formats)}].");
                }

                var rendered = type.Render();
                var expected = $"{ExpectedUnion(formats)} | null";
                if (!string.Equals(rendered, expected, StringComparison.Ordinal))
                {
                    throw new Exception($"Nullable export `format` member rendered as '{rendered}', expected '{expected}'.");
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 19: Export format union and raw payload preservation
    //
    // The union tracks DOCUMENT order, not a canonical sort: reversing the declared format order reverses
    // the emitted union exactly. This rules out an accidental alphabetical sort masquerading as fidelity.
    // Validates: Requirement 4.7.
    [Test]
    public void Format_Union_Order_Tracks_Document_Order_Not_A_Canonical_Sort()
    {
        FormatSets.Sample(
            formats =>
            {
                var reversed = formats.Reverse().ToArray();

                var forward = MapFormatMember(formats, nullableFormat: false);
                var backward = MapFormatMember(reversed, nullableFormat: false);

                if (forward is not TsLiteralUnion forwardUnion || backward is not TsLiteralUnion backwardUnion)
                {
                    throw new Exception("Expected both the forward and reversed export `format` members to be literal unions.");
                }

                if (!forwardUnion.Literals.SequenceEqual(formats, StringComparer.Ordinal))
                {
                    throw new Exception(
                        $"Forward export `format` union was [{string.Join(", ", forwardUnion.Literals)}], expected " +
                        $"[{string.Join(", ", formats)}].");
                }

                if (!backwardUnion.Literals.SequenceEqual(reversed, StringComparer.Ordinal))
                {
                    throw new Exception(
                        $"Reversed export `format` union was [{string.Join(", ", backwardUnion.Literals)}], expected " +
                        $"[{string.Join(", ", reversed)}]; the emitted order must follow the document, not a sort.");
                }
            },
            iter: Iterations);
    }
}
