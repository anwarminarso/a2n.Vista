// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Real generator consumer for Style B executable Property 4 (Spec style-b-executable, task 7.5).
//
// P4PersonView is a partial typed Style B view projected from a single EF source entity. Because this
// assembly references Core AND the EF layer and the projection is a statically reproducible
// member-initialization, the source generator emits an AOT-clean ICompiledViewExecutionPlan for it and
// registers it into GeneratedExecutionPlanStore via a [ModuleInitializer] at module load. Touching any
// type in this assembly (for example `new P4PersonView()`) forces the module to load so the plan is
// present before AddVista(v => v.Register<P4PersonView>()) drains the store.
//
// The view deliberately carries a masked-WITHOUT-opt-in string field (Secret): D95 makes it
// non-filterable and non-searchable by default, which is one of the rejection vectors Property 4
// asserts (the other being a non-projected field).

using a2n.Vista.Authoring;

namespace a2n.Vista.GeneratorExecSampleP4;

/// <summary>The single EF source entity the <see cref="P4PersonView"/> projects from.</summary>
public sealed class P4PersonSource
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>An ordinary, filterable/sortable/searchable string field.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A sensitive string field that the view masks without any filter/search opt-in.</summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>The projected (read) row type for <see cref="P4PersonView"/>.</summary>
public sealed class P4PersonRow
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
/// reproducible, so the generator emits a compiled execution plan for it.
/// </summary>
public partial class P4PersonView : View<P4PersonRow>
{
    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P4PersonRow> builder)
        => builder.Named("p4-disallowed-field-person")
                  .From<P4PersonSource>(s => new P4PersonRow { Id = s.Id, Name = s.Name, Secret = s.Secret })
                  .Field(x => x.Id, f => f.PrimaryKey())
                  // Masked with no Filterable(true)/Searchable(true) opt-in → non-probeable by default (D95).
                  .MaskField(x => x.Secret, _ => true, _ => "***");
}
