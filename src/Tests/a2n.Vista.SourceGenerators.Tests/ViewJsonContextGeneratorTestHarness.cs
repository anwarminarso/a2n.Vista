// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Test harness that drives the Phase 5 (M9, D125/D126, source-generator-json-typeinfo)
// ViewJsonContextGenerator over in-memory source via CSharpGeneratorDriver and returns the run result for
// the VISTA0050/VISTA0051 diagnostic-conformance property (task 3.3; Property 7; requirements R9.1, R9.2,
// R9.3, R9.4). It mirrors ViewInvokerGeneratorTestHarness / WriteMapperGeneratorTestHarness but supplies
// only the minimal authoring surface the per-view JsonTypeInfo generator recognizes by fully-qualified
// name (D48, R1.6/R7.1): the arity-1 base a2n.Vista.Authoring.View<TQuery> (read-only) and the arity-2
// base View<TQuery, TCrud> (writable). Both expose a public Name property and (implicitly) a public
// parameterless constructor so a well-formed candidate is coverable (the generated [ModuleInitializer]
// can instantiate the view to read its runtime Name, R1.7/R4.5).
//
// The generator recognizes the Vista read envelopes ViewListResult<TRow>/PagedResult<TRow> by
// fully-qualified name (they are composed into the VISTA0050 message as string constants and are known
// Core shapes for the Emittable_Shape analysis), so this harness also declares minimal stubs of
// a2n.Vista.Ports.ViewListResult<T> and a2n.Vista.Results.PagedResult<T>. This keeps the whole
// Serializable_DTO_Set { TRow, ViewListResult<TRow>, PagedResult<TRow>, [TCrud] } resolvable in the
// in-memory compilation regardless of whether the shape analysis walks the envelopes or treats them as
// known shapes.
//
// Task 3.3 asserts on the DIAGNOSTICS the source-output stage reports (their ids, severities, count, and
// message content) — NOT on any emitted per-view JsonTypeInfo source (emission is task 5.1). The driver is
// created with trackIncrementalGeneratorSteps: true so the harness could also read back the tracked
// ViewJsonContextModel if a test needs it, matching the Phase 1/2/3/4 harnesses.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace a2n.Vista.SourceGenerators.Tests;

/// <summary>
/// A read-only projection of the generator-internal <c>ViewJsonContextModel</c>'s recognition/coverage
/// fields, read back from a tracked-step output via reflection so the (internal) model type need not be
/// visible to the test assembly (matching the no-InternalsVisibleTo convention). Exposes only the fields
/// the recognition + shape matrix examples (task 2.4) assert on. A class the transform DROPPED (abstract /
/// non-partial / non-<c>View&lt;...&gt;</c>) produces no such model, so it is absent from the projection —
/// the "not a candidate" outcome (R1.3).
/// </summary>
internal sealed record RecognizedJsonContextView(
    string ClassName,
    bool IsWritable,
    bool IsAbstract,
    bool IsPartial,
    bool HasNamedRowType,
    bool HasNamedCrudType,
    bool HasPublicParameterlessCtor,
    bool AllShapesEmittable);

/// <summary>
/// Builds a <see cref="CSharpCompilation"/> from in-memory source and runs
/// <see cref="ViewJsonContextGenerator"/> through a <see cref="CSharpGeneratorDriver"/> with incremental
/// step tracking enabled, returning the run result for diagnostic and generated-source inspection.
/// </summary>
internal static class ViewJsonContextGeneratorTestHarness
{
    /// <summary>
    /// Minimal stub declarations of the Vista types the per-view JsonTypeInfo generator recognizes by
    /// fully-qualified name: the two base types (<c>View&lt;TQuery&gt;</c> / <c>View&lt;TQuery, TCrud&gt;</c>)
    /// and the two read envelopes (<c>ViewListResult&lt;TRow&gt;</c> / <c>PagedResult&lt;TRow&gt;</c>) that
    /// form the Serializable_DTO_Set. Both base types expose a public <c>Name</c> property and an implicit
    /// public parameterless constructor so a well-formed candidate is coverable (R1.7). No other Vista
    /// surface is needed: recognition is base-type + arity + FQN, and the VISTA0050 type names the generator
    /// composes are string constants, not symbol references (R1.6/R7.1).
    /// </summary>
    public const string VistaStubs = @"
namespace a2n.Vista.Authoring
{
    public abstract class View<TQuery>
    {
        public string Name { get; set; } = string.Empty;
    }

