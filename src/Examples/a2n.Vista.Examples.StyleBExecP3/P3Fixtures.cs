// Licensed to the a2n.Vista project. Published artifact — English only.
//
// EF-aware consumer fixtures for the source-generator Phase 2 compiled execution plan
// (spec style-b-executable, Property 3 — Detail-by-key round-trip; Decision Log D118).
//
// These types are deliberately minimal, VALID, partial, single-source typed Style B views. Because
// each is partial, derives from a2n.Vista.Authoring.View<TQuery>, and this assembly references the EF
// layer, the source generator (referenced as an analyzer here) emits — INTO this assembly — a
// `file sealed` CompiledViewExecutionPlan_<View> plus a [ModuleInitializer] that registers the plan
// into a2n.Vista.EntityFrameworkCore.Execution.GeneratedExecutionPlanStore at module load. The test
// project drives Detail-by-key against these views through the REAL generated compiled plan.
//
// Two views are provided so the round-trip property covers both key arities:
//   * P3OrderView      — single-key view (Id) over P3Order;
//   * P3CompositeView  — composite-key view (OrderId, LineNo) over P3LineItem.

using a2n.Vista.Authoring;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.Examples.StyleBExecP3;

/// <summary>EF source entity for the single-key view. <see cref="Id"/> is the primary key (convention).</summary>
public sealed class P3Order
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

/// <summary>Projected (read) row for <see cref="P3OrderView"/>; member-init projection so EF translates it.</summary>
public sealed class P3OrderRow
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Quantity { get; init; }
}

/// <summary>
/// Single-key, single-source Style B read view named <c>p3-orders</c>. The <c>partial</c> modifier and
/// implicit public parameterless constructor let the generator emit the compiled plan and its
/// module-initializer registration. <c>Id</c> is the declared primary key.
/// </summary>
public partial class P3OrderView : View<P3OrderRow>
{
    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P3OrderRow> builder)
        => builder.Named("p3-orders")
                  .From<P3Order>(s => new P3OrderRow { Id = s.Id, Name = s.Name, Quantity = s.Quantity })
                  .Field(x => x.Id, f => f.PrimaryKey());
}

/// <summary>EF source entity for the composite-key view; keyed by (<see cref="OrderId"/>, <see cref="LineNo"/>).</summary>
public sealed class P3LineItem
{
    public int OrderId { get; set; }

    public int LineNo { get; set; }

    public string Sku { get; set; } = string.Empty;
}

/// <summary>Projected (read) row for <see cref="P3CompositeView"/>.</summary>
public sealed class P3LineItemRow
{
    public int OrderId { get; init; }

    public int LineNo { get; init; }

    public string Sku { get; init; } = string.Empty;
}

/// <summary>
/// Composite-key, single-source Style B read view named <c>p3-order-lines</c>. The key is declared
/// explicitly via <c>Key(...)</c> in (OrderId, LineNo) order, the order used for Detail-by-key and the
/// deterministic paging tiebreaker.
/// </summary>
public partial class P3CompositeView : View<P3LineItemRow>
{
    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P3LineItemRow> builder)
        => builder.Named("p3-order-lines")
                  .From<P3LineItem>(s => new P3LineItemRow { OrderId = s.OrderId, LineNo = s.LineNo, Sku = s.Sku })
                  .Key(x => x.OrderId, x => x.LineNo);
}

/// <summary>
/// EF context exposing the fixture entities. The single-key <see cref="P3Order"/> uses the conventional
/// <c>Id</c> primary key; the composite <see cref="P3LineItem"/> declares its (OrderId, LineNo) key in
/// <see cref="OnModelCreating"/>. The test seeds a SQLite-backed instance of this context.
/// </summary>
public sealed class P3TestDbContext : DbContext
{
    public P3TestDbContext(DbContextOptions<P3TestDbContext> options)
        : base(options)
    {
    }

    public DbSet<P3Order> Orders => Set<P3Order>();

    public DbSet<P3LineItem> OrderLines => Set<P3LineItem>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<P3LineItem>().HasKey(e => new { e.OrderId, e.LineNo });
        base.OnModelCreating(modelBuilder);
    }
}
