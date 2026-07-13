// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Representative Style A (central-template) view fixtures for the M9 Style A coverage phase
// (spec style-a-coverage, task 8.1; Decision Log D129/D130).
//
// These are REAL, minimal, VALID Style A views: AddView<TRow>(name, projection) call sites inside a single
// ViewTemplate<StyleASampleDbContext>.Configure override (the DynData-style "central template" authoring
// experience), authored against a genuine EF DbContext — exactly like the Northwind
// ViewTemplate<NorthwindDbContext> example. Because this assembly references Core AND the EF layer AND the
// source generator (as an analyzer), the fifth incremental generator (StyleAShapeGenerator, D129) emits INTO
// this assembly, per COVERED view:
//   * a `file static` <Template>_<View>_VistaAccessors.g.cs export accessor map (for a named-TRow view)
//     registered into a2n.Vista.Metadata.ViewAccessorRegistry (D117) at module load, and
//   * a `file sealed` <Template>_<View>_VistaJsonContext.g.cs holding a reflection-free
//     System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver (built via JsonMetadataServices, NOT the
//     [JsonSerializable] attribute route) registered into a2n.Vista.Metadata.GeneratedJsonContextStore (D125)
//     at module load,
// each keyed by the CONSTANT AddView name (the D129 difference from D125's `new View().Name` — a Style A view
// is an AddView call site, not a class, so there is nothing to instantiate).
//
// NO DEVELOPER App_Json_Context. Like the Phase 5 JsonTypeInfo sample, this assembly declares NONE. The
// covered views prove the nameable Style A slice is AOT-clean for serialization/export WITHOUT any
// hand-authored context; the anonymous-read view demonstrates the permanent by-design RUC boundary
// (D96/D130): its read row is unnameable in generated source, so ONLY its (named) TCrud write model is
// covered — the D96 asymmetry WITHIN one view (VISTA0061 for the read side, VISTA0060 for the write side).
//
// COMPILE-ONCE, QUANTIFY-OVER-VALUES (design "The parity oracle" / "Cost control"). The master oracle-parity
// property test (task 8.2, Property 1), the round-trip property test (task 8.3, Property 2), and the
// export-accessor value-parity property test (task 8.4, Property 3) compile this fixture set ONCE, resolve
// each covered view's GENERATED context / accessor map from the stores by its constant view name, and compare
// its (de)serialization / field read — over random DTO VALUES — against the reflection oracle. They never
// re-compile per iteration. The AOT probe (task 9.1) drives the same fixtures to prove the covered slice is
// IL2026/IL3050-clean while the anonymous read row stays RUC.
//
// The three views cover the serialization/export surface Properties 1/2/3 must exercise:
//   * CatalogItems (stylea-catalog-items)  — a READ-ONLY, NAMED-TRow view whose TRow spans the emittable
//                                            shape spectrum: a scalar (int), a nullable value type (int?),
//                                            an enum, a collection (IReadOnlyList<string>), and a byte[]
//                                            member. Covered: export accessors + read-DTO JsonTypeInfo.
//   * Subscriptions (stylea-subscriptions) — a WRITABLE, NAMED-TRow view whose TCrud is a RECORD with a
//                                            REQUIRED member and INIT-ONLY members (the R3.4 parameterized/
//                                            init construction path). Covered: export accessors + read-DTO
//                                            JsonTypeInfo + TCrud JsonTypeInfo.
//   * AuditEntries (stylea-audit-entries)  — a WRITABLE, ANONYMOUS-TRow view with a named TCrud (the D96
//                                            asymmetry). Covered: TCrud JsonTypeInfo ONLY; the anonymous read
//                                            row stays on the reflection path by design (VISTA0061).
//
// Every AddView name is a compile-time constant (a `const string` on the template) so the generator can key
// each artifact statically (a non-constant name would be VISTA0062). Every DTO member below is an
// Emittable_Shape, so each nameable DTO is COVERED (no VISTA0063).

using System;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Authoring;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.GeneratorStyleASample;

// =====================================================================================================
// The minimal EF data source the Style A projections are expressed against. Style A views are AddView
// call sites over a ViewTemplate<TDbContext>; TDbContext is a genuine EF DbContext (mirroring
// ViewTemplate<NorthwindDbContext>). The DbContext/DbSet types arrive transitively from
// Microsoft.EntityFrameworkCore (referenced via the EF layer). It is never instantiated at runtime by the
// property tests (they resolve the GENERATED contexts/accessors from the Core stores and quantify over DTO
// values); it exists only so the projections compile and the generator can analyze the AddView call sites.
// =====================================================================================================

