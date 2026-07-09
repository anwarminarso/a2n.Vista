// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Packaging / layering assertions for the Phase 3 generated WRITE MAPPER (spec
// source-generator-write-mapper, task 11.2; Requirements R11.1, R11.2, R11.3, R11.5). These are the
// write-mapper analogue of the runtime a2n.Vista.Tests/LayeringGuardTests — but anchored on the
// GENERATOR assembly rather than the runtime assemblies, because the generator's layering rules are
// about how the Roslyn component is packaged and what it may reference.
//
// The three reflection assertions pin the generator's shape (R11.1: a netstandard2.0
// IIncrementalGenerator carrying [Generator(LanguageNames.CSharp)]) and its isolation (R11.2/R11.3: no
// direct reference to ANY a2n.Vista assembly — recognition is by fully-qualified name only, D48). The
// SourceGenerators.Tests project deliberately takes NO Vista project reference (see its .csproj), so
// `typeof(WriteMapperGenerator).Assembly` is the generator's OWN netstandard2.0 assembly and its
// GetReferencedAssemblies() reports exactly the compile-time references the compiler emitted into it.
//
// The final test proves R11.5 by construction: it drives WriteMapperGenerator over an in-memory typed
// Style B writable view (recognized purely by FQN via minimal stubs), then feeds the emitted
// <View>_VistaWriteMapper.g.cs back into the compilation and asserts the augmented compilation has zero
// error diagnostics. Because this test project multi-targets net8.0/net9.0/net10.0 and runs on each, a
// green run per TFM proves the generated write-mapper source compiles on every supported consumer TFM
// (the same guarantee the a2n.Vista.GeneratorWriteMapperSample fixture gives by building on all three).

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

/// <summary>
/// Generator packaging / layering guard (design.md §"Integration / smoke tests" →
/// "Packaging/layering (R11.1–R11.3, R11.5)"). Asserts the <see cref="WriteMapperGenerator"/> is a
/// netstandard2.0 <see cref="IIncrementalGenerator"/> that references no a2n.Vista assembly, and that its
/// emitted write-mapper source compiles on the running (net8/9/10) target framework.
/// </summary>
public sealed class WriteMapperGeneratorPackagingTests
{
    private const string VistaAssemblyPrefix = "a2n.Vista";
    private const string RoslynAssemblyPrefix = "Microsoft.CodeAnalysis";
    private const string NetStandardFrameworkName = ".NETStandard,Version=v2.0";

    // The generator's own assembly. SourceGenerators.Tests references the generator BOTH as an analyzer
    // and as a normal assembly (ReferenceOutputAssembly=true), so this resolves to the real, shipped
    // netstandard2.0 component under test.
    private static readonly Assembly GeneratorAssembly = typeof(WriteMapperGenerator).Assembly;

    /// <summary>
    /// R11.1: <see cref="WriteMapperGenerator"/> is an <see cref="IIncrementalGenerator"/> and is
    /// registered for C# via <c>[Generator(LanguageNames.CSharp)]</c>. This is what makes it a real
    /// incremental Roslyn source generator (not a legacy <c>ISourceGenerator</c> or an analyzer).
    /// </summary>
    [Test]
    public async Task WriteMapperGenerator_Is_A_CSharp_Incremental_Generator()
    {
        await Assert.That(typeof(IIncrementalGenerator).IsAssignableFrom(typeof(WriteMapperGenerator)))
            .IsTrue();

        var generatorAttribute = typeof(WriteMapperGenerator).GetCustomAttribute<GeneratorAttribute>();

        await Assert.That(generatorAttribute).IsNotNull();
        await Assert.That(generatorAttribute!.Languages).Contains(LanguageNames.CSharp);
    }

