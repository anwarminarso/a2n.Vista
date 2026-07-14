// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Emit.Runtime;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for the framework-agnostic guarantee of the generated client (task 10.9; Requirements
/// 6.4, 12.5, 12.6; design Property 10). For <em>any</em> valid document, no file in the
/// <c>Generated_Output</c> may import or declare a dependency on a UI framework package or a grid library
/// package: the client must be constructable and executable given only the injected <c>HttpTransport</c> and
/// the standard platform globals (<c>fetch</c>, <c>URL</c>, the DOM <c>RequestInit</c>/<c>Response</c>
/// types), which need no import.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant, made structural.</b> A self-contained client that leans only on platform globals never
/// needs a <em>bare</em> module specifier — every legitimate import is a relative reference to a sibling
/// runtime/type module (a specifier starting with <c>./</c> or <c>../</c>). The property therefore scans
/// every emitted file for each <c>import … from "X"</c> / <c>export … from "X"</c> specifier (and any
/// dynamic <c>import("X")</c>) and asserts:
/// </para>
/// <list type="number">
///   <item><description>every specifier <c>X</c> is <b>relative</b> (starts with <c>./</c> or <c>../</c>) — i.e. there is no bare package import at all; and</description></item>
///   <item><description>no specifier contains any fragment from an explicit UI/grid denylist (react, vue, angular, svelte, ag-grid, @mui, rxjs, jquery, …), belt-and-suspenders over (1).</description></item>
/// </list>
/// <para>
/// <b>Assembling the full <c>Generated_Output</c>.</b> Each iteration builds the complete emitted file set:
/// the six fixed runtime files (<c>http-transport</c>, <c>auth</c>, <c>result</c>, <c>url</c>,
/// <c>client-context</c>, <c>raw-payload</c>) which are document-independent constants; <c>types.ts</c>
/// (via <see cref="TypesEmitter"/>) and <c>filter-node.ts</c> (via <see cref="FilterNodeEmitter"/>) built
/// from the canonical M18 fixture through the real parse → resolve → re-lift pipeline; and one
/// <c>views/{view}.ts</c> per generated view (via <see cref="ViewClientEmitter.EmitAll"/>). The variation
/// lives in the views: a random number of distinctly-named views, each with a random non-empty subset of the
/// four read facets and a random secured flag, exercising every conditional import branch of the view
/// emitter — the <c>../types</c> DTO imports (list/detail/metadata), the <c>../runtime/raw-payload</c> import
/// (export), and the <c>../runtime/auth</c> import (secured) — alongside the always-present
/// <c>../runtime/result</c>, <c>../runtime/client-context</c>, and <c>../runtime/http-transport</c> imports.
/// The view models are built directly through the model records (the pattern of
/// <see cref="ViewClientEmitterTests"/>) so the secured/typed import branches are reached deterministically.
/// </para>
/// </remarks>
public sealed class NoUiOrGridDependencyPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy — ≥ 100).</summary>
    private const int Iterations = 100;

    /// <summary>The four read facet suffixes, indexed by bit position in the generated subset mask.</summary>
    private static readonly string[] ReadSuffixes =
    [
        OperationGraphBuilder.ListSuffix,
        OperationGraphBuilder.DetailSuffix,
        OperationGraphBuilder.MetadataSuffix,
        OperationGraphBuilder.ExportSuffix,
    ];

    /// <summary>
    /// UI-framework and grid-library package name fragments that must never appear in any import specifier
    /// (Requirements 12.5, 12.6). Matched case-insensitively as a substring of the specifier.
    /// </summary>
    private static readonly string[] UiOrGridDenylist =
    [
        "react", "vue", "@angular", "angular", "svelte", "solid-js", "preact", "lit-element", "ember",
        "ag-grid", "aggrid", "@mui", "mui", "datatables", "handsontable", "tabulator", "primeng",
        "primereact", "antd", "bootstrap", "jquery", "rxjs", "@tanstack",
    ];

    /// <summary>
    /// Matches an ES module <c>import … from "X"</c> / <c>export … from "X"</c> statement and captures the
    /// module specifier <c>X</c> (either quote style).
    /// </summary>
    private static readonly Regex FromSpecifier = new(
        "\\b(?:import|export)\\b[^;]*?\\bfrom\\s*[\"']([^\"']+)[\"']",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Matches a dynamic <c>import("X")</c> call and captures the module specifier <c>X</c>.</summary>
    private static readonly Regex DynamicImportSpecifier = new(
        "\\bimport\\s*\\(\\s*[\"']([^\"']+)[\"']\\s*\\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // The two fixture-derived, document-independent files (types.ts + filter-node.ts), built once. They are
    // constant across iterations but are part of every Generated_Output, so they are always scanned.
    private static readonly Lazy<IReadOnlyList<GeneratedFile>> FixtureDerivedFiles =
        new(BuildFixtureDerivedFiles);

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

    /// <summary>A single view spec: a distinct name, its present read-facet subset, and a secured flag.</summary>
    private static readonly Gen<ViewSpec> ViewSpecGen =
        from name in ViewName
        from subset in ReadSubset
        from secured in Gen.Bool
        select new ViewSpec(name, subset, secured);

    /// <summary>One to four distinctly-named views, each with its own independent facet subset and posture.</summary>
    private static readonly Gen<IReadOnlyList<ViewSpec>> ViewSpecsGen =
        ViewSpecGen.Array[1, 4]
            .Select(specs => (IReadOnlyList<ViewSpec>)specs
                .GroupBy(spec => spec.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray());

    // Feature: typescript-client, Property 10: No UI or grid dependency in the generated client
    //
    // For any valid document, no file in the Generated_Output imports or declares a dependency on a UI
    // framework package or a grid library package: every emitted import/export specifier is a relative
    // module path (starts with "./" or "../"), so the client is self-contained and executable given only
    // the injected HttpTransport plus the standard platform globals — no bare package import appears, and
    // no denylisted UI/grid package name fragment appears in any specifier.
    //
    // Validates: Requirements 6.4, 12.5, 12.6
    [Test]
    public void No_File_In_The_Generated_Output_Imports_A_Ui_Or_Grid_Package()
    {
        ViewSpecsGen.Sample(
            specs =>
            {
                var files = BuildGeneratedOutput(specs);

                foreach (var file in files)
                {
                    foreach (var specifier in ImportSpecifiers(file.Content))
                    {
                        // (1) No bare specifier: a self-contained client only ever imports its own sibling
                        //     runtime/type modules by relative path (Requirement 6.4).
                        if (!specifier.StartsWith("./", StringComparison.Ordinal)
                            && !specifier.StartsWith("../", StringComparison.Ordinal))
                        {
                            throw new Exception(
                                $"File '{file.RelativePath}' imports the bare module specifier \"{specifier}\". " +
                                "The generated client must import only its own sibling modules by relative path " +
                                "(\"./\" or \"../\"), so it depends on no external package (Requirements 6.4, 12.5, 12.6). " +
                                $"Views under test: {DescribeSpecs(specs)}.");
                        }

                        // (2) Explicit UI/grid denylist, belt-and-suspenders over (1): no specifier may name a
                        //     known UI framework or grid library (Requirements 12.5, 12.6).
                        var lowered = specifier.ToLowerInvariant();
                        foreach (var banned in UiOrGridDenylist)
                        {
                            if (lowered.Contains(banned, StringComparison.Ordinal))
                            {
                                throw new Exception(
                                    $"File '{file.RelativePath}' imports \"{specifier}\", which names the " +
                                    $"forbidden UI/grid dependency fragment \"{banned}\" (Requirements 12.5, 12.6). " +
                                    $"Views under test: {DescribeSpecs(specs)}.");
                            }
                        }
                    }
                }
            },
            iter: Iterations);
    }

    // ---- Generated-output assembly ----

    // Builds the complete Generated_Output for a set of view specs: the fixed runtime files, the
    // fixture-derived types.ts + filter-node.ts, and one views/{view}.ts per generated view.
    private static IReadOnlyList<GeneratedFile> BuildGeneratedOutput(IReadOnlyList<ViewSpec> specs)
    {
        var files = new List<GeneratedFile>();

        // 1. The six fixed, document-independent runtime files (always part of the output).
        files.Add(HttpTransportEmitter.Emit());
        files.Add(AuthEmitter.Emit());
        files.Add(ResultEmitter.Emit());
        files.Add(UrlEmitter.Emit());
        files.Add(ClientContextEmitter.Emit());
        files.Add(RawPayloadEmitter.Emit());

        // 2. types.ts + filter-node.ts, derived once from the canonical fixture.
        files.AddRange(FixtureDerivedFiles.Value);

        // 3. One per-view client file per generated view.
        var views = specs.Select(BuildViewModel).ToArray();
        files.AddRange(ViewClientEmitter.EmitAll(views));

        return files;
    }

    // Builds a ViewModel directly from a spec's facet subset and secured flag, mirroring the model-record
    // construction pattern of ViewClientEmitterTests so every conditional import branch of the view emitter
    // (../types, ../runtime/raw-payload, ../runtime/auth) is reachable deterministically.
    private static ViewModel BuildViewModel(ViewSpec spec)
    {
        var rowType = TsType.Named(spec.Name + "Row");
        var route = "/api/views/" + spec.Name.ToLowerInvariant();

        var facets = spec.Facets
            .Select(suffix => BuildFacet(spec, suffix, route, rowType))
            .ToArray();

        return new ViewModel(spec.Name, route, rowType, CrudType: null, facets);
    }

    private static FacetModel BuildFacet(ViewSpec spec, string suffix, string route, TsType rowType)
    {
        var path = route + "/" + suffix;
        return suffix switch
        {
            OperationGraphBuilder.ListSuffix => new FacetModel(
                suffix, "POST", path,
                RequestType: TsType.Named("VistaListRequestBody"),
                SuccessType: TsType.Generic("ViewListResult", [rowType]),
                Secured: spec.Secured,
                Concurrency: ConcurrencyMode.None),
            OperationGraphBuilder.DetailSuffix => new FacetModel(
                suffix, "POST", path,
                RequestType: TsType.Named("VistaDetailRequestBody"),
                SuccessType: rowType,
                Secured: spec.Secured,
                Concurrency: ConcurrencyMode.None),
            OperationGraphBuilder.MetadataSuffix => new FacetModel(
                suffix, "GET", path,
                RequestType: null,
                SuccessType: TsType.Named("VistaMetadataResponse"),
                Secured: spec.Secured,
                Concurrency: ConcurrencyMode.None),
            OperationGraphBuilder.ExportSuffix => new FacetModel(
                suffix, "POST", path,
                RequestType: TsType.Named("VistaListRequestBody"),
                SuccessType: TsType.Named(OperationGraphBuilder.RawPayloadTypeName),
                Secured: spec.Secured,
                Concurrency: ConcurrencyMode.None),
            _ => throw new InvalidOperationException($"Unexpected read facet suffix '{suffix}'."),
        };
    }

    // Builds types.ts + filter-node.ts from the canonical fixture through the real parse -> resolve ->
    // re-lift pipeline (the recipe used by TypesEmitterTests / FilterNodeModelBuilderTests).
    private static IReadOnlyList<GeneratedFile> BuildFixtureDerivedFiles()
    {
        var fixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var raw = File.ReadAllText(Path.Combine(fixturesDirectory, "valid-vista-document.json"));

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

        var document = resolved.Value;
        var notices = new NoticeCollector();

        // types.ts
        var envelopes = new EnvelopeCatalog().Bind(document, includeWriteEnvelopes: false);
        if (envelopes.IsError)
        {
            throw new Exception($"Fixture envelope binding failed: {envelopes.Error.Message}");
        }

        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(document, notices);
        var typesInput = new TypesEmitInput(document, envelopes.Value, reLift, new[] { "CustomerRow" }, notices);
        var typesFile = TypesEmitter.Emit(typesInput);
        if (typesFile.IsError)
        {
            throw new Exception($"Fixture types.ts emission failed: {typesFile.Error.Message}");
        }

        // filter-node.ts
        var filterModel = new FilterNodeModelBuilder().Build(document, notices);
        if (filterModel.IsError)
        {
            throw new Exception($"Fixture filter-node model build failed: {filterModel.Error.Message}");
        }

        var filterFile = FilterNodeEmitter.Emit(filterModel.Value);

        return new[] { typesFile.Value, filterFile };
    }

    // ---- Scanning ----

    // Extracts every module specifier referenced by a static import/export-from statement or a dynamic
    // import() call in the file content.
    private static IEnumerable<string> ImportSpecifiers(string content)
    {
        foreach (Match match in FromSpecifier.Matches(content))
        {
            yield return match.Groups[1].Value;
        }

        foreach (Match match in DynamicImportSpecifier.Matches(content))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static string DescribeSpecs(IReadOnlyList<ViewSpec> specs) =>
        string.Join("; ", specs.Select(spec =>
            $"{spec.Name}[{string.Join(",", spec.Facets)}]{(spec.Secured ? " secured" : string.Empty)}"));

    /// <summary>A generated view spec: a distinct name, its present read-facet subset, and its security posture.</summary>
    private sealed record ViewSpec(string Name, string[] Facets, bool Secured);
}
