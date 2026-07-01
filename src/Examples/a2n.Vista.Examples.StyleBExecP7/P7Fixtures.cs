// Licensed to the a2n.Vista project. Published artifact — English only.
//
// EF-aware consumer fixtures for the source-generator Phase 2 single-source PK auto-derivation
// (spec style-b-executable, Property 7 — D105 / M11; Decision Log D118).
//
// These types are deliberately minimal, VALID, partial, single-source typed Style B views. Because
// each is partial, derives from a2n.Vista.Authoring.View<TQuery>, and this assembly references the EF
// layer, the source generator (referenced as an analyzer here) emits — INTO this assembly — a
// `file sealed` CompiledViewExecutionPlan_<View> plus a [ModuleInitializer] that registers the plan
// into a2n.Vista.EntityFrameworkCore.Execution.GeneratedExecutionPlanStore at module load.
//
// Four views cover the two derivation outcomes Property 7 asserts:
//   * P7OrderDerivedView ("p7-orders-derived")   — KEYLESS, single PK source  → derives [Id].
//   * P7LineDerivedView  ("p7-lines-derived")    — KEYLESS, composite PK source → derives [OrderId, LineNo].
//   * P7OrderExplicitView ("p7-orders-explicit") — EXPLICIT key (Id)          → untouched.
//   * P7LineExplicitView  ("p7-lines-explicit")  — EXPLICIT REVERSED key      → [LineNo, OrderId] untouched.
//
// The KEYLESS views declare no key on purpose: their compiled plan lets Register<TView>() defer the
// D106 fail-fast so VistaModelKeyDerivationService completes KeyFields from DbContext.Model at startup.
// The composite EXPLICIT view declares its key in the REVERSED model order so the test can prove the
// hook neither overrides nor re-orders an author-declared key (R6.3).

using a2n.Vista.Authoring;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.Examples.StyleBExecP7;

/// <summary>EF source entity with a conventional single primary key (<see cref="Id"/>).</summary>
public sealed class P7Order
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

/// <summary>Projected (read) row for the order views; member-init projection so EF translates it.</summary>
public sealed class P7OrderRow
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Quantity { get; init; }
}

/// <summary>EF source entity with a composite primary key (<see cref="OrderId"/>, <see cref="LineNo"/>).</summary>
public sealed class P7LineItem
{
    public int OrderId { get; set; }

    public int LineNo { get; set; }

    public string Sku { get; set; } = string.Empty;
}

/// <summary>Projected (read) row for the line views.</summary>
public sealed class P7LineItemRow
{
    public int OrderId { get; init; }

    public int LineNo { get; init; }

    public string Sku { get; init; } = string.Empty;
}

/// <summary>
/// KEYLESS single-source view named <c>p7-orders-derived</c> over <see cref="P7Order"/>. It declares no
/// key, so its generated compiled plan lets registration defer the key fail-fast and the startup hook
/// derives <c>KeyFields = [Id]</c> from <c>DbContext.Model</c> (R6.1).
/// </summary>
public partial class P7OrderDerivedView : View<P7OrderRow>
{
    /// <summary>The globally-unique view name.</summary>
    public const string ViewName = "p7-orders-derived";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P7OrderRow> builder)
        => builder.Named(ViewName)
                  .From<P7Order>(s => new P7OrderRow { Id = s.Id, Name = s.Name, Quantity = s.Quantity });
}

/// <summary>
/// KEYLESS single-source view named <c>p7-lines-derived</c> over the composite-key
/// <see cref="P7LineItem"/>. The startup hook derives <c>KeyFields = [OrderId, LineNo]</c> in the model's
/// declared key-column order (R6.2).
/// </summary>
public partial class P7LineDerivedView : View<P7LineItemRow>
{
    /// <summary>The globally-unique view name.</summary>
    public const string ViewName = "p7-lines-derived";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P7LineItemRow> builder)
        => builder.Named(ViewName)
                  .From<P7LineItem>(s => new P7LineItemRow { OrderId = s.OrderId, LineNo = s.LineNo, Sku = s.Sku });
}

/// <summary>
/// EXPLICIT-key single-source view named <c>p7-orders-explicit</c> over <see cref="P7Order"/>. The author
/// declares <c>Id</c> as the primary key, so the startup hook must leave <c>KeyFields = [Id]</c> untouched
/// (R6.3) — never re-deriving it from the model.
/// </summary>
public partial class P7OrderExplicitView : View<P7OrderRow>
{
    /// <summary>The globally-unique view name.</summary>
    public const string ViewName = "p7-orders-explicit";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P7OrderRow> builder)
        => builder.Named(ViewName)
                  .From<P7Order>(s => new P7OrderRow { Id = s.Id, Name = s.Name, Quantity = s.Quantity })
                  .Field(x => x.Id, f => f.PrimaryKey());
}

/// <summary>
/// EXPLICIT-key single-source view named <c>p7-lines-explicit</c> over the composite-key
/// <see cref="P7LineItem"/>. The author declares the key in the REVERSED model order — (LineNo, OrderId)
/// — so the test can prove the startup hook neither overrides nor re-orders a declared key (R6.3): its
/// <c>KeyFields</c> stays <c>[LineNo, OrderId]</c>, distinct from the model order <c>[OrderId, LineNo]</c>.
/// </summary>
public partial class P7LineExplicitView : View<P7LineItemRow>
{
    /// <summary>The globally-unique view name.</summary>
    public const string ViewName = "p7-lines-explicit";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P7LineItemRow> builder)
        => builder.Named(ViewName)
                  .From<P7LineItem>(s => new P7LineItemRow { OrderId = s.OrderId, LineNo = s.LineNo, Sku = s.Sku })
                  .Key(x => x.LineNo, x => x.OrderId);
}

/// <summary>
/// EF context exposing the fixture entities. <see cref="P7Order"/> uses the conventional <c>Id</c> primary
/// key; <see cref="P7LineItem"/> declares its (OrderId, LineNo) composite key in <see cref="OnModelCreating"/>.
/// The test resolves this context at startup so the derivation hook can read the model primary keys.
/// </summary>
public sealed class P7TestDbContext : DbContext
{
    public P7TestDbContext(DbContextOptions<P7TestDbContext> options)
        : base(options)
    {
    }

    public DbSet<P7Order> Orders => Set<P7Order>();

    public DbSet<P7LineItem> OrderLines => Set<P7LineItem>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<P7LineItem>().HasKey(e => new { e.OrderId, e.LineNo });
        base.OnModelCreating(modelBuilder);
    }
}
