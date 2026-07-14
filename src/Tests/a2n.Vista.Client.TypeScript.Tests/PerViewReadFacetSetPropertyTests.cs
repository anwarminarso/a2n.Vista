// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for the operation-graph builder's per-view read-facet set (task 7.11; Requirement
/// 4.1; design Property 18). Requirement 4.1: the generator emits a typed operation for each read facet
/// (<c>list</c>/<c>detail</c>/<c>metadata</c>/<c>export</c>) that is <em>present</em> in the document and
/// <em>omits</em> any that is absent — it never synthesizes a facet the document did not declare.
/// </summary>
/// <remarks>
/// <para>
/// For a view whose document declares a random non-empty subset of the four read facets, the built
/// <see cref="ViewModel"/>'s facet suffixes must equal exactly that subset — present facets modeled, absent
/// facets omitted, no extras — and, because the document declares no write operation, no write facet
/// (<c>create</c>/<c>update</c>/<c>delete</c>) may appear.
/// </para>
/// <para>
/// Documents are built directly through the model records (mirroring
/// <see cref="RefResolutionSoundnessPropertyTests"/>) and driven through the real resolve → re-lift →
/// operation-graph pipeline. Operations reference no components, so resolution is unconditionally sound and
/// the facet-presence behaviour is isolated from envelope/DTO binding. Each generated document carries one
/// to three distinctly-named views, each with its own independently-generated subset, so the property also
/// asserts that per-view facet sets do not bleed across views sharing a document.
/// </para>
/// </remarks>
public sealed class PerViewReadFacetSetPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The four read facet suffixes, indexed by bit position in the generated subset mask.</summary>
    private static readonly string[] ReadSuffixes =
    [
        OperationGraphBuilder.ListSuffix,
        OperationGraphBuilder.DetailSuffix,
        OperationGraphBuilder.MetadataSuffix,
        OperationGraphBuilder.ExportSuffix,
    ];

    /// <summary>The write facet suffixes that must never appear for a read-only document.</summary>
    private static readonly string[] WriteSuffixes =
    [
        OperationGraphBuilder.CreateSuffix,
        OperationGraphBuilder.UpdateSuffix,
        OperationGraphBuilder.DeleteSuffix,
    ];

    /// <summary>The HTTP method the document declares per read facet (metadata is a GET, the rest POST).</summary>
    private static string MethodFor(string suffix) =>
        string.Equals(suffix, OperationGraphBuilder.MetadataSuffix, StringComparison.Ordinal) ? "get" : "post";

    // ---- Model construction helpers (build OpenApiDocument instances directly via the records) ----

    /// <summary>A read facet operation for <paramref name="viewName"/>: id "{View}_{suffix}", one 200 response.</summary>
    private static OpenApiOperation ReadOperation(string viewName, string suffix) =>
        new(
            viewName + "_" + suffix,
            RequestBody: null,
            Responses: new Dictionary<string, OpenApiResponse>(StringComparer.Ordinal)
            {
                ["200"] = new OpenApiResponse(null),
            },
            Security: Array.Empty<OpenApiSecurityRequirement>());

    /// <summary>The path a facet is declared at: <c>/api/views/{view}/{suffix}</c> (lower-cased view root).</summary>
    private static string PathFor(string viewName, string suffix) =>
        "/api/views/" + viewName.ToLowerInvariant() + "/" + suffix;

    // Builds a document declaring, for each (viewName, subset) pair, exactly the read facets in the subset.
    private static OpenApiDocument BuildDocument(IReadOnlyList<(string ViewName, string[] Subset)> views)
    {
        var paths = new Dictionary<string, OpenApiPathItem>(StringComparer.Ordinal);

        foreach (var (viewName, subset) in views)
        {
            foreach (var suffix in subset)
            {
                var operations = new Dictionary<string, OpenApiOperation>(StringComparer.Ordinal)
                {
                    [MethodFor(suffix)] = ReadOperation(viewName, suffix),
                };
                paths[PathFor(viewName, suffix)] = new OpenApiPathItem(operations);
            }
        }

        return new OpenApiDocument(
            "3.0.4",
            new OpenApiInfo("a2n.Vista API", "1.0.0"),
            paths,
            new OpenApiComponents(
                new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal),
                new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal)),
            Array.Empty<OpenApiSecurityRequirement>());
    }

    // Runs the real resolve -> re-lift -> operation-graph pipeline over the document.
    private static IReadOnlyList<ViewModel> BuildViews(OpenApiDocument document)
    {
        var resolved = RefResolver.Resolve(document);
        if (resolved.IsError)
        {
            throw new Exception($"Document failed to resolve: {resolved.Error.Message}");
        }

        var notices = new NoticeCollector();
        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(resolved.Value, notices);
        return new OperationGraphBuilder().Build(resolved.Value, reLift, notices);
    }

    // ---- Generators ----

    /// <summary>A view name: an upper-case initial followed by 0–5 lower-case letters (no underscore).</summary>
    private static readonly Gen<string> ViewName =
        from head in Gen.Char['A', 'Z']
        from tail in Gen.Char['a', 'z'].Array[0, 5]
        select head + new string(tail);

    /// <summary>A non-empty subset of the four read suffixes, drawn from a 1..15 bit mask.</summary>
    private static readonly Gen<string[]> ReadSubset =
        Gen.Int[1, 15].Select(mask =>
            ReadSuffixes.Where((_, index) => (mask & (1 << index)) != 0).ToArray());

    /// <summary>One to three distinctly-named views, each paired with its own independent read subset.</summary>
    private static readonly Gen<(string ViewName, string[] Subset)[]> Views =
        from names in ViewName.Array[1, 3].Select(array => array.Distinct().ToArray())
        from subsets in ReadSubset.Array[names.Length]
        select names.Zip(subsets, (name, subset) => (name, subset)).ToArray();

    // Feature: typescript-client, Property 18: Per-view read-facet set matches the document
    //
    // For a view whose document declares a random non-empty subset of the read facets
    // (list/detail/metadata/export), the built ViewModel's read facet suffix set equals exactly that subset
    // — present facets modeled, absent facets omitted, no extras — and no write facet appears (the document
    // declares none).
    //
    // Validates: Requirements 4.1
    [Test]
    public void Per_View_Read_Facet_Set_Equals_Exactly_The_Documents_Present_Subset()
    {
        Views.Sample(
            views =>
            {
                var document = BuildDocument(views);
                var built = BuildViews(document);

                foreach (var (viewName, subset) in views)
                {
                    var view = built.SingleOrDefault(v => string.Equals(v.ViewName, viewName, StringComparison.Ordinal));
                    if (view is null)
                    {
                        throw new Exception(
                            $"View '{viewName}' declared facets {{{string.Join(", ", subset)}}} but no ViewModel " +
                            "was produced for it.");
                    }

                    var expected = subset.ToHashSet(StringComparer.Ordinal);
                    var actual = view.Facets.Select(facet => facet.Suffix).ToHashSet(StringComparer.Ordinal);

                    if (!actual.SetEquals(expected))
                    {
                        var missing = expected.Except(actual).ToArray();
                        var extra = actual.Except(expected).ToArray();
                        throw new Exception(
                            $"View '{viewName}' read-facet set mismatch. Expected {{{string.Join(", ", expected.OrderBy(s => s, StringComparer.Ordinal))}}}, " +
                            $"got {{{string.Join(", ", actual.OrderBy(s => s, StringComparer.Ordinal))}}}. " +
                            $"Missing: [{string.Join(", ", missing)}]; extra: [{string.Join(", ", extra)}].");
                    }

                    // No write facet may appear — the document declares none (Requirement 4.1: never synthesized).
                    foreach (var writeSuffix in WriteSuffixes)
                    {
                        if (actual.Contains(writeSuffix))
                        {
                            throw new Exception(
                                $"View '{viewName}' surfaced write facet '{writeSuffix}', but the document declares " +
                                "no write operation.");
                        }
                    }
                }
            },
            iter: Iterations);
    }
}
