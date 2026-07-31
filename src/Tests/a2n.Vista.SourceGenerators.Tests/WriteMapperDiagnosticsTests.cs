// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver diagnostic examples for the Phase 3 (M9, D121/D122) WriteMapperGenerator (task 4.3;
// requirements R8.2, R8.3, R9.1, R9.2, R9.3, R9.5). The generator is driven directly via
// CSharpGeneratorDriver over in-memory source (see WriteMapperGeneratorTestHarness), and these examples
// assert the write-DSL analyzer's build-time diagnostic behavior:
//
//   * VISTA0030 (Error)  — a CRUD facet that declares zero MapWritable mappings; exactly one error is
//     reported for the view and no write mapper is emitted (R9.1, R9.5).
//   * VISTA0031 (Error)  — a MapWritable mapping whose target is a navigation (non-scalar) member; one
//     error per offending mapping, naming the view and the source/target members, and no write mapper
//     is emitted (R9.2, R9.5).
//   * VISTA0032 (Error)  — a MapWritable mapping whose target is a declared key member (.Key(...)) or the
//     concurrency token (WithConcurrencyToken); one error per offending member, naming the view and the
//     member, and no write mapper is emitted (R9.3, R9.5).
//   * VISTA0033 (Warning)— an unanalyzable MapWritable chain (a non-simple selector); a single warning
//     naming the offending view AND the offending expression, the build stays green (no error-severity
//     diagnostics), and the view falls back to reflection with no write mapper emitted (R8.2, R8.3).
//
// Only the generated source TEXT and the run diagnostics are inspected; no generated [ModuleInitializer]
// is executed.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class WriteMapperDiagnosticsTests
{
    // Shared entity/row/crud types reused across the matrix so each view source stays small.
    //   * Row      (TQuery)  — the read shape; carries the declarable key member Id.
    //   * Source   (TEntity) — the write target; Name/Quantity/RowVersion are scalar members, Related is
    //                          a navigation (non-scalar) target, RowVersion is the concurrency token.
    //   * WriteCrud (TCrud)  — the write contract exposing the members the MapWritable selectors read.
    private const string SharedTypes = @"
namespace App
{
    public sealed class Related
    {
        public int Id { get; set; }
    }

    public sealed class Source
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public byte[] RowVersion { get; set; } = System.Array.Empty<byte>();
        public Related Related { get; set; } = new Related();
    }

    public sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public sealed class WriteCrud
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public byte[] RowVersion { get; set; } = System.Array.Empty<byte>();
        public Related Related { get; set; } = new Related();
    }
}
";

    // ---- VISTA0030: CRUD facet with zero MapWritable mappings --------------------------------------

    // A recognized candidate (partial, derives View<Row, WriteCrud>, declares a CRUD facet via CrudOn)
    // that declares NO MapWritable mapping. The whitelist is empty, so the generated mapper would assign
    // nothing — an authoring mistake under default-deny mass assignment (R9.1).
    private const string ZeroMappingView = SharedTypes + @"
namespace App
{
    public partial class ZeroMappingView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder.CrudOn<Source>();
    }
}
";

    // ---- VISTA0031: MapWritable target is a navigation (non-scalar) member -------------------------

    // The second mapping targets e.Related — a Related reference (navigation), not a scalar member — so
    // exactly one VISTA0031 is reported for that mapping (the scalar Name mapping is fine) (R9.2).
    private const string NonScalarTargetView = SharedTypes + @"
namespace App
{
    public partial class NonScalarTargetView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name)
                .MapWritable(c => c.Related, e => e.Related);
    }
}
";

    // ---- VISTA0032 (declared key): MapWritable target is a declared key member ---------------------

    // Id is declared a key on the read side (.Key(x => x.Id), a separate statement so the arity-2 builder
    // type is preserved for CrudOn), and a mapping targets e.Id — a key member the mapper must never
    // assign, so one VISTA0032 is reported (R9.3). Id is scalar, so VISTA0031 does NOT also fire.
    private const string KeyTargetView = SharedTypes + @"
namespace App
{
    public partial class KeyTargetView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
        {
            builder.Key(x => x.Id);
            builder
                .CrudOn<Source>()
                .MapWritable(c => c.Id, e => e.Id)
                .MapWritable(c => c.Name, e => e.Name);
        }
    }
}
";

    // ---- VISTA0032 (declared key via nameof): audit SEC-05 ----------------------------------------

    // The SAME view, keyed with the string overload spelled as nameof(Row.Id) instead of a literal. The key
    // recognizer used to match only a LiteralExpressionSyntax, so nameof(...) — an invocation — recorded no
    // key at all, VISTA0032 never fired, and the generated mapper mass-assigned the primary key. The safer,
    // refactor-friendly spelling must be guarded exactly like ".Key(\"Id\")".
    private const string KeyTargetViaNameofView = SharedTypes + @"
namespace App
{
    public partial class KeyTargetViaNameofView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
        {
            builder.Key(nameof(Row.Id));
            builder
                .CrudOn<Source>()
                .MapWritable(c => c.Id, e => e.Id)
                .MapWritable(c => c.Name, e => e.Name);
        }
    }
}
";

    // The same shape keyed with a const string, the other compile-time constant spelling the semantic
    // constant lookup now covers uniformly.
    private const string KeyTargetViaConstView = SharedTypes + @"
