// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver EMISSION-STRUCTURE examples for the Phase 4 (M9, D123, source-generator-http-surface)
// ViewInvokerGenerator (task 6.4; requirements R4.5, R7.2, R7.5). The generator is driven directly via
// CSharpGeneratorDriver over in-memory source (see ViewInvokerGeneratorTestHarness), and these examples
// assert the SHAPE of the emitted <View>_VistaViewInvoker.g.cs dispatch invoker plus the incremental
// caching contract, mirroring the Phase 1/2/3 emitted-structure and cache-reuse tests
// (ViewAccessorGeneratorTests / WriteMapperEmissionTests):
//
//   * Emitted structure (R4.5, R7.5) — a covered view with a public parameterless constructor emits a
//     `file sealed` class implementing global::a2n.Vista.Ports.IViewInvoker and EXACTLY ONE
//     [ModuleInitializer], keyed by the view's runtime Name obtained from `new <View>().Name`, that
//     registers a singleton into a2n.Vista.Ports.ViewInvokerStore. The invoker is emitted into the
//     consumer assembly (the assembly that declares the view), never a Vista assembly.
//   * IsWritable matches arity — a writable View<TRow, TCrud> emits `IsWritable => true` (real compile-time
//     write dispatch); a read-only View<TRow> emits `IsWritable => false` (write members throw).
//   * No-ctor skip (R1.5) — a covered view with NO public parameterless constructor emits NO invoker and
//     NO initializer (the store is left untouched, because the initializer could not instantiate the view
//     to read its Name), yet the view still receives its VISTA0041 serialization guidance.
//   * Incremental cache reuse (R7.2) — an unrelated edit that leaves a view's equatable ViewInvokerModel
//     unchanged serves the tagged ViewInvokerModel stage from cache
//     (IncrementalStepRunReason.Cached/Unchanged) rather than regenerating the unchanged view's invoker.
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

public sealed class ViewInvokerEmissionTests
{
    // Shared row/crud contract types reused across the examples so each view source stays small. `Row`
    // and `WriteCrud` are named types (coverable), so a view over them is a covered dispatch candidate.
    private const string SharedTypes = @"
namespace App
{
    public sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class WriteCrud
    {
        public string Name { get; set; } = string.Empty;
    }
}
";

    // ---- covered read-only candidate: named View<TRow>, implicit public parameterless ctor ----------

    private const string ReadOnlyNamedView = SharedTypes + @"
namespace App
{
    public partial class ReadOnlyNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    // ---- covered writable candidate: named View<TRow, TCrud>, implicit public parameterless ctor ----

    private const string WritableNamedView = SharedTypes + @"
namespace App
{
    public partial class WritableNamedView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
    }
}
";

    // ---- NO-CTOR candidate: a covered view that declares ONLY a parameterized constructor, so it has
    // no public parameterless constructor and cannot be instantiated by the module initializer to read
    // its Name (R1.5). It is still covered (named TRow), so it must still receive its VISTA0041 guidance.
    private const string NoCtorNamedView = SharedTypes + @"
namespace App
{
    public partial class NoCtorNamedView : a2n.Vista.Authoring.View<Row>
    {
        public NoCtorNamedView(int seed)
        {
        }
    }
}
";

    // An unrelated edit appended to the view's syntax tree: a plain class with NO base list, so it is
    // not a dispatch candidate. It changes the tree text (forcing the semantic transform to re-run) but
    // leaves the view declaration and its TQuery/TCrud shape identical, so the equatable ViewInvokerModel
    // compares equal and the downstream model stage is served from cache.
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

    // ---- emitted structure + one [ModuleInitializer] keyed by new View().Name (R4.5, R7.5) ----------

    [Test]
    public async Task Covered_View_Emits_Exactly_One_ModuleInitializer_Keyed_By_New_View_Name()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(ReadOnlyNamedView);

        // R7.5: exactly one dispatch-invoker source is emitted into the consumer assembly for the view.
        await Assert.That(result.HasGeneratedSourceContaining("ReadOnlyNamedView_VistaViewInvoker")).IsTrue();
        var generated = result.GeneratedSourceContaining("ReadOnlyNamedView_VistaViewInvoker");

        // The emitted artifact is a file-local sealed class implementing the Core-only IViewInvoker port.
        await Assert.That(generated.Contains(
            "file sealed class ReadOnlyNamedView_VistaViewInvoker : global::a2n.Vista.Ports.IViewInvoker",
            StringComparison.Ordinal)).IsTrue();

