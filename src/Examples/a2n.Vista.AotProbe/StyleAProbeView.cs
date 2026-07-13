// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Self-contained Style A (central-template) fixtures for the M9 Style A coverage AOT verification
// (spec style-a-coverage, Task 9.1, R9.1/R9.2/R9.3; Decision Log D129/D130).
//
// WHY THESE FIXTURES ARE MIRRORED LOCALLY (not a reference to a2n.Vista.GeneratorStyleASample):
//   The Style A coverage phase already ships representative fixtures in the a2n.Vista.GeneratorStyleASample
//   consumer assembly (Task 8.1), and the property tests reuse them. The AOT probe, however, verifies
//   trim/AOT correctness — and the generated artifacts are registered into a2n.Vista.Core's stores by
//   [ModuleInitializer]s emitted INTO the assembly that declares the AddView call sites. A module
//   initializer runs only when its module is loaded; for a NON-entry assembly reached solely via inlined
//   const view-name strings that load is not guaranteed, and under trimming/AOT — the probe's whole point —
//   an assembly reached only through inlined consts can be trimmed away entirely, so its initializer would
//   never run and the store lookups would miss. Every existing probe fixture (StyleBProbeView,
//   WritableProbeView, ProbeStyleATemplate) is therefore declared LOCALLY so its generated
//   [ModuleInitializer]s live in the entry assembly, whose module is always initialized at process start.
//   These Style A fixtures follow that exact pattern — the layering reason the task anticipates for
//   mirroring rather than referencing — so the probe deterministically resolves the generated Style A
//   artifacts from the Core stores.
//
// WHAT THE GENERATOR EMITS FOR THESE FIXTURES:
//   Because the probe assembly references Core AND the source generator (as an analyzer), the fifth
//   incremental generator (StyleAShapeGenerator, D129) recognizes the AddView call sites in
//   StyleACoverageProbeTemplate.Configure and emits — INTO this assembly, per COVERED view:
//     * for the named-TRow read view (aotprobe-stylea-catalog): a `file static`
//       <Template>_<View>_VistaAccessors.g.cs export accessor map registered into
//       a2n.Vista.Metadata.ViewAccessorRegistry (D117), plus a `file sealed`
//       <Template>_<View>_VistaJsonContext.g.cs IJsonTypeInfoResolver (built via JsonMetadataServices, NOT
//       the [JsonSerializable] route) covering { StyleACatalogRow, ViewListResult<StyleACatalogRow>,
//       PagedResult<StyleACatalogRow> } registered into a2n.Vista.Metadata.GeneratedJsonContextStore (D125);
//     * for the writable ANONYMOUS-TRow view (aotprobe-stylea-audit): a `file sealed` per-view context
//       covering ONLY its named TCrud (StyleAAuditCrud) — NO export accessor and NO read-DTO context,
//       because an anonymous read row is unnameable in generated source and stays permanently
//       [RequiresUnreferencedCode] by design (D96/D130, VISTA0061). This is the D96 asymmetry WITHIN one
//       view: the write body binds AOT-clean while the read row does not.
//   Each artifact is keyed by the CONSTANT AddView name (the D129 difference from D125's `new View().Name`
//   — a Style A view is an AddView call site, not a class). StyleACoverageProbe.cs then drives those real
//   generated artifacts under the strict (IL2026/IL3050-as-error) analyzer.
//
// THE DATA SOURCE IS A PLAIN, EF-FREE CLASS ON PURPose:
//   a2n.Vista.Authoring.ViewTemplate<TDbContext> constrains TDbContext to `class` (Core is EF-free, D48),
//   so a genuine EF DbContext is NOT required — the generator recognizes an AddView call site purely by the
//   enclosing type deriving ViewTemplate<TDbContext> and the call resolving to
//   IViewTemplateBuilder<TDbContext>.AddView<TRow> (both by fully-qualified name). Using a plain class that
//   exposes IQueryable<T> projections keeps the fixture free of EF's RUC ctor/Set<T> surface, so the
//   template's Configure needs no suppression and the probe's AOT-clean surface stays honest. The source is
//   never instantiated at runtime by the probe (it resolves the generated artifacts from the stores); it
//   exists only so the AddView projections compile and the generator can analyze them.

using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Authoring;

namespace a2n.Vista.AotProbe;

/// <summary>
/// Availability state for <see cref="StyleACatalogRow"/> — an <c>enum</c> member shape. The generated
/// read-DTO <c>JsonTypeInfo</c> serializes it through the AOT-safe GENERIC
/// <c>JsonStringEnumConverter&lt;TEnum&gt;</c> (net8/9/10 shared framework), never the RUC non-generic
/// converter, so a covered named-row Style A view carrying an enum is still trim/AOT-clean (D129).
/// </summary>
public enum StyleACatalogStatus
{
    /// <summary>Not yet published.</summary>
    Draft = 0,

    /// <summary>Available for sale.</summary>
    Active = 1,

    /// <summary>No longer offered.</summary>
    Discontinued = 2,
}

