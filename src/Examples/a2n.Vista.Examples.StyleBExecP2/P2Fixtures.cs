// Licensed to the a2n.Vista project. Published artifact — English only.
//
// EF-aware consumer fixtures for the source-generator Phase 2 compiled execution plan
// (spec style-b-executable, Property 2 — List page bound and unfiltered total; Decision Log D118).
//
// P2CustomerView is a deliberately minimal, VALID, partial, single-source typed Style B view. Because
// it is partial, derives from a2n.Vista.Authoring.View<TQuery>, and this assembly references the EF
// layer, the source generator (referenced as an analyzer here) emits — INTO this assembly — a
// `file sealed` CompiledViewExecutionPlan_<View> plus a [ModuleInitializer] that registers the plan
// into a2n.Vista.EntityFrameworkCore.Execution.GeneratedExecutionPlanStore at module load. The test
// project drives List paging against this view through the REAL generated compiled plan.

using a2n.Vista.Authoring;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.Examples.StyleBExecP2;

/// <summary>EF source entity for the List view. <see cref="Id"/> is the primary key (convention).</summary>
public sealed class P2Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Score { get; set; }
}

/// <summary>Projected (read) row for <see cref="P2CustomerView"/>; member-init projection so EF translates it.</summary>
public sealed class P2CustomerRow
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Score { get; init; }
}

/// <summary>
/// Single-key, single-source Style B read view named <c>p2-customers</c>. The <c>partial</c> modifier
/// and implicit public parameterless constructor let the generator emit the compiled plan and its
/// module-initializer registration. <c>Id</c> is the declared primary key, used as the deterministic
/// paging tiebreaker (D106).
/// </summary>
public partial class P2CustomerView : View<P2CustomerRow>
{
    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P2CustomerRow> builder)
        => builder.Named("p2-customers")
                  .From<P2Customer>(s => new P2CustomerRow { Id = s.Id, Name = s.Name, Score = s.Score })
                  .Field(x => x.Id, f => f.PrimaryKey());
}

/// <summary>
/// EF context exposing the fixture entity. <see cref="P2Customer"/> uses the conventional <c>Id</c>
/// primary key. The test seeds a SQLite-backed instance of this context.
/// </summary>
public sealed class P2TestDbContext : DbContext
{
    public P2TestDbContext(DbContextOptions<P2TestDbContext> options)
        : base(options)
    {
    }

    public DbSet<P2Customer> Customers => Set<P2Customer>();
}
