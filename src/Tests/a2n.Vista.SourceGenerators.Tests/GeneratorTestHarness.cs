// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Test harness that drives the Vista source generator (ViewAccessorGenerator) over in-memory source
// via CSharpGeneratorDriver and returns the run result for golden/diagnostic assertions (R6.1).
//
// The generator references no Vista project and recognizes the View base types by fully-qualified
// name (Spec 03 D71), so the test compilation only needs minimal STUB declarations of
// a2n.Vista.Authoring.View<TQuery> / View<TQuery, TCrud> and a2n.Vista.Metadata.ViewAccessorRegistry
// for the FQN check to resolve. We only inspect the generated source TEXT and run diagnostics — the
// generated [ModuleInitializer] is never executed (per the test-design guidance).

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace a2n.Vista.SourceGenerators.Tests;

/// <summary>
/// Builds a <see cref="CSharpCompilation"/> from in-memory source and runs
/// <see cref="ViewAccessorGenerator"/> through a <see cref="CSharpGeneratorDriver"/>.
/// </summary>
internal static class GeneratorTestHarness
{
    /// <summary>
    /// Minimal stub declarations of the Vista base types the generator recognizes by FQN, plus the
    /// registry the generated module initializer targets. The View stubs expose a public
    /// <c>Name</c> property and an implicit public parameterless constructor so the generator's
    /// model-init reasoning (instantiate-and-read-Name) holds and VISTA0002 is not triggered.
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

namespace a2n.Vista.Metadata
{
    public static class ViewAccessorRegistry
    {
        public static void Register(
            string viewName,
            System.Collections.Generic.IReadOnlyDictionary<string, System.Func<object, object?>> accessors)
        {
        }
    }
}
";

    // All framework reference assemblies for the running TFM. Using the TRUSTED_PLATFORM_ASSEMBLIES set
    // is the standard way to give the in-memory compilation a complete reference closure (object,
    // System.Func<>, Dictionary<,>, etc.) without hand-picking individual facades.
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(static p => !string.IsNullOrEmpty(p))
        .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    /// <summary>
    /// Runs the generator over <paramref name="viewSource"/> (combined with the Vista stubs) and
    /// returns the driver run result for assertions on generated sources and diagnostics.
    /// </summary>
    public static GeneratorDriverRunResult Run(string viewSource)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.GeneratorTests.InMemory",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText(VistaStubs),
                CSharpSyntaxTree.ParseText(viewSource),
            },
            references: References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var generator = new ViewAccessorGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult();
    }

    /// <summary>
    /// Drives the generator over the same view twice on ONE reused driver to prove incremental cache
    /// reuse of the equatable model (R1.3, task 4.2). The first run sees the view source; the second run
    /// sees a compilation where the view's syntax tree has an UNRELATED edit appended
    /// (<paramref name="unrelatedEdit"/>) that leaves the view declaration and its <c>TQuery</c> shape
    /// identical. Because the tree text changed, Roslyn re-executes the semantic transform — but the
    /// resulting <see cref="ViewModel"/> compares equal, so the tagged
    /// <see cref="TrackingNames.ViewModel"/> stage is served from cache
    /// (<see cref="IncrementalStepRunReason.Unchanged"/>/<see cref="IncrementalStepRunReason.Cached"/>)
    /// rather than flowing a new value downstream.
    /// </summary>
    /// <remarks>
    /// The driver is created with <c>trackIncrementalGeneratorSteps: true</c> so the run result records
    /// per-step outcomes in <see cref="GeneratorRunResult.TrackedSteps"/>. The returned result is the
    /// SECOND run's result, whose tracked steps reflect the cache behavior on the unrelated edit.
    /// </remarks>
    public static GeneratorDriverRunResult RunIncremental(string viewSource, string unrelatedEdit)
    {
        var stubsTree = CSharpSyntaxTree.ParseText(VistaStubs);
        var viewTree = CSharpSyntaxTree.ParseText(viewSource);

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.GeneratorTests.InMemory",
            syntaxTrees: new[] { stubsTree, viewTree },
            references: References,
            options: options);

        var generator = new ViewAccessorGenerator();
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
    /// Returns the generated source text for the single generated source whose hint name contains
    /// <paramref name="hintNameFragment"/>, with line endings normalized to <c>\n</c>. Throws when no
    /// (or more than one) matching source was produced.
    /// </summary>
    public static string GeneratedSourceContaining(this GeneratorDriverRunResult result, string hintNameFragment)
    {
        var matches = result.Results
            .SelectMany(static r => r.GeneratedSources)
            .Where(s => s.HintName.Contains(hintNameFragment, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one generated source containing '{hintNameFragment}', found {matches.Length}: " +
                string.Join(", ", result.Results.SelectMany(static r => r.GeneratedSources).Select(static s => s.HintName)));
        }

        return matches[0].SourceText.ToString().Replace("\r\n", "\n");
    }

    /// <summary>Whether any generated source's hint name contains the fragment.</summary>
    public static bool HasGeneratedSourceContaining(this GeneratorDriverRunResult result, string hintNameFragment)
        => result.Results
            .SelectMany(static r => r.GeneratedSources)
            .Any(s => s.HintName.Contains(hintNameFragment, StringComparison.Ordinal));
}