/// <summary>
/// Source entity for the read-only, named-row <c>aotprobe-stylea-catalog</c> view. A plain POCO the Style A
/// projection reads from; never materialized at runtime by the probe.
/// </summary>
public sealed class StyleACatalogEntity
{
    /// <summary>Primary key.</summary>
    public int ItemId { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional reorder threshold — a nullable value-type scalar.</summary>
    public int? ReorderLevel { get; set; }

    /// <summary>Availability state — an enum scalar.</summary>
    public StyleACatalogStatus Status { get; set; }

    /// <summary>Free-form tags — a collection of an emittable element.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Thumbnail bytes — a <c>byte[]</c> member.</summary>
    public byte[] Thumbnail { get; set; } = System.Array.Empty<byte>();
}

/// <summary>
/// Source entity for the writable, anonymous-row <c>aotprobe-stylea-audit</c> view. The read projection
/// selects an anonymous subset of these members (unnameable in generated source, so its read side stays
/// RUC); the whitelisted members are the write targets of <see cref="StyleAAuditCrud"/>.
/// </summary>
public sealed class StyleAAuditEntity
{
    /// <summary>Primary key.</summary>
    public int EntryId { get; set; }

    /// <summary>Whitelisted string scalar.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Whitelisted value-type scalar.</summary>
    public int Severity { get; set; }

    /// <summary>Whitelisted nullable value-type scalar.</summary>
    public System.DateTime? OccurredAt { get; set; }

    /// <summary>Whitelisted boolean scalar.</summary>
    public bool IsSensitive { get; set; }
}

/// <summary>
/// Projected (read) row for the <c>aotprobe-stylea-catalog</c> view — a NAMED type spanning the emittable
/// read-DTO spectrum: a scalar (<see cref="ItemId"/>), a nullable value type (<see cref="ReorderLevel"/>),
/// an enum (<see cref="Status"/>), a collection (<see cref="Tags"/>), and a <c>byte[]</c>
/// (<see cref="Thumbnail"/>). Named, so it is nameable in generated source — the read-side coverage
/// precondition (D129): the generator emits export accessors and a read-DTO <c>JsonTypeInfo</c> context for
/// this view.
/// </summary>
public sealed class StyleACatalogRow
{
    /// <summary>Primary key — a scalar member.</summary>
    public int ItemId { get; init; }

    /// <summary>Name — a string member.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional reorder threshold — a nullable value-type member.</summary>
    public int? ReorderLevel { get; init; }

    /// <summary>Availability — an enum member.</summary>
    public StyleACatalogStatus Status { get; init; }

    /// <summary>Tags — a collection member (<see cref="IReadOnlyList{T}"/> of an emittable element).</summary>
    public IReadOnlyList<string> Tags { get; init; } = System.Array.Empty<string>();

    /// <summary>Thumbnail — a <c>byte[]</c> member.</summary>
    public byte[] Thumbnail { get; init; } = System.Array.Empty<byte>();
}

/// <summary>
/// Typed write contract (<c>TCrud</c>) for the writable, anonymous-row <c>aotprobe-stylea-audit</c> view — a
/// named <c>record</c> with a <c>required</c> member (<see cref="Action"/>) and <c>init</c>-only members, so
/// the generated <c>JsonTypeInfo</c> must construct instances through the parameterized/<c>init</c> path
/// (R3.4). It is covered for <c>TCrud</c> <c>JsonTypeInfo</c> even though the view's read row is anonymous
/// (R4.2) — the whole point of the D96 asymmetry fixture: the write body binds AOT-clean while the read row
/// stays RUC. A write model is always a named type (the authoring surface forbids an anonymous write model,
/// D38).
/// </summary>
public sealed record StyleAAuditCrud
{
    /// <summary>Action performed — a <c>required</c> member.</summary>
    public required string Action { get; init; }

    /// <summary>Severity level — an <c>init</c>-only value-type member.</summary>
    public int Severity { get; init; }

    /// <summary>When it occurred — an <c>init</c>-only nullable value-type member.</summary>
    public System.DateTime? OccurredAt { get; init; }

    /// <summary>Whether the entry is sensitive — an <c>init</c>-only boolean member.</summary>
    public bool IsSensitive { get; init; }
}

/// <summary>
/// A minimal, EF-free stand-in data source the Style A projections are expressed against. Because
/// <see cref="ViewTemplate{TDbContext}"/> constrains <c>TDbContext</c> to <c>class</c> (Core is EF-free,
/// D48), a genuine EF <c>DbContext</c> is not required; a plain class exposing <see cref="IQueryable{T}"/>
/// projections is enough for the generator to recognize the <c>AddView</c> call sites and for the
/// projections to compile. It is never instantiated at runtime by the probe — the probe resolves the
/// generated artifacts from the Core stores by the constant view names — so the empty queryables are only a
/// compile-time projection root, never enumerated.
/// </summary>
public sealed class StyleAProbeSource
{
    /// <summary>Projection root for the read-only, named-row <c>aotprobe-stylea-catalog</c> view.</summary>
    public IQueryable<StyleACatalogEntity> Catalog => EmptyQueryable<StyleACatalogEntity>();

