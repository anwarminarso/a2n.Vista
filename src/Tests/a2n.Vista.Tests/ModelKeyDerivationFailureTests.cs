// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.EntityFrameworkCore;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.EntityFrameworkCore.Hosting;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Failure and skip scenarios for the D105 / M11 single-source primary-key auto-derivation startup hook
/// (<see cref="VistaModelKeyDerivationService"/>; Requirements R6.4, R6.5, R6.6). These complement the
/// happy-path derivation property test (task 10.2) by pinning the fail-closed and skip behavior:
/// <list type="bullet">
/// <item><b>R6.4</b> — a single-source view whose source entity has no primary key in the EF model
/// aborts startup, naming both the view and the source entity.</item>
/// <item><b>R6.6</b> — a non-single-source view that declared no key aborts startup, naming the view
/// (model derivation is impossible for a multi-source projection).</item>
/// <item><b>R6.5</b> — a non-single-source view is skipped by the hook (no model derivation is
/// attempted), while a sibling single-source key-less view in the same registry is still derived,
/// proving the hook ran and selectively skipped the multi-source one.</item>
/// </list>
/// The service is exercised directly over an in-memory SQLite-backed <see cref="DbContext"/> (mirroring
/// <c>DialectStartupGuardTests</c>) with hand-built registries, so each scenario controls exactly which
/// views reach the hook key-less and what their compiled plan reports.
/// </summary>
public sealed class ModelKeyDerivationFailureTests
{
    /// <summary>
    /// R6.4: a single-source executable view whose source entity has no primary key in
    /// <c>DbContext.Model</c> fails closed at startup, with a message that names both the view and the
    /// source entity.
    /// </summary>
    [Test]
    public async Task SingleSource_With_No_Model_PrimaryKey_Aborts_Startup_Naming_View_And_Entity()
    {
        const string viewName = "kd-no-pk-view";

        var viewRegistry = new ViewRegistry();
        viewRegistry.Add(BuildView(viewName, keyFields: null));

        var planRegistry = new ViewExecutionPlanRegistry();
        planRegistry.Add(new FakeCompiledPlan(
            viewName,
            rowType: typeof(KdRow),
            sourceType: typeof(KdKeylessSource),
            isSingleSource: true));

        using var provider = BuildProvider();
        var service = CreateService(provider, viewRegistry, planRegistry);

        var caught = await CaptureStartAsync(service);

        await Assert.That(caught).IsNotNull();
        // Names the view ...
        await Assert.That(caught!.Message).Contains(viewName);
        // ... and the source entity that lacks a model primary key.
        await Assert.That(caught.Message).Contains(typeof(KdKeylessSource).FullName!);
    }

    /// <summary>
    /// R6.6: a non-single-source view that declared no key cannot be model-derived and fails closed at
    /// startup, with a message that names the view. The failure happens before any DbContext work.
    /// </summary>
    [Test]
    public async Task NonSingleSource_Without_Declared_Key_Aborts_Startup_Naming_View()
    {
        const string viewName = "kd-multi-no-key-view";

        var viewRegistry = new ViewRegistry();
        viewRegistry.Add(BuildView(viewName, keyFields: null));

        var planRegistry = new ViewExecutionPlanRegistry();
        planRegistry.Add(new FakeCompiledPlan(
            viewName,
            rowType: typeof(KdRow),
            sourceType: typeof(void),
            isSingleSource: false));

        using var provider = BuildProvider();
        var service = CreateService(provider, viewRegistry, planRegistry);

        var caught = await CaptureStartAsync(service);

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains(viewName);
    }

    /// <summary>
    /// R6.5: the hook does not attempt model derivation for a non-single-source view. A multi-source view
    /// that already declares a key is left untouched (key not overridden, no derivation attempted, no
    /// throw), while a sibling single-source key-less view in the same registry is still derived from the
    /// model — proving the hook ran and selectively skipped the multi-source one.
    /// </summary>
    [Test]
    public async Task NonSingleSource_With_Declared_Key_Is_Skipped_While_SingleSource_Is_Derived()
    {
        const string multiViewName = "kd-multi-keyed-view";
        const string singleViewName = "kd-single-derive-view";

        var viewRegistry = new ViewRegistry();
        var multiView = BuildView(multiViewName, keyFields: new[] { "DeclaredKey" });
        var singleView = BuildView(singleViewName, keyFields: null);
        viewRegistry.Add(multiView);
        viewRegistry.Add(singleView);

        var planRegistry = new ViewExecutionPlanRegistry();
        planRegistry.Add(new FakeCompiledPlan(
            multiViewName,
            rowType: typeof(KdRow),
            sourceType: typeof(void),
            isSingleSource: false));
        planRegistry.Add(new FakeCompiledPlan(
            singleViewName,
            rowType: typeof(KdRow),
            sourceType: typeof(KdSource),
            isSingleSource: true));

        using var provider = BuildProvider();
        var service = CreateService(provider, viewRegistry, planRegistry);

        var caught = await CaptureStartAsync(service);

        // The hook must not throw: the multi-source view is skipped (it already has a key), not failed.
        await Assert.That(caught).IsNull();
        // Skipped: the multi-source view's declared key is untouched (R6.5 — no derivation attempted).
        await Assert.That(multiView.KeyFields.SequenceEqual(new[] { "DeclaredKey" })).IsTrue();
        // Proof the hook actually ran: the single-source key-less view was derived from the model PK.
        await Assert.That(singleView.KeyFields.SequenceEqual(new[] { nameof(KdSource.Id) })).IsTrue();
    }