    public abstract class View<TQuery, TCrud>
    {
        public string Name { get; set; } = string.Empty;
    }
}

namespace a2n.Vista.Ports
{
    public sealed class ViewListResult<TRow>
    {
        public System.Collections.Generic.IReadOnlyList<TRow> Items { get; set; }
            = System.Array.Empty<TRow>();
        public long TotalCount { get; set; }
        public long FilteredCount { get; set; }
    }
}

namespace a2n.Vista.Results
{
    public sealed class PagedResult<TRow>
    {
        public System.Collections.Generic.IReadOnlyList<TRow> Items { get; set; }
            = System.Array.Empty<TRow>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long TotalCount { get; set; }
    }
}
";

    // All framework reference assemblies for the running TFM (TRUSTED_PLATFORM_ASSEMBLIES) — the standard
    // way to give the in-memory compilation a complete reference closure without hand-picking facades.
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(static p => !string.IsNullOrEmpty(p))
        .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    /// <summary>
    /// Runs <see cref="ViewJsonContextGenerator"/> over <paramref name="viewSource"/> (combined with
    /// <see cref="VistaStubs"/>) with incremental step tracking on, and returns the driver run result for
    /// diagnostic inspection (<see cref="GeneratorDriverRunResult.Diagnostics"/>) and generated-source
    /// inspection.
    /// </summary>
    public static GeneratorDriverRunResult Run(string viewSource)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.ViewJsonContextGeneratorTests.InMemory",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText(VistaStubs),
                CSharpSyntaxTree.ParseText(viewSource),
            },
            references: References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var generator = new ViewJsonContextGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    /// <summary>
    /// Drives <see cref="ViewJsonContextGenerator"/> over the same view twice on ONE reused driver to prove
    /// incremental cache reuse of the equatable <c>ViewJsonContextModel</c> (task 5.4, mirroring the
    /// Phase 1/2/3/4 <c>RunIncremental</c> harnesses). The first run establishes the baseline cache; the
    /// second run sees a compilation where the view's syntax tree has an UNRELATED edit appended
    /// (<paramref name="unrelatedEdit"/>) that leaves the view declaration and its <c>TQuery</c>/
    /// <c>TCrud</c> shape — and therefore its whole Serializable_DTO_Set — identical. Because the tree text
    /// changed, Roslyn re-executes the semantic transform, but the resulting <c>ViewJsonContextModel</c>
    /// compares equal, so the tagged <see cref="TrackingNames.ViewJsonContextModel"/> stage is served from
    /// cache (<see cref="IncrementalStepRunReason.Unchanged"/>/<see cref="IncrementalStepRunReason.Cached"/>)
    /// rather than flowing a new value downstream and regenerating the unchanged view's context (R7.2). The
    /// returned result is the SECOND run's result.
    /// </summary>
    public static GeneratorDriverRunResult RunIncremental(string viewSource, string unrelatedEdit)
    {
        var stubsTree = CSharpSyntaxTree.ParseText(VistaStubs);
        var viewTree = CSharpSyntaxTree.ParseText(viewSource);

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.ViewJsonContextGeneratorTests.InMemory",
            syntaxTrees: new[] { stubsTree, viewTree },
            references: References,
            options: options);

        var generator = new ViewJsonContextGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // First run: establishes the baseline cache for every pipeline stage.
        driver = driver.RunGenerators(compilation);

        // Second run: replace ONLY the view tree with one carrying an unrelated edit appended after the
        // view. The new content is not a candidate (no View<...> base), so the view's own model is unchanged.
        var modifiedViewTree = CSharpSyntaxTree.ParseText(viewSource + unrelatedEdit);
        var modifiedCompilation = compilation.ReplaceSyntaxTree(viewTree, modifiedViewTree);
        driver = driver.RunGenerators(modifiedCompilation);

        return driver.GetRunResult();
    }

    /// <summary>
    /// Whether the generator emitted a per-view JsonTypeInfo context source for the view named
    /// <paramref name="viewName"/> (the emitter, task 5.1, names it <c>&lt;View&gt;_VistaJsonContext.g.cs</c>).
    /// Used to assert that a NOT-COVERED view (VISTA0051) produces no context — forward-compatible with the
    /// emitter: until task 5.1 lands no source is produced, and afterwards a non-emittable view still
    /// produces none.
    /// </summary>
    public static bool HasGeneratedContextFor(this GeneratorDriverRunResult result, string viewName)
        => result.GeneratedTrees.Any(t =>
               Path.GetFileName(t.FilePath).IndexOf(viewName, StringComparison.Ordinal) >= 0
               && Path.GetFileName(t.FilePath).IndexOf("VistaJsonContext", StringComparison.Ordinal) >= 0);

    /// <summary>
    /// Returns the full generated source TEXT of the per-view JsonTypeInfo context emitted for the view
    /// named <paramref name="viewName"/> (the emitter, task 5.1, names it
    /// <c>&lt;Namespace&gt;_&lt;View&gt;_VistaJsonContext.g.cs</c>). Throws when zero or more than one
    /// generated context tree matches — the callers (the emission property tests, tasks 5.2/5.3) expect
    /// exactly one context per covered view. Used to assert on the emitted source's structure without
    /// executing any generated <c>[ModuleInitializer]</c>.
    /// </summary>
    public static string GeneratedContextSourceFor(this GeneratorDriverRunResult result, string viewName)
        => result.GeneratedTrees
            .Single(t =>
                Path.GetFileName(t.FilePath).IndexOf(viewName, StringComparison.Ordinal) >= 0
                && Path.GetFileName(t.FilePath).IndexOf("VistaJsonContext", StringComparison.Ordinal) >= 0)
            .ToString();

    /// <summary>
    /// Projects every <c>ViewJsonContextModel</c> the semantic transform kept (the outputs of the tagged
    /// <see cref="TrackingNames.ViewJsonContextModel"/> step) into the public
    /// <see cref="RecognizedJsonContextView"/> shape, reading the internal model's public properties by
    /// reflection. A class the transform dropped (abstract / non-partial / non-<c>View&lt;...&gt;</c>)
    /// produces no output here, so it is absent from the returned set — the "not a candidate" outcome
    /// (R1.3). A view whose <c>TQuery</c> is anonymous/<c>object</c> IS surfaced (a model is produced) but
    /// with <see cref="RecognizedJsonContextView.HasNamedRowType"/> == <c>false</c>, i.e. recognized as a
    /// base candidate yet not a serialization candidate (R1.1, R1.3).
    /// </summary>
    public static IReadOnlyList<RecognizedJsonContextView> RecognizedJsonContextViews(
        this GeneratorDriverRunResult result)
    {
        var runResult = result.Results.Single();
        if (!runResult.TrackedSteps.TryGetValue(TrackingNames.ViewJsonContextModel, out var steps))
        {
            return Array.Empty<RecognizedJsonContextView>();
        }

        return steps
            .SelectMany(static step => step.Outputs)
            .Select(static output => Project(output.Value))
            .ToArray();
    }

    /// <summary>
    /// Returns the single recognized view with <paramref name="className"/>, or throws when zero or more
    /// than one match — used by the positive-control assertions where exactly one model is expected.
    /// </summary>
    public static RecognizedJsonContextView RecognizedJsonContextView(
        this GeneratorDriverRunResult result, string className)
        => result.RecognizedJsonContextViews().Single(v => v.ClassName == className);

    /// <summary>Whether the transform produced a <c>ViewJsonContextModel</c> for <paramref name="className"/>.</summary>
    public static bool IsRecognizedJsonContextCandidate(
        this GeneratorDriverRunResult result, string className)
        => result.RecognizedJsonContextViews().Any(v => v.ClassName == className);

    // Reads the internal ViewJsonContextModel's public get-only properties by reflection into the public
    // RecognizedJsonContextView projection (the model type is internal to the generator assembly).
    private static RecognizedJsonContextView Project(object model)
    {
        var type = model.GetType();
        return new RecognizedJsonContextView(
            ClassName: (string)Read(type, model, "ClassName"),
            IsWritable: (bool)Read(type, model, "IsWritable"),
            IsAbstract: (bool)Read(type, model, "IsAbstract"),
            IsPartial: (bool)Read(type, model, "IsPartial"),
            HasNamedRowType: (bool)Read(type, model, "HasNamedRowType"),
            HasNamedCrudType: (bool)Read(type, model, "HasNamedCrudType"),
            HasPublicParameterlessCtor: (bool)Read(type, model, "HasPublicParameterlessCtor"),
            AllShapesEmittable: (bool)Read(type, model, "AllShapesEmittable"));
    }

    private static object Read(Type type, object instance, string propertyName)
        => type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;
}
