// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Feature: style-a-coverage
//
// Generator-driver RECOGNITION + COVERAGE MATRIX examples for the Style A coverage generator
// (StyleAShapeGenerator, the fifth phase — M9, D129/D130, style-a-coverage) — task 2.5; requirements
// R1.1, R1.2, R1.3, R1.4, R1.5, R1.7. The generator is driven directly via CSharpGeneratorDriver over
// in-memory source (see StyleAShapeGeneratorTestHarness). These are EXAMPLE tests — fixed, named cases
// that document each concrete recognition/coverage-matrix ROW with a human-readable assertion. The
// companion PROPERTY test that quantifies the same VISTA0060–VISTA0063 invariants over the whole random
// matrix is Vista0060CoverageAndDiagnosticConformancePropertyTests (task 3.3); this file complements it
// with the concrete, documentary rows.
//
// WHAT IS ASSERTED (and why it is stable): each row asserts on two OBSERVABLE outcomes that do not depend
// on whether any source is emitted —
//   * RECOGNITION — whether the semantic transform produced a StyleAViewModel, read back as the count of
//     outputs of the tracked StyleAViewModel step (TrackingNames.StyleAViewModel). A recognized Style A
//     call site has count 1 (even when it is not keyable/coverable, e.g. a non-constant name); a
//     non-candidate invocation has count 0.
//   * COVERAGE CLASSIFICATION — the non-blocking VISTA0060 (covered; names the exact artifact set),
//     VISTA0061 (anonymous read row → RUC by design), VISTA0062 (non-constant name → hard gate), and
//     VISTA0063 (non-emittable DTO member) diagnostics the source-output stage reports.
//
// CONCURRENCY ROBUSTNESS (task 5.1/5.2 land the accessor + JsonTypeInfo EMITTERS in parallel): these
// tests deliberately assert ONLY on DIAGNOSTICS and the recognition/coverage classification, never on
// emitted-source presence/absence or generated-source text (that is task 5.5's job). Diagnostics are
// computed from the equatable model independently of emission, so they are stable whether or not the
// emitters have started calling AddSource.
//
// TASK 2.4 HAS LANDED (the Emittable_Shape analysis), so emittability now flips the artifact set: a
// named-TRow view with all-emittable read DTOs lists "read-DTO JsonTypeInfo", and a writable view with an
// emittable TCrud lists "TCrud JsonTypeInfo". These rows assert that REAL, current behavior (unlike the
// task-3.3 property test, which was authored to be robust to 2.4 not yet having landed). All DTO members
// in the "covered" rows are emittable shapes (int / string / int? / enum), and the non-emittable row uses
// an interface-typed member so VISTA0063 fires deterministically.
//
// The recognition + coverage matrix (design.md "Coverage classification"):
//   1. named TRow, read-only, constant name            -> COVERED: VISTA0060 { export accessors,
//                                                          read-DTO JsonTypeInfo }.
//   2. named TRow + named TCrud, constant name          -> COVERED: VISTA0060 { export accessors,
//                                                          read-DTO JsonTypeInfo, TCrud JsonTypeInfo }.
//   3. anonymous TRow + named TCrud, constant name      -> TCrud-ONLY coverage: VISTA0060 { TCrud
//                                                          JsonTypeInfo } + VISTA0061 (read stays RUC).
//   4. anonymous / object TRow, read-only, constant name-> NOT covered: no VISTA0060 + VISTA0061.
//   5. non-constant name                                -> NOT covered: VISTA0062 ONLY (hard gate).
//   6. named TRow with a non-emittable DTO member       -> read-DTO JsonTypeInfo skipped: VISTA0060
//                                                          { export accessors } + VISTA0063.
//   7. non-AddView / AddView not on a ViewTemplate      -> NOT a candidate: no StyleAViewModel, no
//                                                          Style A diagnostic at all.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class StyleARecognitionAndCoverageMatrixTests
{
    // The fixed message marker after which VISTA0060's {1} artifact list begins (see
    // DiagnosticDescriptors.StyleAViewCovered: "...is covered by generated artifacts: {1}").
    private const string Vista0060ArtifactMarker = "is covered by generated artifacts: ";

    // The three artifact names VISTA0060 can list, in the generator's fixed emission order (see
    // StyleAShapeGenerator.Emit).
    private const string ExportAccessors = "export accessors";
    private const string ReadDtoJsonTypeInfo = "read-DTO JsonTypeInfo";
    private const string TCrudJsonTypeInfo = "TCrud JsonTypeInfo";

    // ---- Row 1: named TRow, read-only, constant name -> COVERED (accessors + read-DTO JsonTypeInfo) ---

    private const string NamedReadOnlyView = @"
using System.Linq;

namespace App
{
    public enum CustomerKind { Regular, Premium }

    public sealed class CustomerRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Rank { get; set; }
        public CustomerKind Kind { get; set; }
    }

    public class CustomerReadOnlyTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<CustomerRow>(""customers"", (db, sp) => new CustomerRow[0].AsQueryable());
        }
    }
}
";

    [Test]
    public async Task Named_ReadOnly_ConstantName_Is_Covered_With_Accessors_And_ReadDto_JsonTypeInfo()
    {
        // Requirements 1.1, 1.2, 1.3, 1.7 — a nameable read-only Style A view is a recognized candidate and
        // is covered for both read-side artifacts (accessors need only a nameable row; read-DTO JsonTypeInfo
        // because every read-DTO member is an emittable shape).
        var result = StyleAShapeGeneratorTestHarness.Run(NamedReadOnlyView);

        await Assert.That(RecognizedCandidateCount(result)).IsEqualTo(1);

        var covered = DiagnosticsWithId(result, "VISTA0060");
        await Assert.That(covered.Length).IsEqualTo(1);
        await Assert.That(covered[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(Names(covered[0]).Contains("'customers'", StringComparison.Ordinal)).IsTrue();

        var artifacts = Vista0060Artifacts(covered[0]);
        await Assert.That(artifacts).IsEquivalentTo(new[] { ExportAccessors, ReadDtoJsonTypeInfo });

        // A named row is nameable, a constant name is keyable, and no member is non-emittable, so none of
        // the boundary diagnostics fire and the build is clean.
        await AssertNoDiagnostics(result, "VISTA0061", "VISTA0062", "VISTA0063");
        await AssertNoErrors(result);
    }

    // ---- Row 2: named TRow + named TCrud, constant name -> COVERED (adds TCrud JsonTypeInfo) -----------

    private const string NamedWritableView = @"
using System.Linq;

namespace App
{
    public enum CustomerKind { Regular, Premium }

    public sealed class CustomerRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Rank { get; set; }
        public CustomerKind Kind { get; set; }
    }

    public sealed class CustomerCrud
    {
        public string Name { get; set; } = string.Empty;
        public int? Rank { get; set; }
    }

    public sealed class CustomerEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Rank { get; set; }
    }

    public class CustomerWritableTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<CustomerRow>(""customers"", (db, sp) => new CustomerRow[0].AsQueryable())
                 .WithCrud<CustomerCrud, CustomerEntity>();
        }
    }
}
";

    [Test]
    public async Task Named_Writable_ConstantName_Is_Covered_Including_TCrud_JsonTypeInfo()
    {
        // Requirements 1.1, 1.3, 1.5, 1.7 — a nameable writable Style A view adds the write-model TCrud
        // JsonTypeInfo to the read-side artifacts (TCrud is always a named type, D38, and its members are
        // all emittable).
        var result = StyleAShapeGeneratorTestHarness.Run(NamedWritableView);

        await Assert.That(RecognizedCandidateCount(result)).IsEqualTo(1);

        var covered = DiagnosticsWithId(result, "VISTA0060");
        await Assert.That(covered.Length).IsEqualTo(1);
        await Assert.That(covered[0].Severity).IsEqualTo(DiagnosticSeverity.Info);

        var artifacts = Vista0060Artifacts(covered[0]);
        await Assert.That(artifacts)
            .IsEquivalentTo(new[] { ExportAccessors, ReadDtoJsonTypeInfo, TCrudJsonTypeInfo });

        await AssertNoDiagnostics(result, "VISTA0061", "VISTA0062", "VISTA0063");
        await AssertNoErrors(result);
    }

    // ---- Row 3: anonymous TRow + named TCrud, constant name -> TCrud-ONLY coverage + VISTA0061 ---------

    private const string AnonymousRowWithNamedCrudView = @"
