// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Real generator consumer for Style B executable Property 5 (Spec style-b-executable, task 9.2 —
// conditional masking at materialization).
//
// P5AccountView is a partial typed Style B view projected from a single EF source entity. Because this
// assembly references Core AND the EF layer and the projection is a statically reproducible
// member-initialization, the source generator emits an AOT-clean ICompiledViewExecutionPlan for it and
// registers it into GeneratedExecutionPlanStore via a [ModuleInitializer] at module load. Touching any
// type in this assembly (for example `new P5AccountView()`) forces the module to load so the plan is
// present before AddVista(v => v.Register<P5AccountView>()) drains the store.
//
// The projected row type (P5AccountRow) is a RECORD, so the generated MaskAccessor setter is a
// `with`-style rebuild (the success path conditional masking needs; an init-only property on a
// non-record class would emit a fail-closed setter). The masked field's masker is a function of the
// ORIGINAL value (R7.3) — it embeds the original length — so the masked output is observably derived
// from the pre-mask value rather than a constant. The shouldMask predicate reads a DI toggle
// (IP5MaskToggle), letting a test flip masking on/off per request and assert the SQL is unaffected.

using System;
using a2n.Vista.Authoring;

namespace a2n.Vista.GeneratorExecSampleP5;

/// <summary>
/// A request-scoped toggle the <see cref="P5AccountView"/> masking predicate reads to decide whether a
/// row's <see cref="P5AccountRow.Secret"/> is masked for the current request. Resolving it through
/// <see cref="IServiceProvider.GetService"/> (no DI package needed) keeps the fixture EF/Core-only while
/// still letting a test register an instance and flip <see cref="Enabled"/> between requests.
/// </summary>
public interface IP5MaskToggle
{
    /// <summary>When <see langword="true"/>, the view masks <see cref="P5AccountRow.Secret"/>.</summary>
    bool Enabled { get; }
}

/// <summary>The single EF source entity the <see cref="P5AccountView"/> projects from.</summary>
public sealed class P5AccountSource
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>An ordinary, unmasked string field — must pass through masking unchanged.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>A sensitive string field the view masks conditionally on the request toggle.</summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>
/// The projected (read) row type for <see cref="P5AccountView"/>. It is a <c>record</c> so the generated
/// <see cref="a2n.Vista.Metadata.MaskAccessor"/> setter rebuilds the row with a <c>with</c> expression —
/// the AOT-clean success path conditional masking at materialization exercises.
/// </summary>
public sealed record P5AccountRow
{
    /// <summary>Primary key — filterable and sortable.</summary>
    public int Id { get; init; }

    /// <summary>Ordinary string field — left untouched by masking.</summary>
    public string Owner { get; init; } = string.Empty;

    /// <summary>The conditionally-masked string field.</summary>
    public string Secret { get; init; } = string.Empty;
}

/// <summary>
/// A partial typed Style B read-only view with a conditionally-masked field. The <c>partial</c> modifier
/// and the implicit public parameterless constructor satisfy the generator's VISTA0001 / VISTA0002
/// requirements, and the single-source member-initialization projection is statically reproducible, so
/// the generator emits a compiled execution plan for it.
/// </summary>
public partial class P5AccountView : View<P5AccountRow>
{
    /// <summary>The globally-unique view name; also the key the generated plan is stored under.</summary>
    public const string ViewName = "p5-conditional-masking-account";

    /// <summary>
    /// The deterministic masker the view applies to <see cref="P5AccountRow.Secret"/>. It is a pure
    /// function of the <b>original</b> (pre-mask) value (R7.3): the masked token embeds the original
    /// length, so the masked output is observably derived from the original rather than a constant. A
    /// test computes the expected masked value with this same method.
    /// </summary>
    /// <param name="original">The pre-mask field value.</param>
    /// <returns>The masked replacement value.</returns>
    public static string Mask(string original) => "MASKED(" + (original?.Length ?? 0) + ")";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<P5AccountRow> builder)
        => builder.Named(ViewName)
                  .From<P5AccountSource>(s => new P5AccountRow { Id = s.Id, Owner = s.Owner, Secret = s.Secret })
                  .Field(x => x.Id, f => f.PrimaryKey())
                  // shouldMask reads the request toggle (R7.2: evaluated once per request); the masker is a
                  // function of the original value (R7.3). Opt the field into filtering so the masking
                  // property can also exercise a filtered request without tripping the D95 default whitelist.
                  .MaskField(x => x.Secret, ShouldMask, Mask)
                  .Field(x => x.Owner, f => f.Filterable());

    /// <summary>Reads <see cref="IP5MaskToggle.Enabled"/> from request services; defaults to off when absent.</summary>
    private static bool ShouldMask(IServiceProvider services)
        => services.GetService(typeof(IP5MaskToggle)) is IP5MaskToggle toggle && toggle.Enabled;
}
