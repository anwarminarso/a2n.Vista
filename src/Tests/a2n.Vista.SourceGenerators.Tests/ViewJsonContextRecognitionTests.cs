// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver recognition + shape-matrix examples for the Phase 5 (M9, D125/D126,
// source-generator-json-typeinfo) ViewJsonContextGenerator (task 2.4; requirements R1.1, R1.2, R1.3, R1.4,
// R1.5, R1.7). The generator is driven directly via CSharpGeneratorDriver over in-memory source (see
// ViewJsonContextGeneratorTestHarness). These are EXAMPLE (data-driven / fact) tests — the companion
// property test for VISTA0050/VISTA0051 conformance is Vista0050CoverageAndDiagnosticConformancePropertyTests.
//
// Two observable outcomes are asserted per matrix row:
//   * The RECOGNITION outcome — whether the semantic transform produced a ViewJsonContextModel and, if so,
//     its coverage flags — read back from the tracked ViewJsonContextModel step
//     (TrackingNames.ViewJsonContextModel) via the harness projection.
//   * The COVERAGE outcome — the non-blocking VISTA0050 (covered) / VISTA0051 (candidate with a
//     non-emittable member) diagnostics the source-output stage reports, and whether a per-view context
//     source is emitted (task 5.1; here "no context" is the forward-compatible assertion for a not-covered
//     view).
//
// The recognition + shape matrix (design.md "Coverage classification"):
//   * named View<TRow> (arity-1), all emittable          -> COVERED (VISTA0050, set has no TCrud).
//   * named View<TRow, TCrud> (arity-2), all emittable   -> COVERED (VISTA0050, set includes TCrud).
//   * View<TRow, object>, read DTOs emittable            -> COVERED for read DTOs only (VISTA0050, no
//                                                           TCrud; HasNamedCrudType == false).
//   * a candidate with a non-emittable DTO member        -> NOT COVERED (VISTA0051, no VISTA0050, no
//                                                           context).
//   * View<object> / anonymous TQuery                    -> NOT a serialization candidate (a model is
//                                                           produced but HasNamedRowType == false; no
//                                                           diagnostic).
//   * abstract / non-partial / not-View<...>             -> NOT a candidate (dropped in the transform, no
//                                                           ViewJsonContextModel produced, no diagnostic).
//   * no public parameterless ctor (else covered)        -> NO emission: coverage is not claimed (no
//                                                           VISTA0050) because the [ModuleInitializer]
//                                                           cannot read the view's runtime Name (R1.7).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ViewJsonContextRecognitionTests
{
    // Shared contract types reused across the matrix so each view source stays small. `Row`/`WriteCrud`
    // carry only EMITTABLE members (a scalar, a string, a nullable value type, and an enum), so the view's
    // coverage turns purely on its recognition shape. `BadRow` carries a NON-EMITTABLE `object` member
    // (`Payload`) — an unsupported polymorphic shape the generator cannot emit reflection-free via
    // JsonMetadataServices (design.md Emittable_Shape table).
    private const string SharedTypes = @"
namespace App
{
    public enum RowKind { Alpha, Beta }

    public sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Score { get; set; }
        public RowKind Kind { get; set; }
    }

    public sealed class WriteCrud
    {
        public string Name { get; set; } = string.Empty;
        public int? Score { get; set; }
    }

    public sealed class BadRow
    {
        public int Id { get; set; }
        public object Payload { get; set; } = new object();
    }
}
";

    // The global::-qualified Serializable_DTO_Set names the VISTA0050 message composes for a view whose
    // projected row type is App.Row (design.md "Coverage classification", R9.1).
    private const string RowFqn = "global::App.Row";
    private const string ViewListResultFqn = "global::a2n.Vista.Ports.ViewListResult<global::App.Row>";
    private const string PagedResultFqn = "global::a2n.Vista.Results.PagedResult<global::App.Row>";
    private const string CrudFqn = "global::App.WriteCrud";

    // The fixed message marker after which the {1} type list begins (see DiagnosticDescriptors.VISTA0050).
    private const string Vista0050TypeListMarker = "optional for these types: ";

    // ---- COVERED: named read-only View<TRow> with emittable DTOs ------------------------------------

    private const string ReadOnlyNamedView = SharedTypes + @"
namespace App
{
    public partial class ReadOnlyNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    [Test]
    public async Task Named_ReadOnly_View_With_Emittable_Dtos_Is_Covered_Without_TCrud()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(ReadOnlyNamedView);

        // Recognition: a serialization candidate, read-only, all shapes emittable.
        var view = result.RecognizedJsonContextView("ReadOnlyNamedView");
        await Assert.That(view.IsWritable).IsFalse();
        await Assert.That(view.HasNamedRowType).IsTrue();
        await Assert.That(view.HasNamedCrudType).IsFalse();
        await Assert.That(view.HasPublicParameterlessCtor).IsTrue();
        await Assert.That(view.AllShapesEmittable).IsTrue();

