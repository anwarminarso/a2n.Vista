// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver diagnostic examples for the Phase 4 (M9, D123, source-generator-http-surface)
// ViewInvokerGenerator (task 4.5; requirements R9.1, R9.2, and the non-blocking-severity guard R9.4). The
// generator is driven directly via CSharpGeneratorDriver over in-memory source (see
// ViewInvokerGeneratorTestHarness) and these examples assert the HTTP-surface diagnostics the source-output
// stage (Emit, task 4.2) reports — the diagnostic IDs, their Info severity, their count, and the message
// content — NOT any emitted invoker source (emission is task 6.1):
//
//   * VISTA0040 (Info) — one per recognized base candidate that cannot receive generated dispatch (its
//     TQuery is anonymous/object, HasNamedRowType == false); no invoker is generated, the view stays on
//     the reflection dispatch fallback, and the build stays green (R9.1, R9.4).
//   * VISTA0041 (Info) — one per covered view (HasNamedRowType == true), naming the exact [JsonSerializable]
//     type set the developer registers via AddVistaJsonContext(...): { TRow, ViewListResult<TRow>,
//     PagedResult<TRow> } plus TCrud iff the view is writable with a named TCrud (R9.2, R9.4).
//
// Every HTTP-surface diagnostic is Info severity — never Error — so an uncovered view is a valid, working
// view on the reflection fallback and the build is always green (R9.4).

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ViewInvokerDiagnosticsTests
{
    // Shared row/crud contract types reused across the matrix so each view source stays small. `Row` and
    // `WriteCrud` are named types (coverable); `object` stands in for the uncovered projected-row shape.
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

    // The global::-qualified [JsonSerializable] type names the VISTA0041 guidance composes for a view whose
    // projected row type is App.Row (design.md "VISTA0041 serialization-guidance type set", R5.4/R9.2).
    private const string RowFqn = "global::App.Row";
    private const string ViewListResultFqn = "global::a2n.Vista.Ports.ViewListResult<global::App.Row>";
    private const string PagedResultFqn = "global::a2n.Vista.Results.PagedResult<global::App.Row>";
    private const string CrudFqn = "global::App.WriteCrud";

    // ---- UNCOVERED: View<object> — the projected row type is `object`, not a named contract -----------

    private const string ObjectRowView = SharedTypes + @"
namespace App
{
    public partial class ObjectRowView : a2n.Vista.Authoring.View<object>
    {
    }
}
";

    // ---- COVERED (read-only): named View<TRow> --------------------------------------------------------

    private const string ReadOnlyNamedView = SharedTypes + @"
namespace App
{
    public partial class ReadOnlyNamedView : a2n.Vista.Authoring.View<Row>
    {
    }
}
";

    // ---- COVERED (writable): named View<TRow, TCrud> -------------------------------------------------

    private const string WritableNamedView = SharedTypes + @"
namespace App
{
    public partial class WritableNamedView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
    }
}
";

    // ---- MIXED: a covered writable view alongside an uncovered View<object> in one compilation --------

    private const string MixedViews = SharedTypes + @"
