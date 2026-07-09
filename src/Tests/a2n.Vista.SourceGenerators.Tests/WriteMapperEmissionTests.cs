// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver EMISSION-STRUCTURE examples for the Phase 3 (M9, D121/D122) WriteMapperGenerator
// (task 6.5; requirements R6.1, R6.2, R6.5, R11.4). The generator is driven directly via
// CSharpGeneratorDriver over in-memory source (see WriteMapperGeneratorTestHarness), and these examples
// assert the SHAPE of the emitted <View>_VistaWriteMapper.g.cs artifact plus the incremental caching
// contract, mirroring the Phase 1/2 emitted-structure and cache-reuse tests
// (ViewAccessorGeneratorTests):
//
//   * Emitted structure (R6.1, R6.2, R11.4) — a well-formed candidate emits EXACTLY ONE
//     [ModuleInitializer] into the consumer assembly, keyed by the view's runtime Name obtained from
//     `new <View>().Name` (the view's public parameterless constructor), and registered into
//     GeneratedWriteMapperStore. The source is a file-local WriteMapper (Action<object, object>).
//   * No-ctor skip (R6.5) — a candidate view with NO public parameterless constructor emits NEITHER the
//     mapper NOR the initializer (nothing at all), and does so silently (no VISTA diagnostic), because
//     the generated initializer could not instantiate the view to read its Name.
//   * Incremental cache reuse (R11.x, mirroring Phase 1/2) — an unrelated edit that leaves a view's
//     equatable WriteMapperModel unchanged serves the tagged WriteMapperModel stage from cache
//     (IncrementalStepRunReason.Cached/Unchanged) rather than regenerating the unchanged view's mapper.
//
// Only the generated source TEXT and the run diagnostics/tracked steps are inspected; no generated
// [ModuleInitializer] is executed.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class WriteMapperEmissionTests
{
    // Shared entity/row/crud types reused across the examples so each view source stays small. The
    // scalar members (Name/Quantity) are safe write targets; Id is a declarable key on the read side.
    private const string SharedTypes = @"
namespace App
{
    public sealed class Source
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public sealed class WriteCrud
    {
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}
";

    // ---- well-formed candidate: two safe scalar mappings, implicit public parameterless ctor -------

    private const string WritableViewSource = SharedTypes + @"
namespace App
{
    public partial class WritableView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name)
                .MapWritable(c => c.Quantity, e => e.Quantity);
    }
}
";

    // ---- NO-CTOR candidate: a recognized candidate that declares ONLY a parameterized constructor, so
    // it has no public parameterless constructor and cannot be instantiated by the module initializer
    // to read its Name (R6.5). The single mapping is safe, so no mass-assignment error fires — the skip
    // is purely due to the missing ctor and is silent.
    private const string NoCtorViewSource = SharedTypes + @"
namespace App
{
    public partial class NoCtorWritableView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public NoCtorWritableView(int seed)
        {
        }

        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name);
    }
}
";

    // An unrelated edit appended to the view's syntax tree: a plain class with NO base list, so it is
    // not a write-mapper candidate. It changes the tree text (forcing the semantic transform to re-run)
    // but leaves the WritableView declaration and its TQuery/TCrud shape identical, so the equatable
    // WriteMapperModel compares equal and the downstream model stage is served from cache.
    private const string UnrelatedEdit = @"
namespace App
{
    public sealed class UnrelatedThing
    {
        public int Value { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}
";

    // ---- emitted structure (R6.1, R6.2, R11.4) ----------------------------------------------------

    [Test]
    public async Task Candidate_Emits_Exactly_One_ModuleInitializer_Keyed_By_New_View_Name()
    {
        var result = WriteMapperGeneratorTestHarness.Run(WritableViewSource);

        // A well-formed candidate raises no diagnostics.
        await Assert.That(result.Diagnostics.Any(static d => d.Id.StartsWith("VISTA", StringComparison.Ordinal))).IsFalse();

        // R11.4: exactly one write-mapper source is emitted into the consumer assembly for the view.
        await Assert.That(result.HasGeneratedSourceContaining("WritableView_VistaWriteMapper")).IsTrue();
        var generated = result.GeneratedSourceContaining("WritableView_VistaWriteMapper");

        // The emitted artifact is a file-local WriteMapper (Action<object, object>) — the fixed write seam.
        await Assert.That(generated.Contains("file static class WritableView_VistaWriteMapper", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains("global::a2n.Vista.Write.WriteMapper Mapper = static (model, entity) =>", StringComparison.Ordinal)).IsTrue();

        // R6.1: EXACTLY ONE [ModuleInitializer] is emitted.
        var initializerCount = CountOccurrences(generated, "[global::System.Runtime.CompilerServices.ModuleInitializer]");
        await Assert.That(initializerCount).IsEqualTo(1);

        // R6.2: the initializer keys the mapper off the view's RUNTIME Name, obtained by instantiating
        // the view via its public parameterless constructor and reading `.Name` — `new View().Name`.
        await Assert.That(generated.Contains("global::a2n.Vista.EntityFrameworkCore.Execution.GeneratedWriteMapperStore.Add(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains("new global::App.WritableView().Name, Mapper);", StringComparison.Ordinal)).IsTrue();
    }

    // ---- no-ctor skip (R6.5) ----------------------------------------------------------------------

    [Test]
    public async Task View_Without_Public_Parameterless_Ctor_Emits_Nothing()
    {
        var result = WriteMapperGeneratorTestHarness.Run(NoCtorViewSource);

        // R6.5: a view the initializer cannot instantiate emits NEITHER the mapper NOR the initializer.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaWriteMapper")).IsFalse();

        // The skip is silent: no VISTA diagnostic (the single mapping is safe, so no mass-assignment
        // error fires; the missing ctor is not itself a diagnostic — it mirrors the Phase 1/2 skip).
        await Assert.That(result.Diagnostics.Any(static d => d.Id.StartsWith("VISTA", StringComparison.Ordinal))).IsFalse();
    }

    // ---- incremental cache reuse on an unrelated edit (equatable model) ---------------------------

    [Test]
    public async Task UnrelatedEdit_Reuses_Cached_WriteMapperModel_Step()
    {
        var result = WriteMapperGeneratorTestHarness.RunIncremental(WritableViewSource, UnrelatedEdit);

        // The tagged equatable-model stage must be present in the tracked steps of the second run.
        var trackedSteps = result.Results.Single().TrackedSteps;
        await Assert.That(trackedSteps.ContainsKey(TrackingNames.WriteMapperModel)).IsTrue();

        // On the unrelated edit, every output of the model stage must be served from cache: either
        // Cached (input node unchanged, not re-executed) or Unchanged (re-executed because the tree
        // text changed, but the equatable WriteMapperModel compared equal so no new value flowed
        // downstream). It must NOT be New/Modified — that would mean the unrelated edit regenerated the
        // unchanged view's mapper.
        var outcomes = trackedSteps[TrackingNames.WriteMapperModel]
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();

        await Assert.That(outcomes.Length).IsGreaterThan(0);
        await Assert.That(outcomes.All(static reason =>
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
            .IsTrue();
    }

    // Counts non-overlapping occurrences of <paramref name="value"/> in <paramref name="text"/>.
    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
