// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Self-contained Style B fixture for the Phase 2 AOT verification probe (spec style-b-executable,
// Task 11.1, R5.1/R5.4/R5.5/R1.7; Decision Log D118).
//
// This is a REAL, minimal, VALID, partial, single-source typed Style B view plus its EF source entity,
// projected row, and DbContext. Because the probe assembly references the EF layer AND the source
// generator (as an analyzer), the Phase 2 emitter produces — INTO this assembly — a `file sealed`
// CompiledViewExecutionPlan_<View> implementing ICompiledViewExecutionPlan, plus a [ModuleInitializer]
// that registers the plan into GeneratedExecutionPlanStore at module load. Program.cs then drives that
// generated plan through EfViewExecutor's non-RUC compiled List/Detail path under the trim/AOT analyzer.
//
// The view deliberately covers the AOT-critical generated surface a List exercises:
//   * a member-initialization projection over a single EF source entity (CreateScopedQueryable);
//   * a declared primary key (Detail-by-key + the deterministic D106 sort tiebreaker);
//   * filterable/sortable fields (the generated member-access map + strongly-typed sort appliers).

using a2n.Vista.Authoring;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.AotProbe;

/// <summary>EF source entity with a conventional single primary key (<see cref="Id"/>).</summary>
public sealed class ProbeWidget
{
    /// <summary>Primary key (EF infers it by convention).</summary>
    public int Id { get; set; }

    /// <summary>A name string — filterable / sortable / searchable by default.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A region string — used to exercise a client equality filter through the compiled path.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>A numeric quantity — filterable / sortable.</summary>
    public int Quantity { get; set; }
}

/// <summary>
/// Projected (read) row sent to clients. Init-only auto-properties projected via member-initialization
/// so EF Core maps each assigned member back to its source column and filter/sort push down to SQL.
/// </summary>
public sealed class ProbeWidgetRow
{
    /// <summary>Primary key.</summary>
    public int Id { get; init; }

    /// <summary>Name (filterable / sortable / searchable).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Region (filterable / sortable).</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Quantity (filterable / sortable).</summary>
    public int Quantity { get; init; }
}

/// <summary>
/// Typed Style B view over <see cref="ProbeWidget"/>. It is <c>partial</c>, single-source, has a public
/// parameterless constructor, declares a primary key, and uses a member-initialization projection — all
/// the conditions the Phase 2 emitter needs to generate a compiled execution plan
/// (<c>CompiledViewExecutionPlan_ProbeWidgetView</c>) and register it at module load.
/// </summary>
public partial class ProbeWidgetView : View<ProbeWidgetRow>
{
    /// <summary>The globally-unique view name; also the key the generated plan is stored under.</summary>
    public const string ViewName = "aotprobe-widgets";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<ProbeWidgetRow> builder) =>
        builder
            .Named(ViewName)
            .From<ProbeWidget>(s => new ProbeWidgetRow
            {
                Id = s.Id,
                Name = s.Name,
                Region = s.Region,
                Quantity = s.Quantity,
            })
            .Field(x => x.Id, f => f.PrimaryKey());
}

/// <summary>
/// EF context exposing <see cref="ProbeWidget"/> over SQLite so the generated compiled plan can root its
/// queryable on <c>DbContext.Set&lt;ProbeWidget&gt;()</c>.
/// </summary>
public sealed class ProbeDbContext : DbContext
{
    /// <summary>Initializes the context with the supplied options (SQLite in-memory in the probe).</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "The base DbContext constructor is EF Core framework infrastructure (not AOT-compatible), not the generated Style B read path under verification (R5.1/R5.4).")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "The base DbContext constructor is EF Core framework infrastructure (not AOT-compatible), not the generated Style B read path under verification (R5.1/R5.4).")]
    public ProbeDbContext(DbContextOptions<ProbeDbContext> options)
        : base(options)
    {
    }

    /// <summary>The source widgets the probe view projects from.</summary>
    public DbSet<ProbeWidget> Widgets => Set<ProbeWidget>();
}
