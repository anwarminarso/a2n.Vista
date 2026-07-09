// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Generator-driver recognition-matrix examples for the Phase 3 (M9, D121/D122) WriteMapperGenerator
// (task 2.3; requirements R1.1, R1.2, R1.4, R1.5). The generator is driven directly via
// CSharpGeneratorDriver over in-memory source (see WriteMapperGeneratorTestHarness).
//
// A view is RECOGNIZED as a write-mapper candidate — and, when it is well-formed, produces a
// <View>_VistaWriteMapper.g.cs generated source — only when it is a non-abstract, `partial` class that
// derives a2n.Vista.Authoring.View<TQuery, TCrud> (arity-2, typed TCrud) and declares a CRUD facet with
// an analyzable MapWritable chain (R1.1, R1.3). Every other shape is NOT emitted:
//   * abstract / non-partial / read-only View<TQuery> (arity-1) / no CRUD facet — dropped in the
//     semantic transform, no generated source and NO VISTA diagnostic (R1.2);
//   * TCrud = object (no named write contract) — kept as a candidate but skipped silently, no source,
//     no diagnostic (R1.4);
//   * a non-simple MapWritable selector — recognized as a candidate but not statically analyzable, so
//     no source is emitted and the view falls back to reflection with a VISTA0033 warning (R1.5, R8).
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

public sealed class WriteMapperRecognitionTests
{
    // Shared entity/row/crud types reused across the matrix so each view source stays small. Kept in one
    // fragment appended to every view body. The scalar members (Name/Quantity) are safe write targets;
    // Id is a declared key on the read side; Related is a navigation (non-scalar).
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
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public Related Related { get; set; } = new Related();
    }
}
";

    // ---- CANDIDATE (positive control): a well-formed typed Style B writable view emits a mapper ------

    // Non-abstract, partial, derives View<Row, WriteCrud>, declares a CRUD facet with a single analyzable
    // scalar mapping (Name -> Name). It is recognized AND emitted (R1.1, R1.3): a <View>_VistaWriteMapper
    // source is produced with no diagnostics.
    private const string CandidateView = SharedTypes + @"
namespace App
{
    public partial class WritableView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name);
    }
}
";

    // ---- NON-CANDIDATE: abstract view -------------------------------------------------------------

    private const string AbstractView = SharedTypes + @"
namespace App
{
    public abstract partial class AbstractWritableView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name);
    }
}
";

    // ---- NON-CANDIDATE: non-partial view ----------------------------------------------------------

    private const string NonPartialView = SharedTypes + @"
namespace App
{
    public class NonPartialWritableView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name, e => e.Name);
    }
}
";

    // ---- NON-CANDIDATE: read-only View<TQuery> (arity-1, no TCrud, no write facet) -----------------

    private const string ReadOnlyView = SharedTypes + @"
namespace App
{
    public partial class ReadOnlyStyleBView : a2n.Vista.Authoring.View<Row>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row> builder)
            => builder
                .From<Source>(src => new Row { Id = src.Id, Name = src.Name, Quantity = src.Quantity })
                .Field(x => x.Id, f => f.PrimaryKey());
    }
}
";

    // ---- NON-CANDIDATE: derives View<TQuery, TCrud> but declares NO CRUD facet ----------------------

    private const string NoFacetView = SharedTypes + @"
namespace App
{
    public partial class NoFacetWritableView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .From<Source>(src => new Row { Id = src.Id, Name = src.Name, Quantity = src.Quantity })
                .Field(x => x.Id, f => f.PrimaryKey());
    }
}
";

    // ---- NON-EMITTED (silent): TCrud = object (no named write contract, R1.4) -----------------------

    private const string ObjectCrudView = SharedTypes + @"
namespace App
{
    public partial class ObjectCrudView : a2n.Vista.Authoring.View<Row, object>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, object> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.ToString(), e => e.Name);
    }
}
";

    // ---- NON-EMITTED (reflection fallback): a non-simple MapWritable selector (R1.5, R8) ------------

    // The source selector is a method call (c => c.Name.ToUpper()), not a simple member selection, so the
    // view is a recognized candidate but not statically analyzable: no mapper is emitted and a VISTA0033
    // warning is raised (the view falls back to the reflection mapper).
    private const string NonSimpleSelectorView = SharedTypes + @"
