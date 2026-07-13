// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Test harness that drives the Style A coverage generator (StyleAShapeGenerator, the fifth phase — M9,
// D129/D130, style-a-coverage) over in-memory source via CSharpGeneratorDriver and returns the run result
// for diagnostic assertions. It mirrors GeneratorTestHarness / ViewJsonContextGeneratorTestHarness /
// ViewInvokerGeneratorTestHarness but supplies the minimal Style A AUTHORING surface the generator
// recognizes by fully-qualified name (D48, R1.6/R7.1) — the central-template authoring types, NOT the
// class-per-view (Style B) View<...> bases the prior phases recognized:
//
//   * a2n.Vista.Authoring.ViewTemplate<TDbContext>            (the base the template subclass derives)
//   * a2n.Vista.Authoring.IViewTemplateBuilder<TDbContext>    (declares AddView<TRow>(name, projection))
//   * a2n.Vista.Authoring.IReadViewBuilder<TRow>              (declares WithCrud<TCrud, TEntity>())
//   * a2n.Vista.Authoring.ICrudFacetBuilder<TCrud, TEntity>   (the WithCrud return type)
//
// These stubs reproduce ONLY what the generator inspects: the method names (AddView / WithCrud), their
// generic arities (`1 / `2), their declaring interfaces' metadata names + namespace, and the
// ViewTemplate<TDbContext> base — see StyleAShapeGenerator's IsAddViewMethod / IsWithCrudMethod /
// DerivesFromViewTemplate / IsRecognizedAuthoringType. A minimal a2n.Vista.TestFixtures.TestDbContext is
// supplied so a template can close ViewTemplate<TDbContext>. No other Vista surface is needed: recognition
// is FQN-based and the VISTA0060 artifact names the generator composes are string constants, not symbol
// references.
//
// Task 3.3 asserts on the DIAGNOSTICS the source-output stage reports (their ids, severities, count, and
// message content) — NOT on any emitted accessor map / per-view JsonTypeInfo source (emission is tasks
// 5.1/5.2). The driver is created with trackIncrementalGeneratorSteps: true so the harness could also read
// back the tracked StyleAViewModel if a later task needs it, matching the Phase 1/2/3/4/5 harnesses. No
// emitted [ModuleInitializer] is executed.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace a2n.Vista.SourceGenerators.Tests;

/// <summary>
/// Builds a <see cref="CSharpCompilation"/> from in-memory source and runs
/// <see cref="StyleAShapeGenerator"/> through a <see cref="CSharpGeneratorDriver"/> with incremental step
/// tracking enabled, returning the run result for diagnostic inspection.
/// </summary>
internal static class StyleAShapeGeneratorTestHarness
{
    /// <summary>
    /// Minimal stub declarations of the Style A authoring types the generator recognizes by
    /// fully-qualified name: the template base <c>ViewTemplate&lt;TDbContext&gt;</c>, the registration
    /// surface <c>IViewTemplateBuilder&lt;TDbContext&gt;</c> (which declares
    /// <c>AddView&lt;TRow&gt;(name, projection)</c>), the read-facet builder <c>IReadViewBuilder&lt;TRow&gt;</c>
    /// (which declares <c>WithCrud&lt;TCrud, TEntity&gt;()</c>), and the <c>WithCrud</c> return type
    /// <c>ICrudFacetBuilder&lt;TCrud, TEntity&gt;</c>. The method names, generic arities, declaring-interface
    /// metadata names, and the <c>a2n.Vista.Authoring</c> namespace mirror the real Core surface, which is
    /// all the generator inspects (R1.6/R7.1). A minimal <c>a2n.Vista.TestFixtures.TestDbContext</c> lets a
    /// template close <c>ViewTemplate&lt;TDbContext&gt;</c>.
    /// </summary>
    public const string VistaStubs = @"
namespace a2n.Vista.Authoring
{
    public abstract class ViewTemplate<TDbContext>
        where TDbContext : class
    {
        protected internal abstract void Configure(IViewTemplateBuilder<TDbContext> views);
    }

    public interface IViewTemplateBuilder<TDbContext>
        where TDbContext : class
    {
        IReadViewBuilder<TRow> AddView<TRow>(
            string name,
            System.Func<TDbContext, System.IServiceProvider, System.Linq.IQueryable<TRow>> query)
            where TRow : class;
    }

    public interface IReadViewBuilder<TRow>
        where TRow : class
    {
        IReadViewBuilder<TRow> MaxPageSize(int rows);
        IReadViewBuilder<TRow> Key(params string[] fieldNames);
        ICrudFacetBuilder<TCrud, TEntity> WithCrud<TCrud, TEntity>()
            where TCrud : class
            where TEntity : class;
    }

    public interface ICrudFacetBuilder<TCrud, TEntity>
        where TCrud : class
        where TEntity : class
    {
    }
}

namespace a2n.Vista.TestFixtures
{
    public sealed class TestDbContext
    {
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
    /// Runs <see cref="StyleAShapeGenerator"/> over <paramref name="viewSource"/> (combined with
    /// <see cref="VistaStubs"/>) with incremental step tracking on, and returns the driver run result for
    /// diagnostic inspection (<see cref="GeneratorDriverRunResult.Diagnostics"/>).
    /// </summary>
    public static GeneratorDriverRunResult Run(string viewSource)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.StyleAShapeGeneratorTests.InMemory",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText(VistaStubs),
                CSharpSyntaxTree.ParseText(viewSource),
            },
            references: References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var generator = new StyleAShapeGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