/// <summary>
/// Minimal EF Core context exposing the three source entities the Style A template projects from. Backed by
/// no real provider — it is a compile-time source for the fixtures' projections, never opened at runtime.
/// </summary>
public class StyleASampleDbContext : DbContext
{
    /// <summary>Creates the context with the supplied options (a fixture never configures a provider).</summary>
    public StyleASampleDbContext(DbContextOptions<StyleASampleDbContext> options)
        : base(options)
    {
    }

    /// <summary>Source set for the read-only, named-row <c>stylea-catalog-items</c> view.</summary>
    public DbSet<CatalogItemEntity> CatalogItems => Set<CatalogItemEntity>();

    /// <summary>Source set for the writable, named-row <c>stylea-subscriptions</c> view.</summary>
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();

    /// <summary>Source set for the writable, anonymous-row <c>stylea-audit-entries</c> view.</summary>
    public DbSet<AuditEntryEntity> AuditEntries => Set<AuditEntryEntity>();
}

// =====================================================================================================
// Case 1 — READ-ONLY, NAMED-TRow view. Its TRow (CatalogItemRow) exercises the full read-DTO
// Emittable_Shape spectrum: a scalar (int), a nullable value type (int?), an enum, a collection
// (IReadOnlyList<string>), and a byte[] member. Every member is emittable, so the view is COVERED
// (VISTA0060): export accessors + read-DTO JsonTypeInfo are generated. No WithCrud → read-only.
// =====================================================================================================

/// <summary>Availability state for <see cref="CatalogItemRow"/> — an enum member shape.</summary>
public enum CatalogItemStatus
{
    /// <summary>Not yet published.</summary>
    Draft = 0,

    /// <summary>Available for sale.</summary>
    Active = 1,

    /// <summary>No longer offered.</summary>
    Discontinued = 2,
}