        // R4.5: EXACTLY ONE [ModuleInitializer] is emitted.
        var initializerCount = CountOccurrences(
            generated, "[global::System.Runtime.CompilerServices.ModuleInitializer]");
        await Assert.That(initializerCount).IsEqualTo(1);

        // R4.5: the initializer keys the invoker off the view's RUNTIME Name, obtained by instantiating
        // the view via its public parameterless constructor and reading `.Name` — `new <View>().Name` —
        // and registers a singleton into the Core-resident ViewInvokerStore.
        await Assert.That(generated.Contains(
            "global::a2n.Vista.Ports.ViewInvokerStore.Register(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "new global::App.ReadOnlyNamedView().Name, new ReadOnlyNamedView_VistaViewInvoker());",
            StringComparison.Ordinal)).IsTrue();
    }

    // ---- IsWritable matches arity: writable View<TRow, TCrud> emits IsWritable => true ---------------

    [Test]
    public async Task Writable_View_Emits_IsWritable_True_With_Write_Dispatch()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(WritableNamedView);

        var generated = result.GeneratedSourceContaining("WritableNamedView_VistaViewInvoker");

        // R3.1: a writable View<TRow, TCrud> with a named TCrud reports IsWritable => true and closes the
        // write facets over TCrud at compile time (no MakeGenericMethod).
        await Assert.That(generated.Contains("public bool IsWritable => true;", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            ".CreateAsync<global::App.WriteCrud>(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            ".UpdateAsync<global::App.WriteCrud>(", StringComparison.Ordinal)).IsTrue();
    }

    // ---- IsWritable matches arity: read-only View<TRow> emits IsWritable => false --------------------

    [Test]
    public async Task ReadOnly_View_Emits_IsWritable_False()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(ReadOnlyNamedView);

        var generated = result.GeneratedSourceContaining("ReadOnlyNamedView_VistaViewInvoker");

        // R3.3: a read-only View<TRow> reports IsWritable => false; the HTTP layer never routes a write
        // through it. It still closes the read facets over TRow at compile time.
        await Assert.That(generated.Contains("public bool IsWritable => false;", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            ".ListAsync<global::App.Row>(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            ".DetailAsync<global::App.Row>(", StringComparison.Ordinal)).IsTrue();

        // A read-only invoker carries no compile-time write dispatch.
        await Assert.That(generated.Contains(".CreateAsync<global::App.Row>(", StringComparison.Ordinal)).IsFalse();
    }

    // ---- no-ctor skip: emits nothing, store untouched, but still gets VISTA0041 (R1.5) ---------------

    [Test]
    public async Task Covered_View_Without_Public_Parameterless_Ctor_Emits_Nothing_But_Still_Gets_VISTA0041()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(NoCtorNamedView);

        // R1.5: a view the initializer cannot instantiate emits NEITHER the invoker NOR the initializer,
        // leaving the ViewInvokerStore untouched.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaViewInvoker")).IsFalse();

        // But it is still a covered view, so it receives exactly one VISTA0041 (Info) serialization
        // guidance — authoring an App_Json_Context stays mechanical even without a generated invoker.
        var vista0041 = result.Diagnostics.Where(static d => d.Id == "VISTA0041").ToArray();
        await Assert.That(vista0041.Length).IsEqualTo(1);
        await Assert.That(vista0041[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(vista0041[0].GetMessage().Contains("NoCtorNamedView", StringComparison.Ordinal)).IsTrue();

        // The skip is otherwise clean: no uncovered diagnostic, and the build stays green.
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0040")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ---- incremental cache reuse on an unrelated edit (equatable model, R7.2) ------------------------

    [Test]
    public async Task UnrelatedEdit_Reuses_Cached_ViewInvokerModel_Step()
    {
        var result = ViewInvokerGeneratorTestHarness.RunIncremental(WritableNamedView, UnrelatedEdit);

        // The tagged equatable-model stage must be present in the tracked steps of the second run.
        var trackedSteps = result.Results.Single().TrackedSteps;
        await Assert.That(trackedSteps.ContainsKey(TrackingNames.ViewInvokerModel)).IsTrue();

        // On the unrelated edit, every output of the model stage must be served from cache: either
        // Cached (input node unchanged, not re-executed) or Unchanged (re-executed because the tree text
        // changed, but the equatable ViewInvokerModel compared equal so no new value flowed downstream).
        // It must NOT be New/Modified — that would mean the unrelated edit regenerated the unchanged
        // view's invoker (R7.2).
        var outcomes = trackedSteps[TrackingNames.ViewInvokerModel]
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