using System.Linq;

namespace App
{
    public sealed class OrderCrud
    {
        public string Reference { get; set; } = string.Empty;
        public int? Quantity { get; set; }
    }

    public sealed class OrderEntity
    {
        public int Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public int? Quantity { get; set; }
    }

    public class OrderWritableTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView(""orders"", (db, sp) => new[] { new { Id = 1, Reference = ""x"" } }.AsQueryable())
                 .WithCrud<OrderCrud, OrderEntity>();
        }
    }
}
";

    [Test]
    public async Task Anonymous_Row_With_Named_TCrud_Is_TCrud_Only_Coverage_Plus_VISTA0061()
    {
        // Requirements 1.4, 1.5 — the D96 asymmetry within a single view: the anonymous read row is
        // unnameable (read stays RUC → VISTA0061), while the always-named TCrud is still covered (VISTA0060
        // lists ONLY "TCrud JsonTypeInfo" — no accessors, no read-DTO JsonTypeInfo).
        var result = StyleAShapeGeneratorTestHarness.Run(AnonymousRowWithNamedCrudView);

        await Assert.That(RecognizedCandidateCount(result)).IsEqualTo(1);

        var covered = DiagnosticsWithId(result, "VISTA0060");
        await Assert.That(covered.Length).IsEqualTo(1);
        await Assert.That(Vista0060Artifacts(covered[0])).IsEquivalentTo(new[] { TCrudJsonTypeInfo });

        // The read side stays on the reflection path by design (D96): exactly one VISTA0061 (Info) naming
        // the view.
        var anonymousRow = DiagnosticsWithId(result, "VISTA0061");
        await Assert.That(anonymousRow.Length).IsEqualTo(1);
        await Assert.That(anonymousRow[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(Names(anonymousRow[0]).Contains("'orders'", StringComparison.Ordinal)).IsTrue();

        await AssertNoDiagnostics(result, "VISTA0062", "VISTA0063");
        await AssertNoErrors(result);
    }

    // ---- Row 4: anonymous / object TRow, read-only, constant name -> NOT covered + VISTA0061 -----------

    private const string AnonymousReadOnlyView = @"
using System.Linq;

namespace App
{
    public class AnonymousReadOnlyTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView(""orders"", (db, sp) => new[] { new { Id = 1, Label = ""x"" } }.AsQueryable());
        }
    }
}
";

    private const string ObjectReadOnlyView = @"
