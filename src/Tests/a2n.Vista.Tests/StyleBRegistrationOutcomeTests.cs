// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Registration-outcome tests for the source-generator Phase 2 wiring in
/// <c>VistaBuilder.Register&lt;TView&gt;()</c> (Decision Log D118; Requirements R4.1, R4.3). The builder
/// consults <see cref="GeneratedExecutionPlanStore"/> by the view's runtime <c>Name</c> and:
/// <list type="bullet">
/// <item>plan present → adds a <see cref="CompiledExecutionPlanAdapter"/> to the
/// <see cref="IViewExecutionPlanRegistry"/> (the view is <b>resolvable = executable</b>) AND publishes
/// its <see cref="ViewMetadata"/> (the view is <b>discoverable</b>) (R4.1);</item>
/// <item>plan absent → publishes <see cref="ViewMetadata"/> only and adds no plan, preserving today's
/// DR5 metadata-only behavior (R4.3);</item>
/// <item>a duplicate view name still fails fast through the registry's duplicate-name guard.</item>
/// </list>
/// </summary>
/// <remarks>
/// <see cref="GeneratedExecutionPlanStore"/> is a process-wide static with first-wins idempotency, so
/// each test uses its own dedicated Style B view with a distinct, stable name. Distinct names keep a
/// seeded plan from one test from leaking into another (an absent-plan test must not observe a plan that
/// a present-plan test seeded under a shared name).
/// </remarks>
public sealed class StyleBRegistrationOutcomeTests
{
    private const string Il2026 =
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming";
    private const string Why =
        "Test exercises the runtime reflection authoring path (Register<TView>) by design; trimming is not used for tests.";

    /// <summary>
    /// R4.1: when the generator has published a compiled plan for the view (here seeded directly into
    /// <see cref="GeneratedExecutionPlanStore"/> before registration), registering the view makes it both
    /// discoverable (metadata) and resolvable/executable (a plan is present in the plan registry).
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Plan_Present_View_Is_Discoverable_And_Resolvable()
    {
        var viewName = new PlanPresentView().Name;

        // Seed the store as a generated [ModuleInitializer] would, BEFORE registration runs.
        GeneratedExecutionPlanStore.Add(viewName, new FakeCompiledPlan(viewName, typeof(RegRow)));

        var services = new ServiceCollection();
        services.AddVista(v => v.Register<PlanPresentView>());
        using var provider = services.BuildServiceProvider();

        var metadata = provider.GetRequiredService<IViewRegistry>().Get(viewName);
        var plan = provider.GetRequiredService<IViewExecutionPlanRegistry>().Get(viewName);

        // Discoverable: metadata published.
        await Assert.That(metadata).IsNotNull();
        // Resolvable/executable: a plan is registered, and it is the compiled adapter facet.
        await Assert.That(plan).IsNotNull();
        await Assert.That(plan is ICompiledViewExecutionPlan).IsTrue();
        await Assert.That(plan!.ViewName).IsEqualTo(viewName);
    }

    /// <summary>
    /// R4.3: when no generated plan exists for the view, registration still publishes its metadata
    /// (discoverable) but adds no entry to the plan registry — DR5 metadata-only behavior preserved.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Plan_Absent_View_Is_Discoverable_But_Not_Resolvable()
    {
        var viewName = new PlanAbsentView().Name;

        // Guard against accidental cross-test pollution: this view's name must have no seeded plan.
        await Assert.That(GeneratedExecutionPlanStore.TryGet(viewName, out _)).IsFalse();

        var services = new ServiceCollection();
        services.AddVista(v => v.Register<PlanAbsentView>());
        using var provider = services.BuildServiceProvider();

        var metadata = provider.GetRequiredService<IViewRegistry>().Get(viewName);
        var plan = provider.GetRequiredService<IViewExecutionPlanRegistry>().Get(viewName);

        // Discoverable: metadata published.
        await Assert.That(metadata).IsNotNull();
        // Not resolvable: metadata-only (DR5), no plan registered.
        await Assert.That(plan).IsNull();
    }

    /// <summary>
    /// Registering the same view name twice still fails fast through the registry's duplicate-name guard,
    /// regardless of whether a generated plan is present (one view = one endpoint).
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Duplicate_View_Name_Fails_Fast()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddVista(v =>
            {
                v.Register<DuplicateRegView>();
                v.Register<DuplicateRegView>();
            }))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// A minimal <see cref="ICompiledViewExecutionPlan"/> test double. Only the contract values the
    /// registration path reads (<see cref="ViewName"/>, <see cref="RowType"/>) are meaningful; the
    /// execution members throw because registration never invokes them.
    /// </summary>
    private sealed class FakeCompiledPlan : ICompiledViewExecutionPlan
    {
        public FakeCompiledPlan(string viewName, Type rowType)
        {
            ViewName = viewName;
            RowType = rowType;
        }

        public string ViewName { get; }

        public Type RowType { get; }

        public Type SourceType => typeof(RegSource);

        public bool IsSingleSource => true;

        public IReadOnlyList<MaskAccessor> MaskAccessors => Array.Empty<MaskAccessor>();

        public IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope) =>
            throw new NotSupportedException("Execution is out of scope for registration-outcome tests.");

        public bool TryGetMemberAccess(string fieldName, out LambdaExpression accessor) =>
            throw new NotSupportedException("Execution is out of scope for registration-outcome tests.");

        public IOrderedQueryable ApplyPrimarySort(IQueryable source, string fieldName, bool descending) =>
            throw new NotSupportedException("Execution is out of scope for registration-outcome tests.");

        public IOrderedQueryable ApplyThenSort(IOrderedQueryable source, string fieldName, bool descending) =>
            throw new NotSupportedException("Execution is out of scope for registration-outcome tests.");
    }
}

/// <summary>EF source entity for the registration-outcome test views (POCO; never materialized here).</summary>
internal sealed class RegSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Read projection for the registration-outcome test views.</summary>
internal sealed class RegRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Style B view whose generated plan is seeded before registration (plan-present case).</summary>
internal sealed class PlanPresentView : View<RegRow>
{
    protected override void Configure(IViewBuilder<RegRow> b) =>
        b.Named("stylebexec-plan-present")
         .From<RegSource>(s => new RegRow { Id = s.Id, Name = s.Name })
         .Field(x => x.Id, f => f.PrimaryKey());
}

/// <summary>Style B view with no generated plan (plan-absent / metadata-only case).</summary>
internal sealed class PlanAbsentView : View<RegRow>
{
    protected override void Configure(IViewBuilder<RegRow> b) =>
        b.Named("stylebexec-plan-absent")
         .From<RegSource>(s => new RegRow { Id = s.Id, Name = s.Name })
         .Field(x => x.Id, f => f.PrimaryKey());
}

/// <summary>Style B view used to assert the duplicate-name fail-fast guard.</summary>
internal sealed class DuplicateRegView : View<RegRow>
{
    protected override void Configure(IViewBuilder<RegRow> b) =>
        b.Named("stylebexec-duplicate")
         .From<RegSource>(s => new RegRow { Id = s.Id, Name = s.Name })
         .Field(x => x.Id, f => f.PrimaryKey());
}
