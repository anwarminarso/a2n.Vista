// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Snapshot/diagnostic tests for the Phase 2 (M10, D118) plan diagnostics emitted by
// ViewAccessorGenerator (task 3.3; requirements R1.6, R9.1, R9.2, R9.3, R9.4). The generator is driven
// directly via CSharpGeneratorDriver over in-memory source (see GeneratorTestHarness).
//
// The generator references no Vista assembly and recognizes the authoring fluent surface by
// fully-qualified name (Spec 03 D71): the base View<TQuery> / View<TQuery, TCrud> types and the
// ViewAccessorRegistry come from GeneratorTestHarness.VistaStubs, and these tests supplement that with
// minimal a2n.Vista.Authoring.IViewBuilder<TQuery> / IFieldBuilder<TProp> stubs (BuilderStubs) so the
// From<TSource>(...) / Key(...) / Field(...) / PrimaryKey() calls inside the views resolve to symbols
// whose containing type the generator matches by FQN. Only the generated source TEXT and the run
// diagnostics are inspected; no generated [ModuleInitializer] is executed.
//
// Covered scenarios:
//   * VISTA0003 (warning) skip-and-continue: a view with an unanalyzable projection is reported and
//     left metadata-only WITHOUT a compilation error, and a sibling view with a reproducible projection
//     in the SAME compilation is still generated (R1.6, R9.1, R9.2).
//   * VISTA0020 (error): a statically-provable keyless executable view — analyzable projection, no
//     declared key, more than one source entity — is reported as an error (R9.3).
//   * Descriptor contract (R9.4): every emitted VISTA#### diagnostic carries the VISTA#### id, the
//     a2n.Vista.SourceGenerators category, a defined severity, and a non-empty help link.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class GeneratorDiagnosticsTests
{
    // Minimal stubs of the authoring fluent surface the generator recognizes by FQN. These supplement
    // GeneratorTestHarness.VistaStubs (which declares View<TQuery> and ViewAccessorRegistry). Types are
    // fully qualified to avoid any using-directive ordering concerns when concatenated with a view body.
    private const string BuilderStubs = @"
namespace a2n.Vista.Authoring
{
    public interface IFieldBuilder<TProp>
    {
        IFieldBuilder<TProp> PrimaryKey();
        IFieldBuilder<TProp> Filterable(bool allowed = true);
        IFieldBuilder<TProp> Sortable(bool allowed = true);
    }

    public interface IViewBuilder<TQuery> where TQuery : class
    {
        IViewBuilder<TQuery> From<TSource>(
            System.Linq.Expressions.Expression<System.Func<TSource, TQuery>> projection)
            where TSource : class;

        IViewBuilder<TQuery> Field<TProp>(
            System.Linq.Expressions.Expression<System.Func<TQuery, TProp>> field,
            System.Action<IFieldBuilder<TProp>> configure);

        IViewBuilder<TQuery> Key(params System.Linq.Expressions.Expression<System.Func<TQuery, object?>>[] fields);

        IViewBuilder<TQuery> MaskField<TProp>(
            System.Linq.Expressions.Expression<System.Func<TQuery, TProp>> field,
            System.Func<System.IServiceProvider, bool> shouldMask,
            System.Func<TProp, TProp> masker);
    }
}
";

    // A compilation with TWO views: BadView's From<TSource>(...) projection is a method call (not a
    // reproducible object-initializer / named-constructor shape), so the generator cannot reproduce it
    // statically → VISTA0003 (warning), view stays metadata-only, no compile error. GoodView's
    // projection is a clean member-init over a single source, so the generator continues past the bad
    // view and still generates the sibling's accessor source (skip-and-continue; R1.6, R9.1, R9.2).
    private const string Vista0003SkipAndContinueViews = BuilderStubs + @"
namespace App
{
    public sealed class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class BadRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class GoodRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // Unanalyzable: the projection is a method call, not a reproducible object-initializer shape.
    public partial class BadView : a2n.Vista.Authoring.View<BadRow>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<BadRow> builder)
            => builder.From<Customer>(src => Project(src));

        private static BadRow Project(Customer c) => new BadRow { Id = c.Id, Name = c.Name };
    }

    // Reproducible single-source member-init projection — the sibling that must still be generated.
    public partial class GoodView : a2n.Vista.Authoring.View<GoodRow>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<GoodRow> builder)
            => builder.From<Customer>(src => new GoodRow { Id = src.Id, Name = src.Name });
    }
}
";

    // A statically-provable keyless executable view: an analyzable (member-init) projection, no declared
    // key (no .Key(...) and no projected field's .PrimaryKey()), and MORE THAN ONE source entity (two
    // distinct From<TSource> calls). Single-source PK auto-derivation (D105) does not apply to
    // multi-source views, so the generator can prove keylessness at compile time → VISTA0020 (error).
    private const string Vista0020KeylessView = BuilderStubs + @"
