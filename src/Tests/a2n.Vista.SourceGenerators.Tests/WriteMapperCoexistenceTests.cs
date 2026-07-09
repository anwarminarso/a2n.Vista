// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver MIXED-COMPILATION coexistence example for the Phase 3 (M9, D121/D122)
// WriteMapperGenerator (task 8.3; requirements R7.3, R7.4, R8.4). The generator is driven directly via
// CSharpGeneratorDriver over ONE in-memory compilation that contains BOTH an analyzable typed Style B
// writable view AND an unanalyzable (fallback) writable view (see WriteMapperGeneratorTestHarness).
//
// This is the generator-driver half of the coexistence contract; the runtime resolver half (that a
// registered view resolves to the generated mapper and an unregistered one to the reflection fallback,
// consistently) is covered by the property test ResolverPreferenceCoexistencePropertyTests (task 8.2),
// so it is deliberately NOT duplicated here. This example asserts what is observable at the
// generator-driver level:
//
//   * R7.4 / R8.4 — in a single compilation with one analyzable and one fallback view, the generator
//     emits a <View>_VistaWriteMapper.g.cs for the analyzable view ONLY; the fallback view gets no
//     mapper and instead resolves to reflection at runtime. Exactly one write-mapper source is emitted,
//     and it is the analyzable view's.
//   * The build stays green for the mixed compilation — the fallback view raises a single VISTA0033
//     WARNING (naming the offending view and expression) and no error-severity diagnostic is reported,
//     so the presence of a fallback view never breaks the build or the analyzable view's emission.
//   * R7.3 — the returned mapper exposes no origin distinction to the executor. At the generator-driver
//     level this is observable as: the emitted mapper is typed as the SAME `global::a2n.Vista.Write.WriteMapper`
//     delegate seam the reflection fallback produces, and it is registered into the store as the bare
//     delegate value — there is no generated-only type, wrapper, marker attribute, or origin property
//     that would let the executor branch on whether the mapper was generated or reflection-built.
//
// Only the generated source TEXT and the run diagnostics are inspected; no generated
// [ModuleInitializer] is executed.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class WriteMapperCoexistenceTests
{
    // Shared read/write/entity types reused by both views in the single compilation. Name/Quantity are
    // safe scalar write targets; Id is a declarable key on the read side.
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

    // ONE compilation, TWO writable views:
    //   * AnalyzableMemoView — a well-formed candidate with a single simple scalar mapping
    //     (Name -> Name). It is analyzable, so the generator emits its write mapper.
    //   * FallbackMemoView   — a recognized candidate whose source selector is a method call
    //     (c => c.Name.ToUpper()), i.e. not a simple member selector, so it is not statically
    //     analyzable: no mapper is emitted and a VISTA0033 warning falls it back to reflection.
    private const string MixedCompilationSource = SharedTypes + @"
namespace App
{
    public partial class AnalyzableMemoView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name);
    }

    public partial class FallbackMemoView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name.ToUpper(), e => e.Name);
    }
}
";

    // ---- R7.4 / R8.4: analyzable view emits a mapper; fallback view does not -----------------------

    [Test]
    public async Task Mixed_Compilation_Emits_Mapper_For_Analyzable_View_Only()
    {
        var result = WriteMapperGeneratorTestHarness.Run(MixedCompilationSource);

        // R7.4 / R8.4: exactly one write-mapper source is emitted across the whole compilation.
        // GeneratedSourceContaining throws unless EXACTLY ONE source matches "_VistaWriteMapper", so this
        // both proves a single mapper was emitted and hands us its text.
        var generated = result.GeneratedSourceContaining("_VistaWriteMapper");

        // The emitted mapper is the ANALYZABLE view's — not the fallback view's.
        await Assert.That(result.HasGeneratedSourceContaining("AnalyzableMemoView_VistaWriteMapper")).IsTrue();
        await Assert.That(result.HasGeneratedSourceContaining("FallbackMemoView_VistaWriteMapper")).IsFalse();
        await Assert.That(generated.Contains("AnalyzableMemoView", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains("FallbackMemoView", StringComparison.Ordinal)).IsFalse();
    }

    // ---- build stays green: fallback view raises exactly one VISTA0033 warning, no errors -----------

    [Test]
    public async Task Mixed_Compilation_Reports_Only_Vista0033_Warning_For_Fallback_View_And_Stays_Green()
    {
        var result = WriteMapperGeneratorTestHarness.Run(MixedCompilationSource);

        // R8.4 fallback: exactly one VISTA0033 warning, naming the fallback view AND its offending
        // expression, so the executor resolves that view to the reflection mapper at runtime.
        var vista0033 = result.Diagnostics.Where(static d => d.Id == "VISTA0033").ToArray();
        await Assert.That(vista0033.Length).IsEqualTo(1);
        await Assert.That(vista0033[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);

        var message = vista0033[0].GetMessage();
        await Assert.That(message.Contains("FallbackMemoView", StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains("ToUpper", StringComparison.Ordinal)).IsTrue();

        // The analyzable view must NOT be dragged into the fallback: the warning is not raised for it.
        await Assert.That(vista0033[0].GetMessage().Contains("AnalyzableMemoView", StringComparison.Ordinal)).IsFalse();

        // The mixed compilation stays green — a fallback view sharing the compilation never breaks the
        // build or the analyzable view's emission (no error-severity diagnostic at all).
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ---- R7.3: the returned mapper exposes no origin distinction to the executor -------------------

    [Test]
    public async Task Emitted_Mapper_Uses_The_Shared_WriteMapper_Seam_With_No_Origin_Distinction()
    {
        var result = WriteMapperGeneratorTestHarness.Run(MixedCompilationSource);

        var generated = result.GeneratedSourceContaining("AnalyzableMemoView_VistaWriteMapper");

        // R7.3: the generated mapper is typed as the SAME `WriteMapper` delegate the reflection fallback
        // produces — the fixed write seam the executor consumes. There is no generated-only delegate,
        // wrapper type, or subtype.
        await Assert.That(generated.Contains(
            "global::a2n.Vista.Write.WriteMapper Mapper = static (model, entity) =>",
            StringComparison.Ordinal)).IsTrue();

        // R7.3: the mapper is registered into the store as the BARE delegate value (keyed only by the
        // view's runtime Name) — no origin flag, no metadata argument, nothing that lets a consumer
        // branch on whether the mapper was generated or reflection-built. The store's Add signature is
        // (viewName, WriteMapper), identical to how a reflection mapper would be surfaced.
        await Assert.That(generated.Contains(
            "global::a2n.Vista.EntityFrameworkCore.Execution.GeneratedWriteMapperStore.Add(",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "new global::App.AnalyzableMemoView().Name, Mapper);",
            StringComparison.Ordinal)).IsTrue();

        // R7.3: the emitted source declares no origin-distinguishing marker — no bespoke interface or
        // origin attribute that stamps the mapper as "generated". The only surfaces are the file-local
        // holder class and the WriteMapper delegate field, so once the delegate is in the store the
        // executor cannot observe (and therefore cannot branch on) the mapper's origin. Guard against a
        // regression that would add a distinguishing marker interface / origin attribute.
        await Assert.That(generated.Contains(": global::a2n.Vista", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generated.Contains("Origin", StringComparison.Ordinal)).IsFalse();
    }
}