    /// <summary>
    /// R11.1: the generator assembly targets <c>netstandard2.0</c>. netstandard2.0 is the mandated
    /// generator TFM so the single compiled component loads uniformly into the Roslyn analyzer host for
    /// every consumer regardless of the consumer's own target framework (net8/9/10, R11.5).
    /// </summary>
    [Test]
    public async Task Generator_Assembly_Targets_NetStandard2_0()
    {
        var targetFramework = GeneratorAssembly.GetCustomAttribute<TargetFrameworkAttribute>();

        await Assert.That(targetFramework).IsNotNull();
        await Assert.That(targetFramework!.FrameworkName).IsEqualTo(NetStandardFrameworkName);
    }

    /// <summary>
    /// R11.2 / R11.3: the generator declares no direct reference to ANY a2n.Vista assembly. It recognizes
    /// Vista types (<c>View&lt;TQuery, TCrud&gt;</c>, the CRUD-facet DSL, the write-mapper store) solely
    /// by fully-qualified name (D48), so no compiled Vista type is referenced — keeping the generator a
    /// self-contained Roslyn component consistent with Phases 1 and 2.
    /// </summary>
    [Test]
    public async Task Generator_Assembly_References_No_Vista_Assembly()
    {
        var referencesVista = ReferencedAssemblyNames(GeneratorAssembly)
            .Any(name => name.StartsWith(VistaAssemblyPrefix, StringComparison.Ordinal));

        await Assert.That(referencesVista).IsFalse();
    }

    /// <summary>
    /// Positive sanity: the generator DOES reference the Roslyn compiler platform
    /// (<c>Microsoft.CodeAnalysis*</c>). This proves the "no Vista reference" assertion above is a real
    /// layering property over a populated reference set, not an artifact of an assembly that references
    /// almost nothing.
    /// </summary>
    [Test]
    public async Task Generator_Assembly_References_The_Roslyn_Platform()
    {
        var referencesRoslyn = ReferencedAssemblyNames(GeneratorAssembly)
            .Any(name => name.StartsWith(RoslynAssemblyPrefix, StringComparison.Ordinal));

        await Assert.That(referencesRoslyn).IsTrue();
    }

    /// <summary>
    /// R11.5: the emitted write-mapper source compiles on the running consumer target framework. Drives
    /// <see cref="WriteMapperGenerator"/> over an in-memory typed Style B writable view (recognized by
    /// FQN via minimal stubs), then adds the generated <c>&lt;View&gt;_VistaWriteMapper.g.cs</c> back into
    /// the compilation and asserts there are no error diagnostics — neither compiler errors (the
    /// generated <c>file</c>-scoped mapper + <c>[ModuleInitializer]</c> are legal net8.0+ C#) nor
    /// generator errors (the sample view is analyzable and safe). Running per-TFM (net8/9/10) proves the
    /// generated source is legal on every supported consumer TFM.
    /// </summary>
    [Test]
    public async Task Generated_Write_Mapper_Source_Compiles_On_Current_Target_Framework()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Vista.WriteMapperPackagingTests.InMemory",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText(WriteMapperStubs),
                CSharpSyntaxTree.ParseText(WritableViewSource),
            },
            references: References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var generator = new WriteMapperGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        // The generator produced the per-view write mapper for the analyzable, safe sample view...
        var runResult = driver.GetRunResult();
        var emittedWriteMapper = runResult.Results
            .SelectMany(static r => r.GeneratedSources)
            .Any(s => s.HintName.Contains("WritableMemoView_VistaWriteMapper", StringComparison.Ordinal));

        await Assert.That(emittedWriteMapper).IsTrue();

        // ...and it reported no error diagnostics (the view is analyzable and mass-assignment-safe).
        var generatorErrors = generatorDiagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(generatorErrors).IsEmpty();

        // The augmented compilation (stubs + view + GENERATED write mapper) has no error diagnostics, so
        // the generated write-mapper source compiles on this target framework (R11.5).
        var compileErrors = updatedCompilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.Id + ": " + d.GetMessage())
            .ToArray();

        await Assert.That(compileErrors).IsEmpty();
    }

    /// <summary>
    /// Projects the direct referenced-assembly simple names of <paramref name="assembly"/>
    /// (<see cref="Assembly.GetReferencedAssemblies()"/> → <see cref="AssemblyName.Name"/>), dropping any
    /// null names defensively. Mirrors the helper in <c>a2n.Vista.Tests/LayeringGuardTests</c>.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)!;

    // ---- in-memory fixtures for the R11.5 compile check -------------------------------------------

    // All framework reference assemblies for the running TFM (TRUSTED_PLATFORM_ASSEMBLIES) — the standard
    // way to give the in-memory compilation a complete reference closure without hand-picking facades.
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(static p => !string.IsNullOrEmpty(p))
        .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    // Minimal FQN stubs the generator recognizes: the arity-2 writable View base, the class-per-view
    // builder (Named/From/Field/Key/CrudOn on a type named IViewBuilder), the CRUD facet builder
    // (MapWritable/WithConcurrencyToken on a type named ICrudBuilder), the per-field builder
    // (PrimaryKey on IFieldBuilder), plus the runtime seams the emitted source names by FQN
    // (a2n.Vista.Write.WriteMapper and a2n.Vista.EntityFrameworkCore.Execution.GeneratedWriteMapperStore).
    private const string WriteMapperStubs = @"
