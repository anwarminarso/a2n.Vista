// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Golden snapshot tests for the Phase 2 (M10, D118) compiled execution-plan emitter in
// ViewAccessorGenerator (task 4.2; requirements R10.1, R1.2, R5.2). The generator is driven directly
// via CSharpGeneratorDriver over in-memory source (see GeneratorTestHarness.RunWithExecutionPlanSupport,
// which makes a2n.Vista.EntityFrameworkCore.Execution.ICompiledViewExecutionPlan present in the
// compilation so the <View>_VistaExecutionPlan.g.cs plan is emitted).
//
// Representative shapes (task 4.2):
//   * single-key, single-source view (CustomerView) — full golden of the emitted plan;
//   * composite-key, single-source view (OrderLineView) — full golden;
//   * masked-field view (PersonView) — full golden, including the emitted MaskAccessor.
//
// The goldens live as embedded text resources under Goldens\ (see the .csproj) rather than inline
// escaped string literals, because the generated plan is ~125 lines. Generated text uses fixed "\n"
// line endings and the harness normalizes to "\n"; the goldens are stored LF and re-normalized on load
// so the comparison is byte-stable across platforms.
//
// Cross-cutting AOT assertions on every emitted plan (R1.2, R5.2): the generated CreateScopedQueryable
// carries no [RequiresUnreferencedCode], and the whole plan contains none of the runtime-expression /
// reflection markers the compiled seam exists to avoid — Activator.CreateInstance, PropertyInfo,
// Expression.Property(string), .Compile(), or MethodInfo.MakeGenericMethod.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ViewExecutionPlanGeneratorTests
{
    // ---- view sources -----------------------------------------------------------------------------

    // Single-key, single-source view: Customer -> CustomerRow with Id (declared PK) + Name.
    private const string SingleKeyViewSource = @"
namespace App
{
    public sealed class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class CustomerRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public partial class CustomerView : a2n.Vista.Authoring.View<CustomerRow>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<CustomerRow> builder)
            => builder
                .From<Customer>(src => new CustomerRow { Id = src.Id, Name = src.Name })
                .Field(x => x.Id, f => f.PrimaryKey());
    }
}
";

    // Composite-key, single-source view: OrderLine -> OrderLineRow with (OrderId, LineNo) declared key.
    private const string CompositeKeyViewSource = @"
namespace App
{
    public sealed class OrderLine
    {
        public int OrderId { get; set; }
        public int LineNo { get; set; }
        public string Sku { get; set; } = string.Empty;
    }

    public sealed class OrderLineRow
    {
        public int OrderId { get; set; }
        public int LineNo { get; set; }
        public string Sku { get; set; } = string.Empty;
    }

    public partial class OrderLineView : a2n.Vista.Authoring.View<OrderLineRow>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<OrderLineRow> builder)
            => builder
                .From<OrderLine>(src => new OrderLineRow { OrderId = src.OrderId, LineNo = src.LineNo, Sku = src.Sku })
                .Key(x => x.OrderId, x => x.LineNo);
    }
}
";

    // Masked-field view: Person -> PersonRow with Email masked (settable property -> direct setter).
    private const string MaskedFieldViewSource = @"
