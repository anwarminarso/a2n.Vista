// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Test harness that drives the Phase 3 (M9, D121/D122) WriteMapperGenerator over in-memory source via
// CSharpGeneratorDriver and returns the run result for recognition/diagnostic/emission assertions
// (tasks 2.3, 4.3, 6.5). It mirrors GeneratorTestHarness (which drives the Phase 1/2
// ViewAccessorGenerator) but supplies the WRITE-side authoring surface the write-mapper generator
// recognizes by fully-qualified name (D48, R11.2/R11.3): the arity-2 base View<TQuery, TCrud>, the
// read/write builder IViewBuilder<TQuery> / IViewBuilder<TQuery, TCrud> (CrudOn / Field / Key), the
// facet builder ICrudBuilder<TQuery, TCrud, TEntity> (MapWritable / WithConcurrencyToken / AllowBulk),
// and the field builder IFieldBuilder<TProp> (PrimaryKey). The runtime write seams the generated source
// names (a2n.Vista.Write.WriteMapper and a2n.Vista.EntityFrameworkCore.Execution.GeneratedWriteMapperStore)
// are stubbed too so the emitted <View>_VistaWriteMapper.g.cs is realistic.
//
// Only the generated source TEXT and the run diagnostics are inspected; the generated [ModuleInitializer]
// is never executed (per the test-design guidance). The extension helpers
// (GeneratedSourceContaining / HasGeneratedSourceContaining) live on GeneratorDriverRunResult in
// GeneratorTestHarness and are reused here.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace a2n.Vista.SourceGenerators.Tests;

/// <summary>
/// Builds a <see cref="CSharpCompilation"/> from in-memory source and runs
/// <see cref="WriteMapperGenerator"/> through a <see cref="CSharpGeneratorDriver"/>.
/// </summary>
internal static class WriteMapperGeneratorTestHarness
{
    /// <summary>
    /// Minimal stub declarations of the Vista WRITE-side authoring surface the write-mapper generator
    /// recognizes by fully-qualified name, plus the runtime write seams the generated source references.
    /// The View base types expose a public <c>Name</c> property and (implicitly) a public parameterless
    /// constructor so a well-formed candidate is emittable (no VISTA0002-style ctor skip). Signatures
    /// mirror the real a2n.Vista.Core interfaces closely enough that <c>CrudOn</c> / <c>MapWritable</c> /
    /// <c>WithConcurrencyToken</c> / <c>Key</c> / <c>Field</c> / <c>PrimaryKey</c> resolve to methods
    /// whose containing type + namespace the generator matches.
    /// </summary>
    public const string WriteStubs = @"
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

    public interface IFieldBuilder<TProp>
    {
        IFieldBuilder<TProp> PrimaryKey();
        IFieldBuilder<TProp> Filterable(bool allowed = true);
        IFieldBuilder<TProp> Sortable(bool allowed = true);
    }

    public interface IViewBuilder<TQuery> where TQuery : class
    {
        IViewBuilder<TQuery> From<TSource>(
            System.Linq.Expressions.Expression<System.Func<TSource, TQuery>> projection)
            where TSource : class;

        IViewBuilder<TQuery> Field<TProp>(
            System.Linq.Expressions.Expression<System.Func<TQuery, TProp>> field,
            System.Action<IFieldBuilder<TProp>> configure);

        IViewBuilder<TQuery> Key(params System.Linq.Expressions.Expression<System.Func<TQuery, object?>>[] fields);

        IViewBuilder<TQuery> Key(params string[] fieldNames);
    }

    public interface ICrudBuilder<TQuery, TCrud, TEntity>
        where TQuery : class
        where TCrud : class
        where TEntity : class
    {
        ICrudBuilder<TQuery, TCrud, TEntity> MapWritable<TProp>(
            System.Linq.Expressions.Expression<System.Func<TCrud, TProp>> from,
            System.Linq.Expressions.Expression<System.Func<TEntity, TProp>> to);

        ICrudBuilder<TQuery, TCrud, TEntity> WithConcurrencyToken<TToken>(
            System.Linq.Expressions.Expression<System.Func<TEntity, TToken>> tokenField);

        ICrudBuilder<TQuery, TCrud, TEntity> AllowBulk(bool allow = true);
    }

    public interface IViewBuilder<TQuery, TCrud> : IViewBuilder<TQuery>
        where TQuery : class
        where TCrud : class
    {
        ICrudBuilder<TQuery, TCrud, TEntity> CrudOn<TEntity>(
            System.Linq.Expressions.Expression<System.Func<TEntity, TQuery>>? projectionForRead = null)
            where TEntity : class;
    }
}

namespace a2n.Vista.Write
{
    public delegate void WriteMapper(object model, object entity);
}

namespace a2n.Vista.EntityFrameworkCore.Execution
{
    public static class GeneratedWriteMapperStore
    {
        public static void Add(string viewName, global::a2n.Vista.Write.WriteMapper mapper)
        {
        }
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
    /// Runs <see cref="WriteMapperGenerator"/> over <paramref name="viewSource"/> (combined with
    /// <see cref="WriteStubs"/>) and returns the driver run result for assertions on generated sources
    /// and diagnostics.
    /// </summary>
    public static GeneratorDriverRunResult Run(string viewSource)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.WriteMapperGeneratorTests.InMemory",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText(WriteStubs),
                CSharpSyntaxTree.ParseText(viewSource),
            },
            references: References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var generator = new WriteMapperGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult();
    }

    /// <summary>
    /// Drives <see cref="WriteMapperGenerator"/> over the same view twice on ONE reused driver to prove
    /// incremental cache reuse of the equatable <see cref="WriteMapperModel"/> (task 6.5, mirroring the
    /// Phase 1/2 <c>GeneratorTestHarness.RunIncremental</c>). The first run establishes the baseline
    /// cache; the second run sees a compilation where the view's syntax tree has an UNRELATED edit
    /// appended (<paramref name="unrelatedEdit"/>) that leaves the writable view declaration and its
    /// <c>TQuery</c>/<c>TCrud</c> shape identical. Because the tree text changed, Roslyn re-executes the
    /// semantic transform — but the resulting <see cref="WriteMapperModel"/> compares equal, so the
    /// tagged <see cref="TrackingNames.WriteMapperModel"/> stage is served from cache
    /// (<see cref="IncrementalStepRunReason.Unchanged"/>/<see cref="IncrementalStepRunReason.Cached"/>)
    /// rather than flowing a new value downstream. The returned result is the SECOND run's result.
    /// </summary>
    /// <remarks>
    /// The driver is created with <c>trackIncrementalGeneratorSteps: true</c> so the run result records
    /// per-step outcomes in <see cref="GeneratorRunResult.TrackedSteps"/>.
    /// </remarks>
    public static GeneratorDriverRunResult RunIncremental(string viewSource, string unrelatedEdit)
    {
        var stubsTree = CSharpSyntaxTree.ParseText(WriteStubs);
        var viewTree = CSharpSyntaxTree.ParseText(viewSource);

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.WriteMapperGeneratorTests.InMemory",
            syntaxTrees: new[] { stubsTree, viewTree },
            references: References,
            options: options);

        var generator = new WriteMapperGenerator();
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
}