namespace a2n.Vista.Authoring
{
    public interface IFieldBuilder<TProp>
    {
        IFieldBuilder<TProp> PrimaryKey();
    }

    public interface ICrudBuilder<TQuery, TCrud, TEntity>
    {
        ICrudBuilder<TQuery, TCrud, TEntity> MapWritable<TProp>(
            System.Linq.Expressions.Expression<System.Func<TCrud, TProp>> from,
            System.Linq.Expressions.Expression<System.Func<TEntity, TProp>> to);

        ICrudBuilder<TQuery, TCrud, TEntity> WithConcurrencyToken<TProp>(
            System.Linq.Expressions.Expression<System.Func<TEntity, TProp>> token);
    }

    public interface IViewBuilder<TQuery, TCrud>
    {
        IViewBuilder<TQuery, TCrud> Named(string name);

        IViewBuilder<TQuery, TCrud> From<TSource>(
            System.Linq.Expressions.Expression<System.Func<TSource, TQuery>> projection);

        IViewBuilder<TQuery, TCrud> Field<TProp>(
            System.Linq.Expressions.Expression<System.Func<TQuery, TProp>> field,
            System.Action<IFieldBuilder<TProp>> configure);

        IViewBuilder<TQuery, TCrud> Key(
            params System.Linq.Expressions.Expression<System.Func<TQuery, object>>[] fields);

        ICrudBuilder<TQuery, TCrud, TEntity> CrudOn<TEntity>();
    }

    public abstract class View<TQuery, TCrud>
    {
        public string Name { get; set; } = string.Empty;
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

    // One analyzable, mass-assignment-safe typed Style B writable view: TQuery=MemoRow, TCrud=
    // MemoWriteModel, TEntity=Memo. Id is a declared key (skipped); Text (string) and Priority (int) are
    // scalar, non-key, non-token targets, so the generator emits two direct assignments plus a
    // [ModuleInitializer] registration.
    private const string WritableViewSource = @"
namespace App
{
    public sealed class Memo
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    public sealed class MemoRow
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    public sealed class MemoWriteModel
    {
        public string Text { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    public partial class WritableMemoView : a2n.Vista.Authoring.View<MemoRow, MemoWriteModel>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<MemoRow, MemoWriteModel> builder)
            => builder
                .Named(""WritableMemoSample"")
                .From<Memo>(src => new MemoRow { Id = src.Id, Text = src.Text, Priority = src.Priority })
                .Field(x => x.Id, f => f.PrimaryKey())
                .CrudOn<Memo>()
                .MapWritable(c => c.Text, e => e.Text)
                .MapWritable(c => c.Priority, e => e.Priority);
    }
}
";
}