namespace App
{
    public sealed class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public sealed class PersonRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public partial class PersonView : a2n.Vista.Authoring.View<PersonRow>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<PersonRow> builder)
            => builder
                .From<Person>(src => new PersonRow { Id = src.Id, Name = src.Name, Email = src.Email })
                .Field(x => x.Id, f => f.PrimaryKey())
                .MaskField(x => x.Email, services => true, value => ""***"");
    }
}
";

    // ---- golden snapshots: full generated plan text -----------------------------------------------

    [Test]
    public async Task SingleKeyView_Emits_Golden_Execution_Plan()
    {
        var result = GeneratorTestHarness.RunWithExecutionPlanSupport(SingleKeyViewSource);

        // No generator errors for a well-formed analyzable single-source view.
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();

        var generated = result.GeneratedSourceContaining("CustomerView_VistaExecutionPlan");
        await Assert.That(generated).IsEqualTo(LoadGolden("CustomerView_VistaExecutionPlan"));
    }

    [Test]
    public async Task CompositeKeyView_Emits_Golden_Execution_Plan()
    {
        var result = GeneratorTestHarness.RunWithExecutionPlanSupport(CompositeKeyViewSource);

        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();

        var generated = result.GeneratedSourceContaining("OrderLineView_VistaExecutionPlan");
        await Assert.That(generated).IsEqualTo(LoadGolden("OrderLineView_VistaExecutionPlan"));
    }

    [Test]
    public async Task MaskedFieldView_Emits_Golden_Execution_Plan_With_Mask_Accessor()
    {
        var result = GeneratorTestHarness.RunWithExecutionPlanSupport(MaskedFieldViewSource);

        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();

        var generated = result.GeneratedSourceContaining("PersonView_VistaExecutionPlan");
        await Assert.That(generated).IsEqualTo(LoadGolden("PersonView_VistaExecutionPlan"));

        // The masked field gets a generated MaskAccessor (cast getter + AOT-clean setter), so masking is
        // applied at materialization without reflection (R7.1). The array is non-empty for this view.
        await Assert.That(generated.Contains(
            "private static readonly global::a2n.Vista.Metadata.MaskAccessor[] MaskAccessorArray = new global::a2n.Vista.Metadata.MaskAccessor[]",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "new global::a2n.Vista.Metadata.MaskAccessor(\n            \"Email\",",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "static row => ((global::App.PersonRow)row).Email,",
            StringComparison.Ordinal)).IsTrue();
    }

    // ---- AOT cleanliness across every emitted plan (R1.2, R5.2) -----------------------------------

    [Test]
    [Arguments("CustomerView_VistaExecutionPlan")]
    [Arguments("OrderLineView_VistaExecutionPlan")]
    [Arguments("PersonView_VistaExecutionPlan")]
    public async Task Emitted_Plan_Is_Aot_Clean(string planClassName)
    {
        var source = planClassName switch
        {
            "CustomerView_VistaExecutionPlan" => SingleKeyViewSource,
            "OrderLineView_VistaExecutionPlan" => CompositeKeyViewSource,
            _ => MaskedFieldViewSource,
        };

        var generated = GeneratorTestHarness.RunWithExecutionPlanSupport(source)
            .GeneratedSourceContaining(planClassName);

        // R5.2: the generated CreateScopedQueryable (and indeed nothing in the plan) is annotated
        // [RequiresUnreferencedCode] — the compiled seam is the non-RUC path.
        await Assert.That(generated.Contains("RequiresUnreferencedCode", StringComparison.Ordinal)).IsFalse();

        // R1.2 / R5.2: none of the runtime-expression / reflection markers the compiled seam avoids.
        await Assert.That(generated.Contains("Activator.CreateInstance", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generated.Contains("PropertyInfo", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generated.Contains("Expression.Property(", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generated.Contains(".Compile(", StringComparison.Ordinal)).IsFalse();
        await Assert.That(generated.Contains("MakeGenericMethod", StringComparison.Ordinal)).IsFalse();

        // Positive guards: the plan is the AOT-clean compiled seam — it implements the non-RUC interface
        // and reproduces the projection as a node-built Expression<> the consumer compiles (R1.2).
        await Assert.That(generated.Contains(
            ": global::a2n.Vista.EntityFrameworkCore.Execution.ICompiledViewExecutionPlan",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains(
            "global::System.Linq.Expressions.Expression<global::System.Func<",
            StringComparison.Ordinal)).IsTrue();
    }

    // ---- embedded-golden loader -------------------------------------------------------------------

    /// <summary>
    /// Loads the embedded golden text for <paramref name="planClassName"/> (logical name
    /// <c>Goldens.&lt;planClassName&gt;.verified.txt</c>, see the .csproj) and normalizes line endings to
    /// <c>\n</c> to match the generator's fixed output.
    /// </summary>
    private static string LoadGolden(string planClassName)
    {
        var resourceName = "Goldens." + planClassName + ".verified.txt";
        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded golden '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