namespace App
{
    public partial class NonSimpleSelectorView : a2n.Vista.Authoring.View<Row, WriteCrud>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)
            => builder
                .CrudOn<Source>()
                .MapWritable(c => c.Name.ToUpper(), e => e.Name);
    }
}
";

    // ---- positive control --------------------------------------------------------------------------

    [Test]
    public async Task Candidate_View_Is_Recognized_And_Emits_A_Write_Mapper()
    {
        var result = WriteMapperGeneratorTestHarness.Run(CandidateView);

        // R1.1 / R1.3: the well-formed candidate is recognized and its write mapper is generated.
        await Assert.That(result.HasGeneratedSourceContaining("WritableView_VistaWriteMapper")).IsTrue();

        // A well-formed candidate raises no diagnostics at all.
        await Assert.That(result.Diagnostics.Any(static d => d.Id.StartsWith("VISTA", StringComparison.Ordinal))).IsFalse();
    }

    // ---- non-candidate shapes: dropped in the transform, no source, no diagnostic (R1.2) -----------

    [Test]
    [Arguments("AbstractView")]
    [Arguments("NonPartialView")]
    [Arguments("ReadOnlyView")]
    [Arguments("NoFacetView")]
    public async Task Non_Candidate_Shapes_Emit_No_Mapper_And_No_Diagnostic(string shape)
    {
        var source = shape switch
        {
            "AbstractView" => AbstractView,
            "NonPartialView" => NonPartialView,
            "ReadOnlyView" => ReadOnlyView,
            _ => NoFacetView,
        };

        var result = WriteMapperGeneratorTestHarness.Run(source);

        // R1.2: none of these shapes is a write-mapper candidate, so no write-mapper source is produced.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaWriteMapper")).IsFalse();

        // Non-candidates are dropped silently — the write-mapper generator raises no VISTA diagnostic
        // (VISTA0001 non-partial is owned by the Phase 1 accessor generator, not this one).
        await Assert.That(result.Diagnostics.Any(static d => d.Id.StartsWith("VISTA", StringComparison.Ordinal))).IsFalse();
    }

    // ---- TCrud = object: recognized candidate but skipped silently (R1.4) --------------------------

    [Test]
    public async Task Object_TCrud_View_Emits_No_Mapper_And_No_Diagnostic()
    {
        var result = WriteMapperGeneratorTestHarness.Run(ObjectCrudView);

        // R1.4: a view whose TCrud is `object` has no named write contract — no mapper is emitted.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaWriteMapper")).IsFalse();

        // The skip is silent: no VISTA diagnostic (not even the VISTA0033 fallback warning), because a
        // view with no named TCrud is not this generator's concern.
        await Assert.That(result.Diagnostics.Any(static d => d.Id.StartsWith("VISTA", StringComparison.Ordinal))).IsFalse();
    }

    // ---- non-simple selector: candidate, unanalyzable, VISTA0033 fallback (R1.5, R8) ---------------

    [Test]
    public async Task Non_Simple_Selector_View_Falls_Back_To_Reflection_With_Vista0033_Warning()
    {
        var result = WriteMapperGeneratorTestHarness.Run(NonSimpleSelectorView);

        // R1.5 / R8.1: a non-simple selector makes the chain unanalyzable, so no mapper is emitted.
        await Assert.That(result.HasGeneratedSourceContaining("_VistaWriteMapper")).IsFalse();

        // R8.2 / R8.3: exactly one VISTA0033 warning, naming the offending view, and no error severity
        // (the build stays green and the view falls back to reflection).
        var vista0033 = result.Diagnostics.Where(static d => d.Id == "VISTA0033").ToArray();
        await Assert.That(vista0033.Length).IsEqualTo(1);
        await Assert.That(vista0033[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(vista0033[0].GetMessage().Contains("NonSimpleSelectorView", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }
}
