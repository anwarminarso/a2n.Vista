// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Fixtures for the Generated/RUC behavioral-parity property test (source-generator Phase 2, D118;
// tasks.md §7.6; design Property 1). Declared in a real EF + source-generator consumer assembly so the
// generator's Phase 2 emitter produces a REAL CompiledViewExecutionPlan_<P1CustomerView> and registers
// it into GeneratedExecutionPlanStore via a [ModuleInitializer] at module load.
//
// Type prefix `P1` (Property 1) avoids collisions with the sibling parity tasks (7.3-7.5).

using System.Linq.Expressions;
using a2n.Vista.Authoring;

namespace a2n.Vista.StyleBExecSample;

/// <summary>
/// EF source entity the parity view projects from. A plain POCO with a conventional integer key
/// (<see cref="Id"/>), mapped by the test's DbContext over SQLite.
/// </summary>
public sealed class P1Customer
{
    /// <summary>Primary key (EF infers it by convention).</summary>
    public int Id { get; set; }

    /// <summary>A name string — filterable, sortable, searchable by default.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A city string — also made client-scopable so the Scope channel is exercised.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>A numeric balance — filterable/sortable, not searchable.</summary>
    public double Balance { get; set; }

    /// <summary>A numeric age — filterable/sortable, not searchable.</summary>
    public int Age { get; set; }
}

/// <summary>
/// Projected (read) row sent to clients. Declared as a class with init-only auto-properties and
/// projected via member-initialization so EF Core maps each assigned member back to its source column
/// (the same translatable shape as the existing WidgetRow fixture), letting filter/sort push down to SQL.
/// </summary>
public sealed class P1CustomerRow
{
    /// <summary>Primary key.</summary>
    public int Id { get; init; }

    /// <summary>Name (filterable/sortable/searchable).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>City (filterable/sortable/searchable + scopable).</summary>
    public string City { get; init; } = string.Empty;

    /// <summary>Balance (filterable/sortable).</summary>
    public double Balance { get; init; }

    /// <summary>Age (filterable/sortable).</summary>
    public int Age { get; init; }
}

/// <summary>
/// Typed Style B view over <see cref="P1Customer"/>. It is <c>partial</c>, single-source, has a public
/// parameterless constructor, declares a primary key, and uses a member-initialization projection — all
/// the conditions the Phase 2 emitter needs to generate a compiled execution plan
/// (<c>CompiledViewExecutionPlan_P1CustomerView</c>) and register it at module load.
/// </summary>
public partial class P1CustomerView : View<P1CustomerRow>
{
    /// <summary>The globally-unique view name; also the key the generated plan is stored under.</summary>
    public const string ViewName = "stylebexec-parity-p1-customers";

    /// <summary>
    /// The read projection, reproduced verbatim as the inline lambda in <see cref="Configure"/>. Exposed
    /// as a static so the parity test can build a hand-written <c>SplitViewExecutionPlan</c> (the RUC
    /// reference model) over the EXACT same member-init shape — guaranteeing both execution paths root on
    /// an identical projection and differ only in how the expression tree is built.
    /// </summary>
    public static readonly Expression<System.Func<P1Customer, P1CustomerRow>> Projection =
        src => new P1CustomerRow
        {
            Id = src.Id,
            Name = src.Name,
            City = src.City,
            Balance = src.Balance,
            Age = src.Age,
        };

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P1CustomerRow> builder) =>
        builder
            .Named(ViewName)
            .From<P1Customer>(src => new P1CustomerRow
            {
                Id = src.Id,
                Name = src.Name,
                City = src.City,
                Balance = src.Balance,
                Age = src.Age,
            })
            .Field(x => x.Id, f => f.PrimaryKey())
            .Field(x => x.City, f => f.Scopable());
}