        // Coverage: exactly one VISTA0050 (Info), no VISTA0051, build green (R1.1, R1.4, R9.1).
        var vista0050 = result.Diagnostics.Where(static d => d.Id == "VISTA0050").ToArray();
        await Assert.That(vista0050.Length).IsEqualTo(1);
        await Assert.That(vista0050[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0051")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();

        // The DTO set is exactly { TRow, ViewListResult<TRow>, PagedResult<TRow> } for a read-only view.
        var dtoSet = Vista0050DtoSet(vista0050[0]);
        await Assert.That(dtoSet).IsEquivalentTo(new[] { RowFqn, ViewListResultFqn, PagedResultFqn });

        // A read-only view has no TCrud, so the set must not name the write model.
        await Assert.That(dtoSet.Contains(CrudFqn)).IsFalse();

        // The message names the view.
        await Assert.That(vista0050[0].GetMessage().Contains("ReadOnlyNamedView", StringComparison.Ordinal))
            .IsTrue();
    }

    // ---- COVERED: named writable View<TRow, TCrud> with emittable DTOs → set includes TCrud ---------

    private const string WritableNamedView = SharedTypes + @"
namespace App
{
    public partial class WritableNamedView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
    }
}
";

    [Test]
    public async Task Named_Writable_View_With_Emittable_Dtos_Is_Covered_Including_TCrud()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(WritableNamedView);

        // Recognition: a writable serialization candidate with a named TCrud, all shapes emittable.
        var view = result.RecognizedJsonContextView("WritableNamedView");
        await Assert.That(view.IsWritable).IsTrue();
        await Assert.That(view.HasNamedRowType).IsTrue();
        await Assert.That(view.HasNamedCrudType).IsTrue();
        await Assert.That(view.AllShapesEmittable).IsTrue();