    /// <summary>
    /// Builds minimal <see cref="ViewMetadata"/> for the hook. Only <see cref="ViewMetadata.Name"/> and
    /// <see cref="ViewMetadata.KeyFields"/> are read by the service; everything else is filler. A
    /// <see langword="null"/> <paramref name="keyFields"/> leaves the key empty so the view reaches the
    /// hook key-less.
    /// </summary>
    private static ViewMetadata BuildView(string name, IReadOnlyList<string>? keyFields)
    {
        var metadata = new ViewMetadata(
            Name: name,
            Route: $"/test/{name}",
            QueryType: typeof(KdRow),
            CrudType: null,
            CrudEntityType: null,
            Fields: Array.Empty<FieldMetadata>(),
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: true);

        return keyFields is null
            ? metadata
            : metadata with { KeyFields = keyFields };
    }

    /// <summary>
    /// Builds a provider with an in-memory SQLite-backed <see cref="KdContext"/> registered and captured,
    /// so the hook can resolve it and read the model. The database is never created or queried — the hook
    /// only reads <c>DbContext.Model</c>.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<KdContext>(o => o.UseSqlite("DataSource=:memory:"));
        return services.BuildServiceProvider();
    }

    private static VistaModelKeyDerivationService CreateService(
        IServiceProvider provider,
        IViewRegistry viewRegistry,
        IViewExecutionPlanRegistry planRegistry)
    {
        var accessor = new VistaDbContextAccessor();
        accessor.Capture(typeof(KdContext));
        return new VistaModelKeyDerivationService(provider, viewRegistry, planRegistry, accessor);
    }

    /// <summary>Runs the hook and returns the thrown exception, or <see langword="null"/> on success.</summary>
    private static async Task<InvalidOperationException?> CaptureStartAsync(VistaModelKeyDerivationService service)
    {
        try
        {
            await service.StartAsync(CancellationToken.None);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// A minimal plan implementing <b>both</b> <see cref="IViewExecutionPlan"/> (so it can be stored in
    /// the <see cref="ViewExecutionPlanRegistry"/>) and <see cref="ICompiledViewExecutionPlan"/> (so the
    /// hook's <c>is</c>-test succeeds and it can read <see cref="SourceType"/>/<see cref="IsSingleSource"/>).
    /// Only those facet values are meaningful; the execution members throw because the startup hook never
    /// invokes them.
    /// </summary>
    private sealed class FakeCompiledPlan : IViewExecutionPlan, ICompiledViewExecutionPlan
    {
        public FakeCompiledPlan(string viewName, Type rowType, Type sourceType, bool isSingleSource)
        {
            ViewName = viewName;
            RowType = rowType;
            SourceType = sourceType;
            IsSingleSource = isSingleSource;
        }

        public string ViewName { get; }

        public Type RowType { get; }

        public Type SourceType { get; }

        public bool IsSingleSource { get; }

        public IReadOnlyList<MaskAccessor> MaskAccessors => Array.Empty<MaskAccessor>();

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Trimming", "IL2046:RequiresUnreferencedCode mismatch on override/interface",
            Justification = "Test double; the startup hook never invokes CreateScopedQueryable.")]
        public IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope) =>
            throw new NotSupportedException("Execution is out of scope for key-derivation tests.");

        public bool TryGetMemberAccess(string fieldName, out LambdaExpression accessor) =>
            throw new NotSupportedException("Execution is out of scope for key-derivation tests.");

        public IOrderedQueryable ApplyPrimarySort(IQueryable source, string fieldName, bool descending) =>
            throw new NotSupportedException("Execution is out of scope for key-derivation tests.");

        public IOrderedQueryable ApplyThenSort(IOrderedQueryable source, string fieldName, bool descending) =>
            throw new NotSupportedException("Execution is out of scope for key-derivation tests.");
    }
}

/// <summary>Read-row filler type for the key-derivation test views (never materialized).</summary>
internal sealed class KdRow
{
    public int Id { get; init; }
}

/// <summary>EF source entity with a conventional <c>Id</c> primary key (single-source derive case).</summary>
internal sealed class KdSource
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>EF source entity configured with no key, so <c>FindPrimaryKey()</c> returns null (R6.4).</summary>
internal sealed class KdKeylessSource
{
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// In-memory SQLite-backed context exposing a keyed and a keyless source entity. The hook reads only
/// <c>Model</c>, so the database is never created; the keyless entity is configured via
/// <c>HasNoKey()</c>.
/// </summary>
internal sealed class KdContext : DbContext
{
    public KdContext(DbContextOptions<KdContext> options)
        : base(options)
    {
    }

    public DbSet<KdSource> Sources => Set<KdSource>();

    public DbSet<KdKeylessSource> Keyless => Set<KdKeylessSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KdKeylessSource>().HasNoKey();
    }
}
