// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Real generator consumer for Style B executable Property 6 (Spec style-b-executable, task 9.3 —
// masked fields are non-probeable).
//
// P6PersonView is a partial typed Style B view projected from a single EF source entity. Because this
// assembly references Core AND the EF layer and the projection is a statically reproducible
// member-initialization, the source generator emits an AOT-clean ICompiledViewExecutionPlan for it and
// registers it into GeneratedExecutionPlanStore via a [ModuleInitializer] at module load. Touching any
// type in this assembly (for example `new P6PersonView()`) forces the module to load so the plan is
// present before AddVista(v => v.Register<P6PersonView>()) drains the store.
//
// The view deliberately carries a masked-WITHOUT-opt-in string field (Secret): D95 makes it
// non-filterable and non-searchable by default, so a client must not be able to reconstruct its value
// by probing filter/search responses. An ordinary searchable/filterable string field (Name) is also
// projected, so the non-probeable property can exercise a real global-search channel that excludes
// Secret.

using a2n.Vista.Authoring;

namespace a2n.Vista.GeneratorExecSampleP6;

/// <summary>The single EF source entity the <see cref="P6PersonView"/> projects from.</summary>
public sealed class P6PersonSource
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>An ordinary, filterable/sortable/searchable string field.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A sensitive string field that the view masks without any filter/search opt-in.</summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>
/// The projected (read) row type for <see cref="P6PersonView"/>. It is a <c>record</c> so the generated
/// <see cref="a2n.Vista.Metadata.MaskAccessor"/> setter rebuilds the row with a <c>with</c> expression —
/// the AOT-clean success path masking at materialization needs (an init-only property on a non-record
/// class would emit a fail-closed setter, so masking could never succeed and List would always fail).
/// </summary>
public sealed record P6PersonRow
{
    /// <summary>Primary key — filterable and sortable.</summary>
    public int Id { get; init; }

    /// <summary>Ordinary string field — filterable, sortable, searchable.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Masked-without-opt-in string field — non-filterable and non-searchable (D95).</summary>
    public string Secret { get; init; } = string.Empty;
}

/// <summary>
/// A partial typed Style B read-only view with a masked-without-opt-in field. The <c>partial</c>
/// modifier and the implicit public parameterless constructor satisfy the generator's VISTA0001 /
/// VISTA0002 requirements, and the single-source member-initialization projection is statically
/// reproducible, so the generator emits a compiled execution plan for it. The masker replaces the
/// value with a constant token, so the masked output is identical regardless of the original value —
/// exactly what the non-probeable property relies on.
/// </summary>
public partial class P6PersonView : View<P6PersonRow>
{
    /// <summary>The globally-unique view name; also the key the generated plan is stored under.</summary>
    public const string ViewName = "p6-nonprobeable-person";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P6PersonRow> builder)
        => builder.Named(ViewName)
                  .From<P6PersonSource>(s => new P6PersonRow { Id = s.Id, Name = s.Name, Secret = s.Secret })
                  .Field(x => x.Id, f => f.PrimaryKey())
                  // Masked with no Filterable(true)/Searchable(true) opt-in → non-probeable by default (D95).
                  .MaskField(x => x.Secret, _ => true, _ => "***");
}