namespace App
{
    public partial class WritableNamedView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
    }

    public partial class ObjectRowView : a2n.Vista.Authoring.View<object>
    {
    }
}
";

    // ---- VISTA0040: anonymous/object TQuery → exactly one Info diagnostic, build green ---------------

    [Test]
    public async Task Object_TQuery_View_Reports_Single_VISTA0040_Info_And_Build_Green()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(ObjectRowView);

        // R9.1: an uncovered candidate (object TQuery) reports exactly one VISTA0040, at Info severity,
        // naming the offending view.
        var vista0040 = result.Diagnostics.Where(static d => d.Id == "VISTA0040").ToArray();
        await Assert.That(vista0040.Length).IsEqualTo(1);
        await Assert.That(vista0040[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(vista0040[0].GetMessage().Contains("ObjectRowView", StringComparison.Ordinal)).IsTrue();

        // No serialization guidance for an uncovered view — it never receives generated dispatch.
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0041")).IsFalse();

        // R9.4: the build stays green — the view falls back to reflection dispatch (no error severity).
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ---- VISTA0041: covered read-only view → one Info diagnostic naming the read type set (no TCrud) --

    [Test]
    public async Task Covered_ReadOnly_View_Reports_Single_VISTA0041_Naming_Read_Types_Without_TCrud()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(ReadOnlyNamedView);

        // R9.2: a covered read-only view reports exactly one VISTA0041, at Info severity, naming the view.
        var vista0041 = result.Diagnostics.Where(static d => d.Id == "VISTA0041").ToArray();
        await Assert.That(vista0041.Length).IsEqualTo(1);
        await Assert.That(vista0041[0].Severity).IsEqualTo(DiagnosticSeverity.Info);

        var message = vista0041[0].GetMessage();
        await Assert.That(message.Contains("ReadOnlyNamedView", StringComparison.Ordinal)).IsTrue();

        // The type set is exactly { TRow, ViewListResult<TRow>, PagedResult<TRow> } for a read-only view.
        await Assert.That(message.Contains(RowFqn, StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(ViewListResultFqn, StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(PagedResultFqn, StringComparison.Ordinal)).IsTrue();

        // A read-only view has no TCrud, so the guidance must not name the write model.
        await Assert.That(message.Contains(CrudFqn, StringComparison.Ordinal)).IsFalse();

        // A covered view is not uncovered — no VISTA0040 — and the build is green.
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0040")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ---- VISTA0041: covered writable view → the guidance also names TCrud ----------------------------

    [Test]
    public async Task Covered_Writable_View_Reports_VISTA0041_Also_Naming_TCrud()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(WritableNamedView);

        // R9.2: one VISTA0041 (Info) for the covered writable view.
        var vista0041 = result.Diagnostics.Where(static d => d.Id == "VISTA0041").ToArray();
        await Assert.That(vista0041.Length).IsEqualTo(1);
        await Assert.That(vista0041[0].Severity).IsEqualTo(DiagnosticSeverity.Info);

        var message = vista0041[0].GetMessage();
        await Assert.That(message.Contains("WritableNamedView", StringComparison.Ordinal)).IsTrue();

        // The full type set for a writable view is { TRow, ViewListResult<TRow>, PagedResult<TRow>, TCrud }.
        await Assert.That(message.Contains(RowFqn, StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(ViewListResultFqn, StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(PagedResultFqn, StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(CrudFqn, StringComparison.Ordinal)).IsTrue();

        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0040")).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    // ---- Non-blocking: every HTTP-surface diagnostic is Info, never Error (R9.4) ---------------------

    [Test]
    public async Task All_HttpSurface_Diagnostics_Are_Info_Never_Error()
    {
        var result = ViewInvokerGeneratorTestHarness.Run(MixedViews);

        // The mixed compilation raises both VISTA0041 (covered writable view) and VISTA0040 (uncovered
        // object-row view) — one each — proving independent per-view reporting.
        var httpSurface = result.Diagnostics
            .Where(static d => d.Id is "VISTA0040" or "VISTA0041")
            .ToArray();
        await Assert.That(httpSurface.Length).IsEqualTo(2);
        await Assert.That(result.Diagnostics.Count(static d => d.Id == "VISTA0040")).IsEqualTo(1);
        await Assert.That(result.Diagnostics.Count(static d => d.Id == "VISTA0041")).IsEqualTo(1);

        // R9.4: every HTTP-surface diagnostic is Info severity — never Error, never Warning — so the build
        // is always green and an uncovered view stays a valid, working view on the reflection fallback.
        await Assert.That(httpSurface.All(static d => d.Severity == DiagnosticSeverity.Info)).IsTrue();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }
}