    /// <summary>Projection root for the writable, anonymous-row <c>aotprobe-stylea-audit</c> view.</summary>
    public IQueryable<StyleAAuditEntity> Audit => EmptyQueryable<StyleAAuditEntity>();

    /// <summary>
    /// Produces the empty <see cref="IQueryable{T}"/> the Style A projections are rooted on. This is a
    /// COMPILE-TIME projection root only: it exists so the <c>AddView</c> projections type-check and the
    /// generator can analyze their shapes — it is never enumerated at runtime (the probe resolves the
    /// generated artifacts from the Core stores by the constant view names, never calling
    /// <see cref="ViewTemplate{TDbContext}.BuildViews"/> or <c>Configure</c>). <c>Queryable.AsQueryable</c>
    /// is framework infrastructure documented as not trim/AOT compatible (it can rebind to
    /// <c>IEnumerable</c> extension methods at runtime); it is NOT the generated Style A accessor /
    /// serialization path under verification, so the single call is isolated here behind a
    /// narrowly-scoped suppression — exactly as the sibling probes isolate EF provider/schema setup.
    /// Nothing on the verified generated surface is suppressed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "Compile-time-only projection root for the generator; never enumerated at runtime, and not the generated Style A path under verification (D129/D130, R9.1/R9.2).")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "Compile-time-only projection root for the generator; never enumerated at runtime, and not the generated Style A path under verification (D129/D130, R9.1/R9.2).")]
    private static IQueryable<T> EmptyQueryable<T>() => Enumerable.Empty<T>().AsQueryable();
}

/// <summary>
/// Central-template (Style A / Gaya A) authoring for the Style A coverage AOT fixtures. Declares the two
/// representative views the probe (<see cref="StyleACoverageProbe"/>) drives under the trim/AOT analyzer:
/// a covered named-row read view and the D96-asymmetry writable anonymous-row view. Deriving
/// <see cref="ViewTemplate{TDbContext}"/> and calling
/// <c>IViewTemplateBuilder&lt;TDbContext&gt;.AddView&lt;TRow&gt;</c> is what makes the generator recognize
/// the call sites (both by fully-qualified name). <c>Configure</c> carries no suppression: the authoring
/// DSL (<c>AddView</c>/<c>Field</c>/<c>WithCrud</c>/<c>MapWritable</c>) is AOT-clean — only
/// <see cref="ViewTemplate{TDbContext}.BuildViews"/> is RUC, and the probe never calls it (it resolves the
/// generated artifacts from the stores instead).
/// </summary>
public sealed class StyleACoverageProbeTemplate : ViewTemplate<StyleAProbeSource>
{
    /// <summary>
    /// Constant view name for the read-only, named-row catalog view — the key its generated export accessor
    /// map and read-DTO <c>JsonTypeInfo</c> context are registered under.
    /// </summary>
    public const string CatalogViewName = "aotprobe-stylea-catalog";

    /// <summary>
    /// Constant view name for the writable, anonymous-row audit view — the key its generated
    /// <c>TCrud</c>-only <c>JsonTypeInfo</c> context is registered under (its read row is anonymous, so no
    /// read-side artifact is generated — the D96 asymmetry).
    /// </summary>
    public const string AuditViewName = "aotprobe-stylea-audit";

    /// <inheritdoc />
    protected override void Configure(IViewTemplateBuilder<StyleAProbeSource> views)
    {
        // Case 1 — READ-ONLY, NAMED-TRow (StyleACatalogRow). Projects into a named row DTO spanning the
        // emittable-shape spectrum. Named TRow + constant name → export accessors + read-DTO JsonTypeInfo
        // generated (VISTA0060). No WithCrud → read-only.
        views.AddView(CatalogViewName, (db, sp) =>
                db.Catalog.Select(e => new StyleACatalogRow
                {
                    ItemId = e.ItemId,
                    Name = e.Name,
                    ReorderLevel = e.ReorderLevel,
                    Status = e.Status,
                    Tags = e.Tags,
                    Thumbnail = e.Thumbnail,
                }))
            .Field(x => x.ItemId, f => f.PrimaryKey());

        // Case 2 — WRITABLE, ANONYMOUS-TRow (the D96 asymmetry) with a named TCrud (StyleAAuditCrud). The
        // read projection is an ANONYMOUS type (unnameable in generated source), so its read side stays on
        // the reflection path by design (VISTA0061); the named, emittable TCrud is still covered
        // (VISTA0060 write side). Result: TCrud JsonTypeInfo ONLY — the write body binds AOT-clean while the
        // read row does not.
        views.AddView(AuditViewName, (db, sp) =>
                db.Audit.Select(e => new
                {
                    e.EntryId,
                    e.Action,
                    e.OccurredAt,
                }))
            .WithCrud<StyleAAuditCrud, StyleAAuditEntity>()
                .MapWritable(c => c.Action, e => e.Action)
                .MapWritable(c => c.Severity, e => e.Severity)
                .MapWritable(c => c.OccurredAt, e => e.OccurredAt)
                .MapWritable(c => c.IsSensitive, e => e.IsSensitive);
    }
}
