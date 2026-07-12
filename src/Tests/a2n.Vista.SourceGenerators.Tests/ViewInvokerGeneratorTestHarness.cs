// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Test harness that drives the Phase 4 (M9, D123, source-generator-http-surface) ViewInvokerGenerator
// over in-memory source via CSharpGeneratorDriver and returns the run result for recognition assertions
// (task 3.3; requirements R1.1, R1.2, R1.3, R1.5). It mirrors GeneratorTestHarness /
// WriteMapperGeneratorTestHarness but supplies only the minimal authoring surface the dispatch-invoker
// generator recognizes by fully-qualified name (D48, R1.4/R7.1): the arity-1 base
// a2n.Vista.Authoring.View<TQuery> (read-only) and the arity-2 base View<TQuery, TCrud> (writable). Both
// expose a public Name property and (implicitly) a public parameterless constructor so a well-formed
// candidate is emittable (no VISTA0002-style ctor skip).
//
// Task 3.3 asserts on RECOGNITION OUTCOMES — which classes become dispatch candidates and their coverage
// flags (IsWritable, HasNamedRowType, HasNamedCrudType) — via the tracked ViewInvokerModel step
// (TrackingNames.ViewInvokerModel) rather than on any emitted invoker source (emission is task 6.1). The
// driver is therefore created with trackIncrementalGeneratorSteps: true so GeneratorRunResult.TrackedSteps
// records the per-view equatable model the transform produced. Because ViewInvokerModel is `internal` to
// the generator assembly (no InternalsVisibleTo to the tests, matching the project convention), the model
// fields are read back through reflection into the public RecognizedView projection below.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace a2n.Vista.SourceGenerators.Tests;

/// <summary>
/// A read-only projection of the generator-internal <c>ViewInvokerModel</c>'s recognition fields, read
/// back from a tracked-step output via reflection so the (internal) model type need not be visible to the
/// test assembly. Exposes only the fields task 3.3 asserts on.
/// </summary>
internal sealed record RecognizedView(
    string ClassName,
    bool IsWritable,
    bool IsAbstract,
    bool IsPartial,
    bool HasNamedRowType,
    bool HasNamedCrudType,
    bool HasPublicParameterlessCtor);

