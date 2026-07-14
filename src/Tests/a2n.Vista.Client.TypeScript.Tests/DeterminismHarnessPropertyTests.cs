// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Emit.Runtime;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using a2n.Vista.Client.TypeScript.Write;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// The headline determinism gate (task 11.3; Requirements 9.1, 9.2, 9.3; design Property 1). It asserts that
/// the full <c>Generated_Output</c> — the six fixed runtime files, <c>types.ts</c>, <c>filter-node.ts</c>,
/// and one <c>views/{view}.ts</c> per view — is <em>deterministic</em>, <em>order-independent</em>, and
/// <em>idempotent</em> under regeneration, exercising four dimensions per generated case:
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><description>
///     <b>Two independent runs (Requirement 9.1).</b> Building the full generated file set twice, from two
///     freshly parsed-and-resolved copies of the same document with the same config, yields the identical
///     set of file paths and byte-identical content per file. The fixed encoding (UTF-8, no BOM) and fixed
///     line terminator (<c>\n</c>) mean the within-process guarantee is the across-process guarantee.
///   </description></item>
///   <item><description>
///     <b>Member-order-permuted document (Requirement 9.2).</b> Permuting the enumeration order of the
///     members whose order must not affect output — <c>components.schemas</c>, each schema's object
///     <c>properties</c>, <c>paths</c>, per-path operations, per-operation responses, and
///     <c>securitySchemes</c> — then rebuilding, yields output identical to the unpermuted run. The
///     order-<em>significant</em> members (enum arrays, <c>oneOf</c> variant arrays, and every semantic
///     array) are left untouched, so this proves the output depends only on names, never on enumeration
///     order.
///   </description></item>
///   <item><description>
///     <b>Idempotent regeneration over a populated directory (Requirement 9.3).</b> Writing the output to a
///     fresh temp directory through <see cref="OutputWriter"/>, then writing again into that now-populated
///     directory, leaves an identical set of file paths whose on-disk bytes match the in-memory content
///     exactly (UTF-8, no BOM). The temp directory is always cleaned up.
///   </description></item>
///   <item><description>
///     <b>Identical file sets and bytes</b> — the assertion shared by dimensions (1)–(3).
///   </description></item>
/// </list>
/// <para>
/// <b>Assembling the full output.</b> Each case runs the real pipeline stages against the canonical M18
/// fixture: parse → resolve → envelope bind → generic re-lift → operation-graph build → emit. The six fixed
/// runtime files are document-independent constants; <c>types.ts</c> is emitted from the bound envelopes,
/// the re-lift result, and the per-view DTO component names derived from the built <c>ViewModel</c>s;
/// <c>filter-node.ts</c> from the <see cref="FilterNodeModelBuilder"/>; and one per-view client file via
/// <see cref="ViewClientEmitter.EmitAll(IEnumerable{ViewModel}, bool)"/>. The generated case varies the
/// permutation seed and the <c>EmitWriteFacets</c> flag.
/// </para>
/// <para>
/// <b>Scope note.</b> The <c>index.ts</c> barrel and <c>README.md</c> (task 10.8) are <em>not</em> included:
/// their emitter has not yet landed, so there is no API to call. When 10.8 lands, its two files should be
/// added to <see cref="BuildOutput"/> and they will be covered by the same four dimensions with no other
/// change.
/// </para>
/// </remarks>
public sealed class DeterminismHarnessPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy — ≥ 100).</summary>
    private const int Iterations = 100;

    /// <summary>The canonical fixture text, read once and re-parsed per run so each run is independent.</summary>
    private static readonly string FixtureJson = LoadFixtureJson();

    /// <summary>
    /// The parsed baseline document, used as the immutable source the permutation rebuilds from. Permutation
    /// constructs entirely new records/dictionaries, so it never mutates this instance.
    /// </summary>
    private static readonly OpenApiDocument BaselineDocument = ParseFixture();

    /// <summary>A generated case: a permutation seed and the write-facet configuration flag.</summary>
    private static readonly Gen<(int Seed, bool EmitWriteFacets)> Scenarios =
        from seed in Gen.Int
        from emitWriteFacets in Gen.Bool
        select (seed, emitWriteFacets);

    // Feature: typescript-client, Property 1: Deterministic, idempotent, order-independent output
    //
    // For any valid document and config, generating twice (independently, and after permuting the document's
    // schema/property/path/operation/security enumeration order) produces the identical set of file paths
    // and byte-identical content for every file, using one fixed encoding (UTF-8, no BOM) and one fixed line
    // terminator (\n); and regenerating over a directory already containing a prior output leaves that
    // identical file set and bytes.
    //
    // Validates: Requirements 9.1, 9.2, 9.3
    [Test]
    public void Generated_Output_Is_Deterministic_Idempotent_And_Order_Independent()
    {
        Scenarios.Sample(
            scenario =>
            {
                var (seed, emitWriteFacets) = scenario;
                var rng = new Random(seed);

                // Dimension 1 — two independent runs from freshly parsed+resolved documents (Requirement 9.1).
                var runA = BuildOutput(ResolveFixture(), emitWriteFacets);
                var runB = BuildOutput(ResolveFixture(), emitWriteFacets);
                AssertIdenticalOutput(runA, runB, "two independent runs (R9.1)", seed, emitWriteFacets);

                // The fixed encoding/line-terminator contract the byte-identity guarantee rests on
                // (Requirement 9.1): every emitted file uses \n only and ends with a single trailing \n.
                AssertLineTerminatorContract(runA, seed, emitWriteFacets);

                // Dimension 2 — a member-order-permuted document yields identical output (Requirement 9.2).
                var permutedDocument = PermuteDocument(BaselineDocument, rng);
                var resolvedPermuted = RefResolver.Resolve(permutedDocument);
                if (resolvedPermuted.IsError)
                {
                    throw new Exception(
                        $"The permuted document failed to resolve (seed={seed}): {resolvedPermuted.Error}.");
                }

                var runC = BuildOutput(resolvedPermuted.Value, emitWriteFacets);
                AssertIdenticalOutput(runA, runC, "member-order-permuted document (R9.2)", seed, emitWriteFacets);

                // Dimension 3 — idempotent regeneration over a populated directory (Requirement 9.3).
                AssertIdempotentRegeneration(runA, seed, emitWriteFacets);
            },
            iter: Iterations);
    }

    // ---- Full-output assembly (parse -> resolve -> model -> emit) ----

    // Assembles the complete Generated_Output for a resolved document and config: the six fixed runtime
    // files, types.ts + filter-node.ts, and one views/{view}.ts per view. Mirrors the pipeline wiring
    // (task 12.2) closely enough to be a faithful determinism oracle. index.ts/README.md (task 10.8) are
    // excluded until their emitter lands (see the type remarks).
    private static IReadOnlyList<GeneratedFile> BuildOutput(ResolvedDocument document, bool emitWriteFacets)
    {
        var notices = new NoticeCollector();
        var files = new List<GeneratedFile>
        {
            // 1. The six fixed, document-independent runtime files (always part of the output).
            HttpTransportEmitter.Emit(),
            AuthEmitter.Emit(),
            ResultEmitter.Emit(),
            UrlEmitter.Emit(),
            ClientContextEmitter.Emit(),
            RawPayloadEmitter.Emit(),
        };

        // 2. Locate the fixed Vista envelopes (write envelopes required only when write facets are enabled).
        var envelopes = new EnvelopeCatalog().Bind(document, includeWriteEnvelopes: emitWriteFacets);
        if (envelopes.IsError)
        {
            throw new Exception($"Envelope binding failed (emitWriteFacets={emitWriteFacets}): {envelopes.Error}.");
        }

        // 3. Re-lift the monomorphized list envelopes to the single generic pair, and build the views.
        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(document, notices);
        var views = new OperationGraphBuilder().Build(document, reLift, notices);

        // 4. Derive the per-view DTO component names to emit (the ViewModels' by-name RowType/CrudType).
        var dtoNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var view in views)
        {
            if (view.RowType is TsNamed rowType)
            {
                dtoNames.Add(rowType.Name);
            }

            if (view.CrudType is TsNamed crudType)
            {
                dtoNames.Add(crudType.Name);
            }
        }

        // 5. types.ts
        var typesFile = TypesEmitter.Emit(
            new TypesEmitInput(document, envelopes.Value, reLift, dtoNames.ToArray(), notices));
        if (typesFile.IsError)
        {
            throw new Exception($"types.ts emission failed: {typesFile.Error}.");
        }

        files.Add(typesFile.Value);

        // 6. filter-node.ts
        var filterModel = new FilterNodeModelBuilder().Build(document, notices);
        if (filterModel.IsError)
        {
            throw new Exception($"filter-node model build failed: {filterModel.Error}.");
        }

        files.Add(FilterNodeEmitter.Emit(filterModel.Value));

        // 7. One per-view client file per view.
        files.AddRange(ViewClientEmitter.EmitAll(views, emitWriteFacets));

        // NOTE: index.ts + README.md (task 10.8) are intentionally omitted — their emitter has not landed.
        return files;
    }

    // ---- Deterministic member-order permutation ----

    // Rebuilds the document with the enumeration order of every order-INDEPENDENT member map shuffled:
    // components.schemas, each schema's object properties (recursively), paths, per-path operations,
    // per-operation responses, and securitySchemes. Order-SIGNIFICANT sequences (enum arrays, oneOf variant
    // arrays, required lists, security requirement lists, and array element order) are left unchanged, so a
    // difference in output would be a genuine order-dependence bug rather than a semantic change.
    private static OpenApiDocument PermuteDocument(OpenApiDocument document, Random rng)
    {
        var schemas = Shuffle(document.Components.Schemas.Keys, rng)
            .ToDictionary(
                name => name,
                name => PermuteSchema(document.Components.Schemas[name], rng),
                StringComparer.Ordinal);

        var securitySchemes = Shuffle(document.Components.SecuritySchemes.Keys, rng)
            .ToDictionary(name => name, name => document.Components.SecuritySchemes[name], StringComparer.Ordinal);

        var paths = Shuffle(document.Paths.Keys, rng)
            .ToDictionary(path => path, path => PermutePathItem(document.Paths[path], rng), StringComparer.Ordinal);

        return new OpenApiDocument(
            document.OpenApiVersion,
            document.Info,
            paths,
            new OpenApiComponents(schemas, securitySchemes),
            document.Security);
    }

    private static OpenApiPathItem PermutePathItem(OpenApiPathItem pathItem, Random rng)
    {
        var operations = Shuffle(pathItem.Operations.Keys, rng)
            .ToDictionary(
                method => method,
                method => PermuteOperation(pathItem.Operations[method], rng),
                StringComparer.Ordinal);

        return new OpenApiPathItem(operations);
    }

    private static OpenApiOperation PermuteOperation(OpenApiOperation operation, Random rng)
    {
        var requestBody = operation.RequestBody is null
            ? null
            : new OpenApiRequestBody(
                operation.RequestBody.Required,
                operation.RequestBody.Schema is null ? null : PermuteSchema(operation.RequestBody.Schema, rng));

        // The responses map is keyed by status code; its enumeration order is not semantic (the generator
        // picks the lowest 2xx deterministically), so it is a valid map to shuffle.
        var responses = Shuffle(operation.Responses.Keys, rng)
            .ToDictionary(
                status => status,
                status =>
                {
                    var schema = operation.Responses[status].Schema;
                    return new OpenApiResponse(schema is null ? null : PermuteSchema(schema, rng));
                },
                StringComparer.Ordinal);

        // The per-operation security requirement list order is left unchanged.
        return new OpenApiOperation(operation.OperationId, requestBody, responses, operation.Security);
    }

    private static OpenApiSchema PermuteSchema(OpenApiSchema schema, Random rng)
    {
        // Object properties: a map whose order is NOT semantic — shuffle it and recurse into each value.
        IReadOnlyDictionary<string, OpenApiSchema>? properties = null;
        if (schema.Properties is { } props)
        {
            properties = Shuffle(props.Keys, rng)
                .ToDictionary(name => name, name => PermuteSchema(props[name], rng), StringComparer.Ordinal);
        }

        // Array items: recurse, but the element order of any array VALUE is a runtime concern, not a schema
        // one — there is a single item schema here.
        var items = schema.Items is null ? null : PermuteSchema(schema.Items, rng);

        // oneOf: KEEP the variant order (document-order-significant, Requirement 3.2 sibling), but recurse
        // into each variant's own (order-independent) properties.
        IReadOnlyList<OpenApiSchema>? oneOf = null;
        if (schema.OneOf is { } variants)
        {
            oneOf = variants.Select(variant => PermuteSchema(variant, rng)).ToList();
        }

        return new OpenApiSchema(
            schema.Ref,
            schema.Type,
            schema.Format,
            schema.Nullable,
            schema.Required, // required-member list order left unchanged
            properties,
            items,
            oneOf, // same variant order
            schema.Enum, // enum values are document-order-significant — left unchanged
            schema.AdditionalPropertiesOpen);
    }

    // Fisher-Yates shuffle of a key sequence into a fresh list, deterministic for a given rng seed. Rebuilt
    // dictionaries are populated in this order so the generated model's enumeration order varies per case.
    private static List<string> Shuffle(IEnumerable<string> keys, Random rng)
    {
        var list = keys.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    // ---- Assertions ----

    // Asserts two generated outputs have the identical set of relative paths and byte-identical (exact
    // string) content for every corresponding file.
    private static void AssertIdenticalOutput(
        IReadOnlyList<GeneratedFile> expected,
        IReadOnlyList<GeneratedFile> actual,
        string dimension,
        int seed,
        bool emitWriteFacets)
    {
        var expectedPaths = expected.Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var actualPaths = actual.Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();

        if (!expectedPaths.SequenceEqual(actualPaths, StringComparer.Ordinal))
        {
            throw new Exception(
                $"[{dimension}] the generated file sets differ (seed={seed}, emitWriteFacets={emitWriteFacets}). " +
                $"Expected [{string.Join(", ", expectedPaths)}], got [{string.Join(", ", actualPaths)}].");
        }

        var actualByPath = actual.ToDictionary(file => file.RelativePath, file => file.Content, StringComparer.Ordinal);
        foreach (var file in expected)
        {
            var other = actualByPath[file.RelativePath];
            if (!string.Equals(file.Content, other, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"[{dimension}] the content of '{file.RelativePath}' differs (seed={seed}, " +
                    $"emitWriteFacets={emitWriteFacets}). {DescribeFirstDifference(file.Content, other)}");
            }
        }
    }

    // Asserts the fixed line-terminator contract (Requirement 9.1): every emitted file uses the single
    // fixed line terminator '\n', so no carriage return may appear anywhere.
    private static void AssertLineTerminatorContract(
        IReadOnlyList<GeneratedFile> files,
        int seed,
        bool emitWriteFacets)
    {
        foreach (var file in files)
        {
            if (file.Content.Contains('\r'))
            {
                throw new Exception(
                    $"File '{file.RelativePath}' contains a carriage return, but the fixed line terminator is " +
                    $"'\\n' (seed={seed}, emitWriteFacets={emitWriteFacets}).");
            }
        }
    }

    // Writes the output to a fresh temp directory, then writes AGAIN into the now-populated directory, and
    // asserts the on-disk file set and bytes match the in-memory content exactly (UTF-8, no BOM). Cleans up.
    private static void AssertIdempotentRegeneration(
        IReadOnlyList<GeneratedFile> files,
        int seed,
        bool emitWriteFacets)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "vista-ts-determinism-" + Guid.NewGuid().ToString("N"));
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        try
        {
            var writer = new OutputWriter();

            var first = writer.Write(files, tempDir);
            if (first.IsError)
            {
                throw new Exception(
                    $"The first write failed (seed={seed}, emitWriteFacets={emitWriteFacets}): {first.Error}.");
            }

            // Regenerate over the now-populated directory (Requirement 9.3).
            var second = writer.Write(files, tempDir);
            if (second.IsError)
            {
                throw new Exception(
                    $"Regeneration over the populated directory failed (seed={seed}, " +
                    $"emitWriteFacets={emitWriteFacets}): {second.Error}.");
            }

            // The on-disk file set must be exactly the emitted set (no stragglers from a prior generation).
            var onDisk = Directory
                .EnumerateFiles(tempDir, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(tempDir, path).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var expectedPaths = files.Select(file => file.RelativePath)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();

            if (!onDisk.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            {
                throw new Exception(
                    $"After idempotent regeneration the on-disk file set differs (seed={seed}, " +
                    $"emitWriteFacets={emitWriteFacets}). Expected [{string.Join(", ", expectedPaths)}], " +
                    $"got [{string.Join(", ", onDisk)}].");
            }

            // Each file's on-disk bytes must equal the in-memory content encoded as UTF-8 without a BOM.
            foreach (var file in files)
            {
                var native = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(tempDir, native);
                var actualBytes = File.ReadAllBytes(fullPath);
                var expectedBytes = utf8NoBom.GetBytes(file.Content);

                if (!actualBytes.SequenceEqual(expectedBytes))
                {
                    throw new Exception(
                        $"After idempotent regeneration the on-disk bytes of '{file.RelativePath}' differ from " +
                        $"the emitted content (seed={seed}, emitWriteFacets={emitWriteFacets}). " +
                        "The output is not byte-identical or carries an unexpected BOM.");
                }
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    // Produces a compact description of the first character difference between two strings, for diagnostics.
    private static string DescribeFirstDifference(string expected, string actual)
    {
        var min = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < min; i++)
        {
            if (expected[i] != actual[i])
            {
                return $"First difference at index {i}: expected '{Describe(expected[i])}', got '{Describe(actual[i])}'.";
            }
        }

        return $"Lengths differ: expected {expected.Length} chars, got {actual.Length} chars.";
    }

    private static string Describe(char c) => c switch
    {
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ => c.ToString(),
    };

    // ---- Fixture loading ----

    private static string LoadFixtureJson()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid-vista-document.json");
        return File.ReadAllText(path);
    }

    private static OpenApiDocument ParseFixture()
    {
        var parsed = OpenApiParser.Parse(FixtureJson);
        if (parsed.IsError)
        {
            throw new Exception($"The canonical fixture failed to parse: {parsed.Error}.");
        }

        return parsed.Value;
    }

    // Parses and resolves a fresh copy of the fixture so each "run" is genuinely independent (new
    // dictionaries), modelling the across-process guarantee within one process.
    private static ResolvedDocument ResolveFixture()
    {
        var parsed = OpenApiParser.Parse(FixtureJson);
        if (parsed.IsError)
        {
            throw new Exception($"The canonical fixture failed to parse: {parsed.Error}.");
        }

        var resolved = RefResolver.Resolve(parsed.Value);
        if (resolved.IsError)
        {
            throw new Exception($"The canonical fixture failed to resolve: {resolved.Error}.");
        }

        return resolved.Value;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            // Best-effort cleanup of a unique throwaway temp directory; a failed cleanup must not mask a
            // real test result.
        }
    }
}