using System.Linq;

namespace App
{
    public class ObjectReadOnlyTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<object>(""things"", (db, sp) => new object[0].AsQueryable());
        }
    }
}
";

    [Test]
    [Arguments("anonymous", "orders")]
    [Arguments("object", "things")]
    public async Task Anonymous_Or_Object_ReadOnly_ConstantName_Is_Not_Covered_And_Reports_VISTA0061(
        string rowKind, string viewName)
    {
        // Requirements 1.3, 1.4 — a read-only view whose row is unnameable (anonymous or object) has an
        // empty artifact set, so it is not "covered" (no VISTA0060) and only the by-design RUC note fires.
        var source = rowKind == "anonymous" ? AnonymousReadOnlyView : ObjectReadOnlyView;

        var result = StyleAShapeGeneratorTestHarness.Run(source);

        await Assert.That(RecognizedCandidateCount(result)).IsEqualTo(1);

        // No coverage is claimed for an anonymous/object read-only view.
        await Assert.That(DiagnosticsWithId(result, "VISTA0060").Length).IsEqualTo(0);

        var anonymousRow = DiagnosticsWithId(result, "VISTA0061");
        await Assert.That(anonymousRow.Length).IsEqualTo(1);
        await Assert.That(anonymousRow[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(Names(anonymousRow[0]).Contains("'" + viewName + "'", StringComparison.Ordinal))
            .IsTrue();

        await AssertNoDiagnostics(result, "VISTA0062", "VISTA0063");
        await AssertNoErrors(result);
    }

    // ---- Row 5: non-constant name -> hard-gated to VISTA0062 only --------------------------------------

    private const string NonConstantNameView = @"
using System.Linq;

namespace App
{
    public sealed class CustomerRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class NonConstantNameTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        private static string ViewName() => ""runtime"";

        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<CustomerRow>(ViewName(), (db, sp) => new CustomerRow[0].AsQueryable());
        }
    }
}
";

    [Test]
    public async Task NonConstant_Name_Is_Hard_Gated_To_VISTA0062_Only()
    {
        // Requirement 1.2 — a non-constant AddView name cannot be keyed statically, so the call site is a
        // recognized candidate but a HARD GATE fires: exactly one diagnostic, VISTA0062 (Info), naming the
        // enclosing template — and nothing else (no VISTA0060/0061/0063), even though the row is a named
        // type, because the generator returns immediately after reporting the gate.
        var result = StyleAShapeGeneratorTestHarness.Run(NonConstantNameView);

        await Assert.That(RecognizedCandidateCount(result)).IsEqualTo(1);

        await Assert.That(result.Diagnostics.Length).IsEqualTo(1);
        var only = result.Diagnostics[0];
        await Assert.That(only.Id).IsEqualTo("VISTA0062");
        await Assert.That(only.Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(Names(only).Contains("'NonConstantNameTemplate'", StringComparison.Ordinal))
            .IsTrue();
    }

    // ---- Row 6: named TRow with a non-emittable member -> read-DTO JsonTypeInfo skipped + VISTA0063 ----

    private const string NonEmittableMemberView = @"
using System.Linq;

namespace App
{
    public sealed class BadRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public System.IDisposable Handle { get; set; }
    }

    public class NonEmittableMemberTemplate
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<BadRow>(""bad"", (db, sp) => new BadRow[0].AsQueryable());
        }
    }
}
";

    [Test]
    public async Task NonEmittable_Member_Skips_ReadDto_JsonTypeInfo_But_Keeps_Accessors_Plus_VISTA0063()
    {
        // Requirements 1.3, 1.7 — a named-row view with a non-emittable member (an interface-typed field)
        // still gets its export accessor map (accessors are compile-time member access, independent of DTO
        // emittability), but the read-DTO JsonTypeInfo is withheld and VISTA0063 names the offending member.
        var result = StyleAShapeGeneratorTestHarness.Run(NonEmittableMemberView);

        await Assert.That(RecognizedCandidateCount(result)).IsEqualTo(1);

        var covered = DiagnosticsWithId(result, "VISTA0060");
        await Assert.That(covered.Length).IsEqualTo(1);
        // The set has ONLY "export accessors" — read-DTO JsonTypeInfo is skipped for the non-emittable DTO.
        await Assert.That(Vista0060Artifacts(covered[0])).IsEquivalentTo(new[] { ExportAccessors });

        var notEmittable = DiagnosticsWithId(result, "VISTA0063");
        await Assert.That(notEmittable.Length).IsEqualTo(1);
        await Assert.That(notEmittable[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);

        var message = Names(notEmittable[0]);
        await Assert.That(message.Contains("'bad'", StringComparison.Ordinal)).IsTrue();
        // The message names the offending member (BadRow.Handle) so the developer can find it.
        await Assert.That(message.Contains("Handle", StringComparison.Ordinal)).IsTrue();

        await AssertNoDiagnostics(result, "VISTA0061", "VISTA0062");
        await AssertNoErrors(result);
    }

    // ---- Row 7: not a Style A candidate -> no model, no Style A diagnostic ----------------------------

    // A look-alike builder whose AddView<TRow> is NOT a2n.Vista.Authoring.IViewTemplateBuilder.AddView.
    // The invocation matches the fast syntax predicate (invoked member named "AddView") but the semantic
    // transform rejects it (the declaring type is not IViewTemplateBuilder<TDbContext>), so it is dropped.
    private const string DecoyAddViewOnNonVistaType = @"
using System.Linq;

namespace App
{
    public sealed class CustomerRow
    {
        public int Id { get; set; }
    }

    public sealed class LookAlikeBuilder
    {
        public LookAlikeBuilder AddView<TRow>(string name, System.Func<TRow> factory)
            where TRow : class
            => this;
    }

    public class DecoyCaller
    {
        public void Run()
        {
            var builder = new LookAlikeBuilder();
            builder.AddView<CustomerRow>(""customers"", () => new CustomerRow());
        }
    }
}
";

    // The genuine Vista IViewTemplateBuilder.AddView, but called from a class that does NOT derive
    // a2n.Vista.Authoring.ViewTemplate<TDbContext>. The transform rejects it on the enclosing-type check,
    // so it is not a Style A call site.
    private const string AddViewOutsideTemplate = @"
using System.Linq;

namespace App
{
    public sealed class CustomerRow
    {
        public int Id { get; set; }
    }

    public class NotATemplate
    {
        public void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {
            views.AddView<CustomerRow>(""customers"", (db, sp) => new CustomerRow[0].AsQueryable());
        }
    }
}
";

    [Test]
    [Arguments("decoy-addview-not-vista")]
    [Arguments("addview-outside-template")]
    public async Task Non_Style_A_Invocations_Are_Not_Candidates_And_Report_No_Diagnostics(string shape)
    {
        // Requirement 1.1 — an AddView-named invocation that is NOT the Vista authoring method, or a genuine
        // AddView call NOT inside a ViewTemplate<TDbContext> subclass, is not a Style A call site: the
        // semantic transform drops it (no StyleAViewModel), so no Style A diagnostic is reported.
        var source = shape == "decoy-addview-not-vista" ? DecoyAddViewOnNonVistaType : AddViewOutsideTemplate;

        var result = StyleAShapeGeneratorTestHarness.Run(source);

        await Assert.That(RecognizedCandidateCount(result)).IsEqualTo(0);

        // The harness runs only StyleAShapeGenerator, so a non-candidate yields zero diagnostics.
        await Assert.That(result.Diagnostics.Length).IsEqualTo(0);
        await AssertNoDiagnostics(result, "VISTA0060", "VISTA0061", "VISTA0062", "VISTA0063");
    }

    // ---- helpers --------------------------------------------------------------------------------------

    /// <summary>
    /// The number of Style A call sites the semantic transform recognized: the count of outputs of the
    /// tracked <see cref="TrackingNames.StyleAViewModel"/> step (which is fed by the post-<c>Where</c>
    /// non-null models). A recognized call site contributes one output even when it is not keyable/coverable
    /// (a non-constant name); a dropped (non-candidate) invocation contributes none.
    /// </summary>
    private static int RecognizedCandidateCount(GeneratorDriverRunResult result)
    {
        var runResult = result.Results.Single();
        return runResult.TrackedSteps.TryGetValue(TrackingNames.StyleAViewModel, out var steps)
            ? steps.SelectMany(static step => step.Outputs).Count()
            : 0;
    }

    /// <summary>All diagnostics the generator reported with the given id.</summary>
    private static Diagnostic[] DiagnosticsWithId(GeneratorDriverRunResult result, string id)
        => result.Diagnostics.Where(d => d.Id == id).ToArray();

    /// <summary>The invariant-culture rendered message of a diagnostic (for substring/name assertions).</summary>
    private static string Names(Diagnostic diagnostic)
        => diagnostic.GetMessage(CultureInfo.InvariantCulture);

    /// <summary>Asserts the generator reported none of the given diagnostic ids.</summary>
    private static async Task AssertNoDiagnostics(GeneratorDriverRunResult result, params string[] ids)
    {
        foreach (var id in ids)
        {
            await Assert.That(DiagnosticsWithId(result, id).Length).IsEqualTo(0);
        }
    }

    /// <summary>Asserts the generator reported no Error-severity diagnostic (the family is non-blocking).</summary>
    private static async Task AssertNoErrors(GeneratorDriverRunResult result)
        => await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            .IsFalse();

    /// <summary>
    /// Parses the comma-separated artifact list that follows <see cref="Vista0060ArtifactMarker"/> in a
    /// VISTA0060 message back into its ordered tokens. Splitting on ", " is safe: no artifact name contains
    /// a comma.
    /// </summary>
    private static IReadOnlyList<string> Vista0060Artifacts(Diagnostic diagnostic)
    {
        var message = Names(diagnostic);
        var markerIndex = message.IndexOf(Vista0060ArtifactMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return Array.Empty<string>();
        }

        var list = message[(markerIndex + Vista0060ArtifactMarker.Length)..];
        return list.Split(new[] { ", " }, StringSplitOptions.None);
    }
}