        // Coverage: one VISTA0050 (Info), no VISTA0051 (R1.2, R9.1).
        var vista0050 = result.Diagnostics.Where(static d => d.Id == "VISTA0050").ToArray();
        await Assert.That(vista0050.Length).IsEqualTo(1);
        await Assert.That(vista0050[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0051")).IsFalse();

        // The DTO set adds TCrud for a writable view with a named write model.
        var dtoSet = Vista0050DtoSet(vista0050[0]);
        await Assert.That(dtoSet)
            .IsEquivalentTo(new[] { RowFqn, ViewListResultFqn, PagedResultFqn, CrudFqn });
    }

    // ---- COVERED for read DTOs only: View<TRow, object> — writable base, unnamed TCrud --------------

    private const string ObjectCrudView = SharedTypes + @"
namespace App
{
    public partial class ObjectCrudView : a2n.Vista.Authoring.View<Row, object>
    {
    }
}
";

    [Test]
    public async Task Writable_Object_TCrud_View_Is_Covered_For_Read_Dtos_Only()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(ObjectCrudView);

        // Recognition: writable base, but the `object` TCrud gives read coverage only (R1.2).
        var view = result.RecognizedJsonContextView("ObjectCrudView");
        await Assert.That(view.IsWritable).IsTrue();
        await Assert.That(view.HasNamedRowType).IsTrue();
        await Assert.That(view.HasNamedCrudType).IsFalse();
        await Assert.That(view.AllShapesEmittable).IsTrue();

        // Coverage: one VISTA0050 (Info) whose DTO set is the READ DTOs only — no TCrud (R1.2, R9.1).
        var vista0050 = result.Diagnostics.Where(static d => d.Id == "VISTA0050").ToArray();
        await Assert.That(vista0050.Length).IsEqualTo(1);
        await Assert.That(vista0050[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0051")).IsFalse();

        var dtoSet = Vista0050DtoSet(vista0050[0]);
        await Assert.That(dtoSet).IsEquivalentTo(new[] { RowFqn, ViewListResultFqn, PagedResultFqn });
        await Assert.That(dtoSet.Any(t => t.Contains("object", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    // ---- NOT COVERED: a candidate with a non-emittable DTO member -----------------------------------

    private const string NonEmittableMemberView = SharedTypes + @"
namespace App
{
    public partial class NonEmittableMemberView : a2n.Vista.Authoring.View<BadRow>
    {
    }
}
";

    [Test]
    public async Task NonEmittable_Member_View_Is_Not_Covered_And_Emits_No_Context()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(NonEmittableMemberView);

        // Recognition: a genuine serialization candidate (named TQuery) that is nevertheless not covered
        // because its row DTO has a non-emittable member (R1.5).
        var view = result.RecognizedJsonContextView("NonEmittableMemberView");
        await Assert.That(view.HasNamedRowType).IsTrue();
        await Assert.That(view.AllShapesEmittable).IsFalse();

        // Coverage: exactly one VISTA0051 (Warning) naming the view and the offending member; no VISTA0050;
        // no per-view context emitted; build green (R1.5, R9.2).
        var vista0051 = result.Diagnostics.Where(static d => d.Id == "VISTA0051").ToArray();
        await Assert.That(vista0051.Length).IsEqualTo(1);
        await Assert.That(vista0051[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);

        var message = vista0051[0].GetMessage();
        await Assert.That(message.Contains("NonEmittableMemberView", StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains("Payload", StringComparison.Ordinal)).IsTrue();

        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0050")).IsFalse();
        await Assert.That(result.HasGeneratedContextFor("NonEmittableMemberView")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ---- NOT a serialization candidate: View<object> (object/anonymous TQuery) ----------------------

    private const string ObjectRowView = SharedTypes + @"
namespace App
{
    public partial class ObjectRowView : a2n.Vista.Authoring.View<object>
    {
    }
}
";

    [Test]
    public async Task Object_TQuery_View_Is_Not_A_Serialization_Candidate_And_Gets_No_Diagnostic()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(ObjectRowView);

        // It derives View<...>, so a base model is still surfaced — but its `object` TQuery makes it a
        // non-serialization-candidate: HasNamedRowType is false (R1.1, R1.3).
        var view = result.RecognizedJsonContextView("ObjectRowView");
        await Assert.That(view.HasNamedRowType).IsFalse();
        await Assert.That(view.HasNamedCrudType).IsFalse();

        // No serialization-context diagnostic is reported for a non-candidate, and no context is emitted;
        // it stays on the developer App_Json_Context / reflection fallback (R1.3).
        await Assert.That(result.Diagnostics.Any(static d => d.Id is "VISTA0050" or "VISTA0051")).IsFalse();
        await Assert.That(result.HasGeneratedContextFor("ObjectRowView")).IsFalse();
    }

    // ---- NOT a candidate: dropped in the transform, no ViewJsonContextModel produced (R1.3) ---------

    private const string AbstractView = SharedTypes + @"
namespace App
{
    public abstract partial class AbstractNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    private const string NonPartialView = SharedTypes + @"
namespace App
{
    public class NonPartialNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    private const string NonViewBaseView = SharedTypes + @"
namespace App
{
    public class SomeBase
    {
    }

    public partial class NotAView : SomeBase
    {
    }
}
";

    [Test]
    [Arguments("AbstractView", "AbstractNamedView")]
    [Arguments("NonPartialView", "NonPartialNamedView")]
    [Arguments("NonViewBaseView", "NotAView")]
    public async Task Non_Candidate_Shapes_Produce_No_Model_And_No_Diagnostic(string shape, string className)
    {
        var source = shape switch
        {
            "AbstractView" => AbstractView,
            "NonPartialView" => NonPartialView,
            _ => NonViewBaseView,
        };

        var result = ViewJsonContextGeneratorTestHarness.Run(source);

        // R1.3: abstract, non-partial, and non-View<...> classes are not serialization candidates — the
        // semantic transform drops them, so no ViewJsonContextModel flows through the tracked step.
        await Assert.That(result.IsRecognizedJsonContextCandidate(className)).IsFalse();

        // No serialization-context diagnostic and no emitted context for a dropped class.
        await Assert.That(result.Diagnostics.Any(static d => d.Id is "VISTA0050" or "VISTA0051")).IsFalse();
        await Assert.That(result.HasGeneratedContextFor(className)).IsFalse();
    }

    // ---- NO emission: a covered shape with no public parameterless ctor (R1.7) ----------------------

    private const string NoParameterlessCtorView = SharedTypes + @"
namespace App
{
    public partial class NoParameterlessCtorView : a2n.Vista.Authoring.View<Row>
    {
        public NoParameterlessCtorView(int seed)
        {
        }
    }
}
";

    [Test]
    public async Task No_Public_Parameterless_Ctor_View_Claims_No_Coverage()
    {
        var result = ViewJsonContextGeneratorTestHarness.Run(NoParameterlessCtorView);

        // Recognition: a covered SHAPE (named TQuery, all emittable) that nonetheless lacks a public
        // parameterless ctor, so the [ModuleInitializer] cannot read its runtime Name (R1.7).
        var view = result.RecognizedJsonContextView("NoParameterlessCtorView");
        await Assert.That(view.HasNamedRowType).IsTrue();
        await Assert.That(view.AllShapesEmittable).IsTrue();
        await Assert.That(view.HasPublicParameterlessCtor).IsFalse();

        // No coverage is claimed (no VISTA0050) and no context is emitted — the App_Json_Context stays
        // required for it. It is not a non-emittable failure either, so no VISTA0051. Build green (R1.7).
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0050")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0051")).IsFalse();
        await Assert.That(result.HasGeneratedContextFor("NoParameterlessCtorView")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ---- helper -------------------------------------------------------------------------------------

    /// <summary>
    /// Parses the comma-separated Serializable_DTO_Set that follows the fixed marker in a VISTA0050
    /// message back into its ordered global::-qualified type names. Splitting on ", " is safe: each
    /// generic argument is a single type, so no comma occurs inside a name.
    /// </summary>
    private static IReadOnlyList<string> Vista0050DtoSet(Diagnostic diagnostic)
    {
        var message = diagnostic.GetMessage();
        var markerIndex = message.IndexOf(Vista0050TypeListMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return Array.Empty<string>();
        }

        var list = message[(markerIndex + Vista0050TypeListMarker.Length)..];
        return list.Split(new[] { ", " }, StringSplitOptions.None);
    }
}