namespace App
{
    public sealed class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class Order
    {
        public int OrderId { get; set; }
        public string Code { get; set; } = string.Empty;
    }

    public sealed class JoinRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public partial class JoinView : a2n.Vista.Authoring.View<JoinRow>
    {
        public void Configure(a2n.Vista.Authoring.IViewBuilder<JoinRow> builder)
        {
            builder.From<Customer>(src => new JoinRow { Id = src.Id, Name = src.Name });
            builder.From<Order>(src => new JoinRow { Id = src.OrderId, Name = src.Code });
        }
    }
}
";

    // ---- VISTA0003 skip-and-continue ---------------------------------------------------------------

    [Test]
    public async Task Vista0003_Reports_Warning_For_Unanalyzable_View_And_Still_Generates_Sibling()
    {
        var result = GeneratorTestHarness.Run(Vista0003SkipAndContinueViews);

        var vista0003 = result.Diagnostics.Where(static d => d.Id == "VISTA0003").ToArray();

        // Exactly one VISTA0003, warning severity, naming the offending view.
        await Assert.That(vista0003.Length).IsEqualTo(1);
        await Assert.That(vista0003[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(vista0003[0].GetMessage().Contains("BadView", StringComparison.Ordinal)).IsTrue();

        // R9.2: the unanalyzable view stays metadata-only with NO compilation error raised.
        await Assert.That(result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error)).IsFalse();

        // Skip-and-continue: the sibling view with a reproducible projection is still generated.
        await Assert.That(result.HasGeneratedSourceContaining("GoodView")).IsTrue();
    }

    // ---- VISTA0020 provable keyless view -----------------------------------------------------------

    [Test]
    public async Task Vista0020_Reports_Error_For_Provably_Keyless_Multi_Source_View()
    {
        var result = GeneratorTestHarness.Run(Vista0020KeylessView);

        var vista0020 = result.Diagnostics.Where(static d => d.Id == "VISTA0020").ToArray();

        await Assert.That(vista0020.Length).IsEqualTo(1);
        await Assert.That(vista0020[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(vista0020[0].GetMessage().Contains("JoinView", StringComparison.Ordinal)).IsTrue();

        // The multi-source keyless view is not silently downgraded to a VISTA0003 warning.
        await Assert.That(result.Diagnostics.Any(static d => d.Id == "VISTA0003")).IsFalse();
    }

    // ---- Descriptor contract (R9.4) ----------------------------------------------------------------

    [Test]
    public async Task Emitted_Diagnostics_Honor_The_Descriptor_Contract()
    {
        // Gather every Vista diagnostic emitted across both Phase 2 plan-diagnostic scenarios.
        var diagnostics = GeneratorTestHarness.Run(Vista0003SkipAndContinueViews).Diagnostics
            .Concat(GeneratorTestHarness.Run(Vista0020KeylessView).Diagnostics)
            .Where(static d => d.Id.StartsWith("VISTA", StringComparison.Ordinal))
            .ToArray();

        // Both VISTA0003 and VISTA0020 must be present so the contract assertions cover them.
        await Assert.That(diagnostics.Any(static d => d.Id == "VISTA0003")).IsTrue();
        await Assert.That(diagnostics.Any(static d => d.Id == "VISTA0020")).IsTrue();

        foreach (var diagnostic in diagnostics)
        {
            // Id: VISTA#### prefix with exactly four digits (D81).
            await Assert.That(Regex.IsMatch(diagnostic.Id, "^VISTA[0-9]{4}$")).IsTrue();

            // Category: a2n.Vista.SourceGenerators.
            await Assert.That(diagnostic.Descriptor.Category).IsEqualTo("a2n.Vista.SourceGenerators");

            // Severity: a defined DiagnosticSeverity value.
            await Assert.That(Enum.IsDefined(typeof(DiagnosticSeverity), diagnostic.Severity)).IsTrue();

            // Help link: present and non-empty.
            await Assert.That(string.IsNullOrWhiteSpace(diagnostic.Descriptor.HelpLinkUri)).IsFalse();
        }
    }
}