/// <summary>EF source entity for the <c>stylea-catalog-items</c> view; keyed by <see cref="ItemId"/>.</summary>
public class CatalogItemEntity
{
    /// <summary>Primary key.</summary>
    public int ItemId { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional reorder threshold — a nullable value-type scalar.</summary>
    public int? ReorderLevel { get; set; }

    /// <summary>Availability state — an enum scalar.</summary>
    public CatalogItemStatus Status { get; set; }

    /// <summary>Free-form tags — a collection of an emittable element.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Thumbnail bytes — a <c>byte[]</c> member (System.Text.Json base64 default).</summary>
    public byte[] Thumbnail { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Projected (read) row for the <c>stylea-catalog-items</c> view spanning the emittable-shape spectrum: a
/// scalar (<see cref="ItemId"/>), a nullable value type (<see cref="ReorderLevel"/>), an enum
/// (<see cref="Status"/>), a collection (<see cref="Tags"/>), and a <c>byte[]</c> (<see cref="Thumbnail"/>).
/// A NAMED type, so it is nameable in generated source — the read-side coverage precondition (D129).
/// </summary>
public sealed class CatalogItemRow
{
    /// <summary>Primary key — a scalar member.</summary>
    public int ItemId { get; init; }

    /// <summary>Name — a string member.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional reorder threshold — a nullable value-type member.</summary>
    public int? ReorderLevel { get; init; }

    /// <summary>Availability — an enum member.</summary>
    public CatalogItemStatus Status { get; init; }

    /// <summary>Tags — a collection member (<see cref="IReadOnlyList{T}"/> of an emittable element).</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>Thumbnail — a <c>byte[]</c> member.</summary>
    public byte[] Thumbnail { get; init; } = Array.Empty<byte>();
}

// =====================================================================================================
// Case 2 — WRITABLE, NAMED-TRow view. TRow (SubscriptionRow) is named (read-side covered), and its TCrud
// (SubscriptionCrud) is a RECORD with a REQUIRED member and INIT-ONLY members, exercising the R3.4
// construction path the generated JsonTypeInfo must round-trip (the DTO is built through its init/required
// object-initializer path, not plain setters). COVERED: export accessors + read-DTO JsonTypeInfo +
// TCrud JsonTypeInfo (VISTA0060 naming all three).
// =====================================================================================================

/// <summary>Plan tier for <see cref="SubscriptionCrud"/> / <see cref="SubscriptionRow"/> — an enum.</summary>
public enum SubscriptionTier
{
    /// <summary>Free tier.</summary>
    Free = 0,

    /// <summary>Paid standard tier.</summary>
    Standard = 1,

    /// <summary>Paid premium tier.</summary>
    Premium = 2,
}

/// <summary>EF source entity for the <c>stylea-subscriptions</c> view; keyed by <see cref="SubscriptionId"/>.</summary>
public class SubscriptionEntity
{
    /// <summary>Primary key — never assigned by the write path.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Whitelisted string scalar.</summary>
    public string PlanName { get; set; } = string.Empty;

    /// <summary>Whitelisted value-type scalar.</summary>
    public int SeatCount { get; set; }

    /// <summary>Whitelisted nullable value-type scalar.</summary>
    public DateTime? RenewsOn { get; set; }

    /// <summary>Whitelisted enum scalar.</summary>
    public SubscriptionTier Tier { get; set; }
}

/// <summary>Projected (read) row for the <c>stylea-subscriptions</c> view — a named read DTO.</summary>
public sealed class SubscriptionRow
{
    /// <summary>Primary key.</summary>
    public int SubscriptionId { get; init; }

    /// <summary>Plan name.</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Seat count.</summary>
    public int SeatCount { get; init; }

    /// <summary>Optional renewal date.</summary>
    public DateTime? RenewsOn { get; init; }

    /// <summary>Plan tier.</summary>
    public SubscriptionTier Tier { get; init; }
}

/// <summary>
/// Typed write contract for the <c>stylea-subscriptions</c> view — a <c>record</c> with a <c>required</c>
/// member (<see cref="PlanName"/>) and <c>init</c>-only members, so the generated <c>JsonTypeInfo</c> must
/// construct instances through the init/required object-initializer path (R3.4) and round-trip them. Always
/// a named type (the authoring surface forbids an anonymous write model, D38).
/// </summary>
public sealed record SubscriptionCrud
{
    /// <summary>New plan name — a <c>required</c> member.</summary>
    public required string PlanName { get; init; }

    /// <summary>New seat count — an <c>init</c>-only value-type member.</summary>
    public int SeatCount { get; init; }

    /// <summary>New renewal date — an <c>init</c>-only nullable value-type member.</summary>
    public DateTime? RenewsOn { get; init; }

    /// <summary>New plan tier — an <c>init</c>-only enum member.</summary>
    public SubscriptionTier Tier { get; init; }
}

// =====================================================================================================
// Case 3 — WRITABLE, ANONYMOUS-TRow view (the D96 asymmetry). The read projection is an ANONYMOUS type,
// which has no source-writable name, so its read serialization + export stay on the reflection path
// permanently by design (VISTA0061). Its TCrud (AuditEntryCrud, a named record) is unaffected and IS
// covered — so the write body binds AOT-clean while the read row does not. COVERED: TCrud JsonTypeInfo ONLY
// (VISTA0060 write side + VISTA0061 read side, in one view).
// =====================================================================================================

/// <summary>EF source entity for the <c>stylea-audit-entries</c> view; keyed by <see cref="EntryId"/>.</summary>
public class AuditEntryEntity
{
    /// <summary>Primary key.</summary>
    public int EntryId { get; set; }

    /// <summary>Whitelisted string scalar.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Whitelisted value-type scalar.</summary>
    public int Severity { get; set; }

    /// <summary>Whitelisted nullable value-type scalar.</summary>
    public DateTime? OccurredAt { get; set; }

    /// <summary>Whitelisted boolean scalar.</summary>
    public bool IsSensitive { get; set; }
}

/// <summary>
/// Typed write contract for the <c>stylea-audit-entries</c> view — a named <c>record</c> whose members are
/// all Emittable_Shape (a <c>required</c> string, an <c>init</c>-only scalar, an <c>init</c>-only nullable
/// value type, and an <c>init</c>-only boolean). It is covered for <c>TCrud</c> <c>JsonTypeInfo</c> even
/// though the view's read row is anonymous (R4.2) — the whole point of this fixture.
/// </summary>
public sealed record AuditEntryCrud
{
    /// <summary>Action performed — a <c>required</c> member.</summary>
    public required string Action { get; init; }

    /// <summary>Severity level — an <c>init</c>-only value-type member.</summary>
    public int Severity { get; init; }

    /// <summary>When it occurred — an <c>init</c>-only nullable value-type member.</summary>
    public DateTime? OccurredAt { get; init; }

    /// <summary>Whether the entry is sensitive — an <c>init</c>-only boolean member.</summary>
    public bool IsSensitive { get; init; }
}

// =====================================================================================================
// The single Style A central template registering the three representative views. Every AddView `name`
// argument is a compile-time constant (the `const string` fields below) so the StyleAShapeGenerator can
// key each generated artifact statically. The generator recognizes these AddView call sites because this
// type derives ViewTemplate<StyleASampleDbContext> and the calls resolve to
// IViewTemplateBuilder<TDbContext>.AddView<TRow> (both by fully-qualified name).
// =====================================================================================================

/// <summary>
/// Central-template (Style A / Gaya A) authoring for the Style A coverage fixtures. Registers the three
/// representative views the style-a-coverage property tests (8.2/8.3/8.4) and the AOT probe (9.1) resolve
/// from the generated stores by their constant names.
/// </summary>
public class GeneratorStyleASampleViews : ViewTemplate<StyleASampleDbContext>
{
    /// <summary>
    /// View name for the read-only, named-row catalog view — the key its generated export accessor map and
    /// read-DTO <c>JsonTypeInfo</c> context are registered under.
    /// </summary>
    public const string CatalogItemsViewName = "stylea-catalog-items";

    /// <summary>
    /// View name for the writable, named-row subscription view — the key its generated accessor map and its
    /// read-DTO + <c>TCrud</c> <c>JsonTypeInfo</c> context are registered under.
    /// </summary>
    public const string SubscriptionsViewName = "stylea-subscriptions";

    /// <summary>
    /// View name for the writable, anonymous-row audit view — the key its generated <c>TCrud</c>-only
    /// <c>JsonTypeInfo</c> context is registered under (its read row is anonymous, so no read-side artifact
    /// is generated — the D96 asymmetry).
    /// </summary>
    public const string AuditEntriesViewName = "stylea-audit-entries";

    /// <inheritdoc />
    protected override void Configure(IViewTemplateBuilder<StyleASampleDbContext> views)
    {
        // Case 1 — READ-ONLY, NAMED-TRow (CatalogItemRow). Projects into a named row DTO spanning the
        // emittable-shape spectrum. Named TRow + constant name → export accessors + read-DTO JsonTypeInfo
        // generated (VISTA0060). No WithCrud → read-only (List + Detail by ItemId).
        views.AddView(CatalogItemsViewName, (db, sp) =>
                db.CatalogItems.Select(e => new CatalogItemRow
                {
                    ItemId = e.ItemId,
                    Name = e.Name,
                    ReorderLevel = e.ReorderLevel,
                    Status = e.Status,
                    Tags = e.Tags,
                    Thumbnail = e.Thumbnail,
                }))
            .Field(x => x.ItemId, f => f.PrimaryKey());

        // Case 2 — WRITABLE, NAMED-TRow (SubscriptionRow) with a record TCrud (SubscriptionCrud) covering
        // required + init-only members. Named TRow + named, emittable TCrud + constant name → export
        // accessors + read-DTO JsonTypeInfo + TCrud JsonTypeInfo generated (VISTA0060 naming all three).
        views.AddView(SubscriptionsViewName, (db, sp) =>
                db.Subscriptions.Select(e => new SubscriptionRow
                {
                    SubscriptionId = e.SubscriptionId,
                    PlanName = e.PlanName,
                    SeatCount = e.SeatCount,
                    RenewsOn = e.RenewsOn,
                    Tier = e.Tier,
                }))
            .Field(x => x.SubscriptionId, f => f.PrimaryKey())
            .WithCrud<SubscriptionCrud, SubscriptionEntity>()
                .MapWritable(c => c.PlanName, e => e.PlanName)
                .MapWritable(c => c.SeatCount, e => e.SeatCount)
                .MapWritable(c => c.RenewsOn, e => e.RenewsOn)
                .MapWritable(c => c.Tier, e => e.Tier);

        // Case 3 — WRITABLE, ANONYMOUS-TRow (the D96 asymmetry) with a named TCrud (AuditEntryCrud). The
        // read projection is an ANONYMOUS type (unnameable in generated source), so its read side stays on
        // the reflection path by design (VISTA0061); the named, emittable TCrud is still covered
        // (VISTA0060 write side). Result: TCrud JsonTypeInfo ONLY — the write body binds AOT-clean while the
        // read row does not.
        views.AddView(AuditEntriesViewName, (db, sp) =>
                db.AuditEntries.Select(e => new
                {
                    e.EntryId,
                    e.Action,
                    e.OccurredAt,
                }))
            .WithCrud<AuditEntryCrud, AuditEntryEntity>()
                .MapWritable(c => c.Action, e => e.Action)
                .MapWritable(c => c.Severity, e => e.Severity)
                .MapWritable(c => c.OccurredAt, e => e.OccurredAt)
                .MapWritable(c => c.IsSensitive, e => e.IsSensitive);
    }
}
