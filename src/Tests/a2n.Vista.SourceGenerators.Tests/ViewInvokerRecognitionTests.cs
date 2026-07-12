// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver recognition-matrix examples for the Phase 4 (M9, D123, source-generator-http-surface)
// ViewInvokerGenerator (task 3.3; requirements R1.1, R1.2, R1.3, R1.5). The generator is driven directly
// via CSharpGeneratorDriver over in-memory source (see ViewInvokerGeneratorTestHarness). Assertions read
// the RECOGNITION OUTCOME — which classes become dispatch candidates and their coverage flags — from the
// tracked ViewInvokerModel step (TrackingNames.ViewInvokerModel), NOT from any emitted invoker source
// (emission is task 6.1; diagnostic reporting is task 4.2).
//
// The recognition matrix (design.md "Coverage classification"):
//   * named View<TRow>                 -> candidate; IsWritable=false, HasNamedRowType=true,
//                                         HasNamedCrudType=false (read coverage only).
//   * named View<TRow, TCrud>          -> candidate; IsWritable=true, HasNamedRowType=true,
//                                         HasNamedCrudType=true (read + write coverage).
//   * View<TRow, object>               -> candidate; IsWritable=true, HasNamedRowType=true,
//                                         HasNamedCrudType=false (read coverage only, no write dispatch).
//   * View<object> / anonymous TQuery  -> candidate but UNCOVERED; HasNamedRowType=false
//                                         (VISTA0040 + reflection fallback, R1.1/R1.3).
//   * abstract / non-partial / not-View<...> -> NOT a candidate (dropped in the semantic transform, no
//                                         ViewInvokerModel produced, R1.3).

using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ViewInvokerRecognitionTests
{
    // Shared row/crud contract types reused across the matrix so each view source stays small. Kept in
    // one fragment appended to every view body. `Row` and `WriteCrud` are named types (coverable);
    // `object`/anonymous stand in for the uncovered shapes.
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

    // ---- CANDIDATE: named read-only View<TRow> (arity-1) -------------------------------------------

    private const string ReadOnlyNamedView = SharedTypes + @"
namespace App
{
    public partial class ReadOnlyNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    // ---- CANDIDATE: named writable View<TRow, TCrud> (arity-2) -------------------------------------

    private const string WritableNamedView = SharedTypes + @"
namespace App
{
    public partial class WritableNamedView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
    }
}
";

    // ---- CANDIDATE (read coverage only): View<TRow, object> — writable base, unnamed TCrud ---------

    private const string ObjectCrudView = SharedTypes + @"
namespace App
{
    public partial class ObjectCrudView : a2n.Vista.Authoring.View<Row, object>
    {
    }
}
";

    // ---- UNCOVERED: View<object> — the projected row type is `object`, not a named contract ---------

    private const string ObjectRowView = SharedTypes + @"
namespace App
{
    public partial class ObjectRowView : a2n.Vista.Authoring.View<object>
    {
    }
}
";

    // ---- NON-CANDIDATE: abstract view (arity-1, otherwise well-formed) ------------------------------

    private const string AbstractView = SharedTypes + @"
namespace App
{
    public abstract partial class AbstractNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    // ---- NON-CANDIDATE: non-partial view (arity-1, otherwise well-formed) ---------------------------

    private const string NonPartialView = SharedTypes + @"
namespace App
{
    public class NonPartialNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    // ---- NON-CANDIDATE: a partial class with a base list that is NOT a Vista View<...> --------------

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

    // ---- positive control: named read-only View<TRow> ----------------------------------------------

    [Test]
    public async Task Named_ReadOnly_View_Is_A_Read_Candidate()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(ReadOnlyNamedView);

        await Assert.That(result.IsRecognizedCandidate("ReadOnlyNamedView")).IsTrue();

        var view = result.RecognizedView("ReadOnlyNamedView");

        // R1.1: named TQuery on an arity-1 base → covered read candidate, not writable, no write coverage.
        await Assert.That(view.IsWritable).IsFalse();
        await Assert.That(view.HasNamedRowType).IsTrue();
        await Assert.That(view.HasNamedCrudType).IsFalse();
        await Assert.That(view.HasPublicParameterlessCtor).IsTrue();
    }

    // ---- positive control: named writable View<TRow, TCrud> ----------------------------------------

    [Test]
    public async Task Named_Writable_View_Is_A_Read_And_Write_Candidate()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(WritableNamedView);

        await Assert.That(result.IsRecognizedCandidate("WritableNamedView")).IsTrue();

        var view = result.RecognizedView("WritableNamedView");

        // R1.1 / R1.2: named TQuery + named TCrud on an arity-2 base → covered read+write candidate.
        await Assert.That(view.IsWritable).IsTrue();
        await Assert.That(view.HasNamedRowType).IsTrue();
        await Assert.That(view.HasNamedCrudType).IsTrue();
        await Assert.That(view.HasPublicParameterlessCtor).IsTrue();
    }

    // ---- read coverage only: View<TRow, object> ---------------------------------------------------

    [Test]
    public async Task Object_TCrud_View_Is_A_Read_Candidate_With_No_Write_Coverage()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(ObjectCrudView);

        await Assert.That(result.IsRecognizedCandidate("ObjectCrudView")).IsTrue();

        var view = result.RecognizedView("ObjectCrudView");

        // R1.2: a writable base whose TCrud is `object` keeps read coverage (HasNamedRowType) but gets no
        // write coverage (HasNamedCrudType=false) — no generated write dispatch/binding for it.
        await Assert.That(view.IsWritable).IsTrue();
        await Assert.That(view.HasNamedRowType).IsTrue();
        await Assert.That(view.HasNamedCrudType).IsFalse();
    }

    // ---- uncovered: View<object> (object/anonymous TQuery) -----------------------------------------

    [Test]
    public async Task Object_TQuery_View_Is_Recognized_But_Uncovered()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(ObjectRowView);

        // R1.1 / R1.3: it derives View<...> so it is still surfaced as a base candidate (a ViewInvokerModel
        // is produced), but its `object` TQuery makes it uncovered — HasNamedRowType is false, so no
        // dispatch invoker is generated and the view stays on the reflection fallback (VISTA0040).
        await Assert.That(result.IsRecognizedCandidate("ObjectRowView")).IsTrue();

        var view = result.RecognizedView("ObjectRowView");
        await Assert.That(view.HasNamedRowType).IsFalse();
        await Assert.That(view.HasNamedCrudType).IsFalse();
    }

    // ---- non-candidates: dropped in the transform, no ViewInvokerModel produced (R1.3) -------------

    [Test]
    [Arguments("AbstractView")]
    [Arguments("NonPartialView")]
    [Arguments("NonViewBaseView")]
    public async Task Non_Candidate_Shapes_Produce_No_ViewInvokerModel(string shape)
    {
        var (source, className) = shape switch
        {
            "AbstractView" => (AbstractView, "AbstractNamedView"),
            "NonPartialView" => (NonPartialView, "NonPartialNamedView"),
            _ => (NonViewBaseView, "NotAView"),
        };

        var result = ViewInvokerGeneratorTestHarness.Run(source);

        // R1.3: abstract, non-partial, and non-View<...> classes are not dispatch candidates — the
        // semantic transform drops them, so no ViewInvokerModel flows through the tagged step.
        await Assert.That(result.IsRecognizedCandidate(className)).IsFalse();
    }
}