namespace App
{
    public partial class KeyTargetViaConstView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        private const string IdField = ""Id"";

        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
        {
            builder.Key(IdField);
            builder
                .CrudOn<Source>()
                .MapWritable(c => c.Id, e => e.Id)
                .MapWritable(c => c.Name, e => e.Name);
        }
    }
}
";

    // ---- VISTA0032 (concurrency token): MapWritable target is the concurrency token ----------------

    // RowVersion is the concurrency token (WithConcurrencyToken(e => e.RowVersion)) and a mapping targets
    // e.RowVersion — the token the mapper must never assign, so one VISTA0032 is reported (R9.3).
    // RowVersion is a byte[] (scalar per the shared rule), so VISTA0031 does NOT also fire.
    private const string TokenTargetView = SharedTypes + @"
namespace App
{
    public partial class TokenTargetView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .WithConcurrencyToken(e => e.RowVersion)
                .MapWritable(c => c.Name, e => e.Name)
                .MapWritable(c => c.RowVersion, e => e.RowVersion);
    }
}
";

    // ---- VISTA0033: unanalyzable MapWritable chain (non-simple selector) ---------------------------

    // The source selector is a method call (c => c.Name.ToUpper()), not a simple member selection, so the
    // chain is not statically analyzable: no mapper is emitted, the build stays green, and a single
    // VISTA0033 warning names both the view and the offending expression (R8.2, R8.3).
    private const string UnanalyzableView = SharedTypes + @"
namespace App
{
    public partial class UnanalyzableView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name.ToUpper(), e => e.Name);
    }
}
";

    // ---- VISTA0030 ---------------------------------------------------------------------------------

    [Test]
    public async Task Zero_Mapping_Facet_Reports_One_Vista0030_Error_And_Emits_No_Mapper()
    {
        var result = WriteMapperGeneratorTestHarness.Run(ZeroMappingView);

        // R9.1: exactly one VISTA0030, at error severity, naming the offending view.
        var vista0030 = result.Diagnostics.Where(static d => d.Id == "VISTA0030").ToArray();
        await Assert.That(vista0030.Length).IsEqualTo(1);
        await Assert.That(vista0030[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(vista0030[0].GetMessage().Contains("ZeroMappingView", StringComparison.Ordinal)).IsTrue();

        // R9.5: no write mapper is emitted for the erroring view.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaWriteMapper")).IsFalse();
    }

    // ---- VISTA0031 ---------------------------------------------------------------------------------

    [Test]
    public async Task Non_Scalar_Target_Reports_Vista0031_Error_And_Emits_No_Mapper()
    {
        var result = WriteMapperGeneratorTestHarness.Run(NonScalarTargetView);

        // R9.2: one VISTA0031 for the single non-scalar (navigation) target mapping, at error severity,
        // naming the view and the offending target member.
        var vista0031 = result.Diagnostics.Where(static d => d.Id == "VISTA0031").ToArray();
        await Assert.That(vista0031.Length).IsEqualTo(1);
        await Assert.That(vista0031[0].Severity).IsEqualTo(DiagnosticSeverity.Error);

        var message = vista0031[0].GetMessage();
        await Assert.That(message.Contains("NonScalarTargetView", StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains("Related", StringComparison.Ordinal)).IsTrue();

        // R9.5: no write mapper is emitted for the erroring view.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaWriteMapper")).IsFalse();
    }

    // ---- VISTA0032 (key and token) -----------------------------------------------------------------

    [Test]
    [Arguments("KeyTargetView", "Id")]
    [Arguments("KeyTargetViaNameofView", "Id")]
    [Arguments("KeyTargetViaConstView", "Id")]
    [Arguments("TokenTargetView", "RowVersion")]
    public async Task Key_Or_Token_Target_Reports_Vista0032_Error_And_Emits_No_Mapper(
        string shape,
        string offendingMember)
    {
        var source = shape switch
        {
            "KeyTargetView" => KeyTargetView,
            "KeyTargetViaNameofView" => KeyTargetViaNameofView,
            "KeyTargetViaConstView" => KeyTargetViaConstView,
            _ => TokenTargetView,
        };

        var result = WriteMapperGeneratorTestHarness.Run(source);

        // R9.3: one VISTA0032 for the single key/token target, at error severity, naming the view and the
        // offending member.
        var vista0032 = result.Diagnostics.Where(static d => d.Id == "VISTA0032").ToArray();
        await Assert.That(vista0032.Length).IsEqualTo(1);
        await Assert.That(vista0032[0].Severity).IsEqualTo(DiagnosticSeverity.Error);

        var message = vista0032[0].GetMessage();
        await Assert.That(message.Contains(shape, StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(offendingMember, StringComparison.Ordinal)).IsTrue();

        // R9.5: no write mapper is emitted for the erroring view.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaWriteMapper")).IsFalse();
    }

    // ---- VISTA0033 ---------------------------------------------------------------------------------

    [Test]
    public async Task Unanalyzable_Chain_Reports_Vista0033_Warning_Naming_View_And_Expression_And_Build_Succeeds()
    {
        var result = WriteMapperGeneratorTestHarness.Run(UnanalyzableView);

        // R8.2: exactly one VISTA0033 warning, naming the offending view AND the offending expression.
        var vista0033 = result.Diagnostics.Where(static d => d.Id == "VISTA0033").ToArray();
        await Assert.That(vista0033.Length).IsEqualTo(1);
        await Assert.That(vista0033[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);

        var message = vista0033[0].GetMessage();
        await Assert.That(message.Contains("UnanalyzableView", StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains("ToUpper", StringComparison.Ordinal)).IsTrue();

        // R8.3: the build stays green — no error-severity diagnostic is raised for the fallback view.
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();

        // R8.1: no write mapper is emitted; the view falls back to reflection.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaWriteMapper")).IsFalse();
    }
}