/// <summary>
/// Builds a <see cref="CSharpCompilation"/> from in-memory source and runs
/// <see cref="ViewInvokerGenerator"/> through a <see cref="CSharpGeneratorDriver"/> with incremental
/// step tracking enabled, so the recognized <c>ViewInvokerModel</c>s can be read back from the tagged
/// <see cref="TrackingNames.ViewInvokerModel"/> step.
/// </summary>
internal static class ViewInvokerGeneratorTestHarness
{
    /// <summary>
    /// Minimal stub declarations of the two Vista base types the dispatch-invoker generator recognizes by
    /// fully-qualified name. Both expose a public <c>Name</c> property and an implicit public parameterless
    /// constructor so a well-formed candidate is emittable (R1.5). No other Vista surface is needed:
    /// recognition is purely base-type + arity, and the VISTA0041 serializable-type names the generator
    /// composes are string constants, not symbol references (R1.4/R7.1).
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
    /// Runs <see cref="ViewInvokerGenerator"/> over <paramref name="viewSource"/> (combined with
    /// <see cref="VistaStubs"/>) with incremental step tracking on, and returns the driver run result for
    /// recognition assertions (via <see cref="RecognizedViews"/>) and diagnostic inspection.
    /// </summary>
    public static GeneratorDriverRunResult Run(string viewSource)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.ViewInvokerGeneratorTests.InMemory",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText(VistaStubs),
                CSharpSyntaxTree.ParseText(viewSource),
            },
            references: References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var generator = new ViewInvokerGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    /// <summary>
    /// Drives <see cref="ViewInvokerGenerator"/> over the same view twice on ONE reused driver to prove
    /// incremental cache reuse of the equatable <see cref="ViewInvokerModel"/> (task 6.4, mirroring the
    /// Phase 1/2/3 <c>RunIncremental</c> harnesses). The first run establishes the baseline cache; the
    /// second run sees a compilation where the view's syntax tree has an UNRELATED edit appended
    /// (<paramref name="unrelatedEdit"/>) that leaves the view declaration and its <c>TQuery</c>/
    /// <c>TCrud</c> shape identical. Because the tree text changed, Roslyn re-executes the semantic
    /// transform — but the resulting <see cref="ViewInvokerModel"/> compares equal, so the tagged
    /// <see cref="TrackingNames.ViewInvokerModel"/> stage is served from cache
    /// (<see cref="IncrementalStepRunReason.Unchanged"/>/<see cref="IncrementalStepRunReason.Cached"/>)
    /// rather than flowing a new value downstream. The returned result is the SECOND run's result.
    /// </summary>
    public static GeneratorDriverRunResult RunIncremental(string viewSource, string unrelatedEdit)
    {
        var stubsTree = CSharpSyntaxTree.ParseText(VistaStubs);
        var viewTree = CSharpSyntaxTree.ParseText(viewSource);

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.ViewInvokerGeneratorTests.InMemory",
            syntaxTrees: new[] { stubsTree, viewTree },
            references: References,
            options: options);

        var generator = new ViewInvokerGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // First run: establishes the baseline cache for every pipeline stage.
        driver = driver.RunGenerators(compilation);

        // Second run: replace ONLY the view tree with one carrying an unrelated edit appended after the
        // view. The new content is not a candidate (no base list), so the view's own model is unchanged.
        var modifiedViewTree = CSharpSyntaxTree.ParseText(viewSource + unrelatedEdit);
        var modifiedCompilation = compilation.ReplaceSyntaxTree(viewTree, modifiedViewTree);
        driver = driver.RunGenerators(modifiedCompilation);

        return driver.GetRunResult();
    }

    /// <summary>
    /// Projects every <c>ViewInvokerModel</c> the transform kept (the outputs of the tagged
    /// <see cref="TrackingNames.ViewInvokerModel"/> step) into the public <see cref="RecognizedView"/>
    /// shape, reading the internal model's public properties by reflection. A class the transform dropped
    /// (abstract / non-partial / non-<c>View&lt;...&gt;</c>) produces no output here, so it is absent from
    /// the returned set — the "not a candidate" outcome (R1.3).
    /// </summary>
    public static IReadOnlyList<RecognizedView> RecognizedViews(this GeneratorDriverRunResult result)
    {
        var runResult = result.Results.Single();
        if (!runResult.TrackedSteps.TryGetValue(TrackingNames.ViewInvokerModel, out var steps))
        {
            return Array.Empty<RecognizedView>();
        }

        return steps
            .SelectMany(static step => step.Outputs)
            .Select(static output => Project(output.Value))
            .ToArray();
    }

    /// <summary>
    /// Returns the single recognized view with <paramref name="className"/>, or throws when zero or more
    /// than one match — used by the positive-control assertions where exactly one candidate is expected.
    /// </summary>
    public static RecognizedView RecognizedView(this GeneratorDriverRunResult result, string className)
        => result.RecognizedViews().Single(v => v.ClassName == className);

    /// <summary>Whether any recognized view has <paramref name="className"/>.</summary>
    public static bool IsRecognizedCandidate(this GeneratorDriverRunResult result, string className)
        => result.RecognizedViews().Any(v => v.ClassName == className);

    // Reads the internal ViewInvokerModel's public get-only properties by reflection into RecognizedView.
    private static RecognizedView Project(object model)
    {
        var type = model.GetType();
        return new RecognizedView(
            ClassName: (string)Read(type, model, "ClassName"),
            IsWritable: (bool)Read(type, model, "IsWritable"),
            IsAbstract: (bool)Read(type, model, "IsAbstract"),
            IsPartial: (bool)Read(type, model, "IsPartial"),
            HasNamedRowType: (bool)Read(type, model, "HasNamedRowType"),
            HasNamedCrudType: (bool)Read(type, model, "HasNamedCrudType"),
            HasPublicParameterlessCtor: (bool)Read(type, model, "HasPublicParameterlessCtor"));
    }

    private static object Read(Type type, object instance, string propertyName)
        => type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;
}
