// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using a2n.Vista.Client.TypeScript.Emit;
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
/// Unit tests for the <c>types.ts</c> emitter (task 9.2; Requirements 2.1, 2.4, 2.5, 2.6, 3.1–3.4). They
/// assert the emitter produces one declaration per envelope/DTO, emits the single generic result pair once
/// (never the monomorphized components), keeps the <c>FilterNode</c> family cross-file via an
/// <c>import type</c>, orders top-level declarations by name, is byte-for-byte deterministic, and propagates
/// a missing per-view DTO as a fatal <see cref="GenerationError.MissingSchema"/>.
/// </summary>
public sealed class TypesEmitterTests
{
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

    // Builds the emit input from the fixture: read-surface envelopes, the re-lift outcome, and the single
    // CustomerRow DTO the one fixture view exposes.
    private static TypesEmitInput BuildInput(
        ResolvedDocument document,
        NoticeCollector notices,
        IReadOnlyCollection<string>? dtoNames = null)
    {
        var envelopes = new EnvelopeCatalog().Bind(document, includeWriteEnvelopes: false);
        if (envelopes.IsError)
        {
            throw new Exception($"Fixture envelope binding failed: {envelopes.Error.Message}");
        }

        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(document, notices);

        return new TypesEmitInput(
            document,
            envelopes.Value,
            reLift,
            dtoNames ?? new[] { "CustomerRow" },
            notices);
    }

    [Test]
    public async Task Emits_The_types_ts_Path()
    {
        var document = ResolveFixture();
        var result = TypesEmitter.Emit(BuildInput(document, new NoticeCollector()));

        await Assert.That(result.IsOk).IsTrue();
        await Assert.That(result.Value.RelativePath).IsEqualTo("types.ts");
    }

    [Test]
    public async Task Declares_Each_Envelope_And_Dto_Exactly_Once()
    {
        var document = ResolveFixture();
        var content = TypesEmitter.Emit(BuildInput(document, new NoticeCollector())).Value.Content;

        // Every bound read-surface envelope, ProblemDetails, and the per-view DTO are present, each declared
        // exactly once (Requirements 2.1, 2.5).
        foreach (var name in new[]
                 {
                     "VistaListRequestBody", "VistaSortBody", "VistaDetailRequestBody", "VistaMetadataResponse",
                     "VistaFieldMetadataResponse", "ProblemDetails", "CustomerRow",
                 })
        {
            await Assert.That(DeclarationCount(content, name)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Emits_The_Single_Generic_Result_Pair_Once_And_Not_The_Monomorphized_Component()
    {
        var document = ResolveFixture();
        var content = TypesEmitter.Emit(BuildInput(document, new NoticeCollector())).Value.Content;

        // The generic pair is declared once, with a TRow type parameter (Requirement 2.6).
        await Assert.That(content).Contains("export interface PagedResult<TRow> {");
        await Assert.That(content).Contains("export interface ViewListResult<TRow> {");
        await Assert.That(content).Contains("page: PagedResult<TRow>;");
        await Assert.That(content).Contains("items: TRow[];");

        // The monomorphized ViewListResult_CustomerRow is re-lifted, never emitted (Requirement 2.6).
        await Assert.That(content).DoesNotContain("ViewListResult_CustomerRow");
    }

    [Test]
    public async Task References_The_FilterNode_Family_Cross_File_Via_Import_Type()
    {
        var document = ResolveFixture();
        var content = TypesEmitter.Emit(BuildInput(document, new NoticeCollector())).Value.Content;

        // VistaListRequestBody.filter/scope reference FilterNode, which lives in ./filter-node — imported,
        // never redeclared here (Requirement 2.5).
        await Assert.That(content).Contains("import type { FilterNode } from \"./filter-node\";");
        await Assert.That(content).DoesNotContain("export interface FilterNode");
        await Assert.That(content).DoesNotContain("export interface FilterLeaf");
    }

    [Test]
    public async Task Emits_ProblemDetails_With_Document_Declared_Nullability_And_Optionality()
    {
        var document = ResolveFixture();
        var content = TypesEmitter.Emit(BuildInput(document, new NoticeCollector())).Value.Content;

        // Each RFC 7807 member plus the Vista `code` extension, all nullable and optional as the document
        // declares (Requirement 2.4).
        foreach (var member in new[] { "type", "title", "detail", "instance", "code" })
        {
            await Assert.That(content).Contains($"{member}?: string | null;");
        }

        await Assert.That(content).Contains("status?: number | null;");
    }

    [Test]
    public async Task Orders_Top_Level_Declarations_By_Name()
    {
        var document = ResolveFixture();
        var content = TypesEmitter.Emit(BuildInput(document, new NoticeCollector())).Value.Content;

        var declarationOrder = content
            .Split('\n')
            .Where(line => line.StartsWith("export interface ", StringComparison.Ordinal))
            .Select(line => line["export interface ".Length..].Split('<', ' ')[0])
            .ToArray();

        var sortedOrder = declarationOrder.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        await Assert.That(declarationOrder).IsEquivalentTo(sortedOrder);
    }

    [Test]
    public async Task Is_Byte_For_Byte_Deterministic_Across_Runs()
    {
        var document = ResolveFixture();
        var first = TypesEmitter.Emit(BuildInput(document, new NoticeCollector())).Value.Content;
        var second = TypesEmitter.Emit(BuildInput(document, new NoticeCollector())).Value.Content;

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(first).EndsWith("\n");
        await Assert.That(first).DoesNotContain("\r");
    }

    [Test]
    public async Task Missing_Per_View_Dto_Aborts_With_MissingSchema_Naming_It()
    {
        var document = ResolveFixture();
        var input = BuildInput(document, new NoticeCollector(), new[] { "NotAComponent" });

        var result = TypesEmitter.Emit(input);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error is GenerationError.MissingSchema { SchemaName: "NotAComponent" }).IsTrue();
    }

    [Test]
    public async Task Emits_A_Blank_Line_Between_Declarations_And_A_Header()
    {
        var document = ResolveFixture();
        var content = TypesEmitter.Emit(BuildInput(document, new NoticeCollector())).Value.Content;

        // The generated header is present, and declarations are separated by a single blank line.
        await Assert.That(content).StartsWith("// <auto-generated>");
        await Assert.That(content).Contains("}\n\nexport interface ");
        await Assert.That(content).DoesNotContain("}\n\n\nexport interface ");
    }

    // Counts how many times a top-level `export interface {name}` (optionally generic) is declared.
    private static int DeclarationCount(string content, string name) => content
        .Split('\n')
        .Count(line =>
            line.StartsWith($"export interface {name} ", StringComparison.Ordinal)
            || line.StartsWith($"export interface {name}<", StringComparison.Ordinal)
            || line.StartsWith($"export interface {name} {{", StringComparison.Ordinal));
}
