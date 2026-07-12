// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Representative typed Style B view fixtures for the M9 per-view JsonTypeInfo phase
// (spec source-generator-json-typeinfo, task 8.1; Decision Log D125/D126).
//
// These are REAL, minimal, VALID, partial, single-source typed Style B views. Because this assembly
// references Core AND the EF layer AND the source generator (as an analyzer), the ViewJsonContextGenerator
// emits INTO this assembly, per COVERED view, a `file sealed` <View>_VistaJsonContext.g.cs holding a
// reflection-free System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver (built via
// JsonMetadataServices, NOT the [JsonSerializable] attribute route) that provides the JsonTypeInfo for the
// view's TRow, ViewListResult<TRow>, PagedResult<TRow>, and — when writable — TCrud, plus a
// [ModuleInitializer] that registers it into a2n.Vista.Metadata.GeneratedJsonContextStore keyed by the
// view's runtime Name.
//
// NO DEVELOPER App_Json_Context. Unlike the Phase 4 HTTP-surface sample (which ships a developer-authored
// [JsonSerializable] App_Json_Context per view), this assembly declares NONE. Every DTO member below is an
// Emittable_Shape, so the ViewJsonContextGenerator classifies each view as COVERED (VISTA0050) and emits
// its per-view JsonTypeInfo context — proving a typed Style B app is AOT-clean for serialization WITHOUT
// any hand-authored context (R6.1, R6.3, and the "context is now optional" claim).
//
// COMPILE-ONCE, QUANTIFY-OVER-VALUES (design "The parity oracle" / "Cost control"). The master
// oracle-parity property test (task 8.2, Property 1) and the round-trip property test (task 8.3,
// Property 2) compile this fixture set ONCE, resolve each view's GENERATED context from the store, and
// compare its (de)serialization — over random DTO VALUES — against the reflection oracle. They never
// re-compile per iteration.
//
// The set is deliberately chosen to cover the serialization surface Properties 1/2 must exercise
// (mirroring the Phase 2 / Phase 4 fixtures):
//   * CatalogItemView   — a read-only SINGLE-KEY view (View<TRow>) whose TRow spans the emittable-shape
//                         spectrum: a scalar PK, a nullable value type, an enum, a collection, and a
//                         byte[] member (the AOT-critical read-DTO member shapes).
//   * GeoZoneView       — a read-only COMPOSITE-KEY view (View<TRow>): a two-field key (RegionId, ZoneCode)
//                         marked in declaration order, exercising a composite-key row DTO.
//   * SubscriptionView  — a WRITABLE view (View<TRow, TCrud>) whose TCrud is a RECORD with INIT-ONLY and
//                         REQUIRED members, exercising the R2.5 init/required construction path the
//                         generated JsonTypeInfo must round-trip.
//
// Every view is single-source, partial, has an implicit public parameterless constructor, and declares its
// primary key via per-field PrimaryKey() marks — the exact conditions the ViewJsonContextGenerator needs to
// emit a context + [ModuleInitializer] for it.

using System;
using System.Collections.Generic;
using a2n.Vista.Authoring;

namespace a2n.Vista.GeneratorJsonContextSample;

// =====================================================================================================
// Case 1 — READ-ONLY, SINGLE-KEY view. Its TRow exercises the full read-DTO Emittable_Shape spectrum:
// a scalar PK (int), a nullable value type (decimal?), an enum (CatalogItemStatus), a collection
// (List<string>), and a byte[] member. Every member is emittable, so the view is COVERED (VISTA0050) and
// its per-view JsonTypeInfo context is generated.
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

/// <summary>EF source entity for <see cref="CatalogItemView"/>; keyed by <see cref="ItemId"/>.</summary>
public sealed class CatalogItemEntity
{
    /// <summary>Primary key (EF infers it by convention).</summary>
    public int ItemId { get; set; }

    /// <summary>Display name — filterable/sortable/searchable by default.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional discount — a nullable value-type scalar.</summary>
    public decimal? Discount { get; set; }

    /// <summary>Availability state — an enum scalar.</summary>
    public CatalogItemStatus Status { get; set; }

    /// <summary>Free-form tags — a collection of an emittable element.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Thumbnail bytes — a <c>byte[]</c> member (System.Text.Json base64 default).</summary>
    public byte[] Thumbnail { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Projected (read) row for <see cref="CatalogItemView"/> spanning the emittable-shape spectrum: a scalar
/// PK, a nullable value type, an enum, a collection, and a <c>byte[]</c> member.
/// </summary>
public sealed class CatalogItemRow
{
    /// <summary>Primary key — a scalar member.</summary>
    public int ItemId { get; init; }

    /// <summary>Name — a string member.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional discount — a nullable value-type member.</summary>
    public decimal? Discount { get; init; }

    /// <summary>Availability — an enum member.</summary>
    public CatalogItemStatus Status { get; init; }

    /// <summary>Tags — a collection member (<see cref="List{T}"/> of an emittable element).</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>Thumbnail — a <c>byte[]</c> member.</summary>
    public byte[] Thumbnail { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// Read-only typed Style B view over <see cref="CatalogItemEntity"/> with a single integer primary key. It
/// is <c>partial</c>, single-source, has an implicit public parameterless constructor, and declares its PK
/// — the conditions the <c>ViewJsonContextGenerator</c> needs to emit a per-view <c>IJsonTypeInfoResolver</c>
/// (over <c>TRow</c>, <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>) and register it at
/// module load. No developer <c>App_Json_Context</c> is declared for it (R6.1).
/// </summary>
public partial class CatalogItemView : View<CatalogItemRow>
{
    /// <summary>Globally-unique view name; the key the generated context is stored under.</summary>
    public const string ViewName = "jsonctx-catalog-items";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<CatalogItemRow> builder) =>
        builder
            .Named(ViewName)
            .From<CatalogItemEntity>(s => new CatalogItemRow
            {
                ItemId = s.ItemId,
                Name = s.Name,
                Discount = s.Discount,
                Status = s.Status,
                Tags = s.Tags,
                Thumbnail = s.Thumbnail,
            })
            .Field(x => x.ItemId, f => f.PrimaryKey());
}

// =====================================================================================================
// Case 2 — READ-ONLY, COMPOSITE-KEY view. A two-field key (RegionId, ZoneCode) marked in declaration
// order, exercising a composite-key row DTO. Every member is emittable, so the view is COVERED.
// =====================================================================================================

/// <summary>EF source entity for <see cref="GeoZoneView"/>; keyed by (RegionId, ZoneCode).</summary>
public sealed class GeoZoneEntity
{
    /// <summary>First primary-key component.</summary>
    public int RegionId { get; set; }

    /// <summary>Second primary-key component.</summary>
    public string ZoneCode { get; set; } = string.Empty;

    /// <summary>Zone description — filterable/sortable/searchable.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether the zone is active — a boolean scalar.</summary>
    public bool IsActive { get; set; }
}

/// <summary>Projected (read) row for <see cref="GeoZoneView"/> exposing the composite key.</summary>
public sealed class GeoZoneRow
{
    /// <summary>First primary-key component.</summary>
    public int RegionId { get; init; }

    /// <summary>Second primary-key component.</summary>
    public string ZoneCode { get; init; } = string.Empty;

    /// <summary>Description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Active flag.</summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// Read-only typed Style B view over <see cref="GeoZoneEntity"/> with a two-field composite key
/// (RegionId, ZoneCode) marked in declaration order. Its per-view JsonTypeInfo context is generated with
/// no developer <c>App_Json_Context</c>.
/// </summary>
public partial class GeoZoneView : View<GeoZoneRow>
{
    /// <summary>Globally-unique view name; the key the generated context is stored under.</summary>
    public const string ViewName = "jsonctx-geo-zones";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<GeoZoneRow> builder) =>
        builder
            .Named(ViewName)
            .From<GeoZoneEntity>(s => new GeoZoneRow
            {
                RegionId = s.RegionId,
                ZoneCode = s.ZoneCode,
                Description = s.Description,
                IsActive = s.IsActive,
            })
            .Field(x => x.RegionId, f => f.PrimaryKey())
            .Field(x => x.ZoneCode, f => f.PrimaryKey());
}

// =====================================================================================================
// Case 3 — WRITABLE view whose TCrud is a RECORD with INIT-ONLY and REQUIRED members. This exercises the
// R2.5 construction path the generated JsonTypeInfo must round-trip: the DTO has a public parameterless
// ctor and init-only/required members, so the emitter builds instances through an object initializer
// (new T() { Required = …, Init = … }) rather than plain setters. The whitelist maps only safe non-key
// scalars (never the key), so the WriteMapperGenerator emits a mapper with no VISTA0030/31/32.
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

/// <summary>EF source entity for <see cref="SubscriptionView"/>; keyed by <see cref="SubscriptionId"/>.</summary>
public sealed class SubscriptionEntity
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

/// <summary>Projected (read) row for <see cref="SubscriptionView"/>.</summary>
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
/// Typed write contract for <see cref="SubscriptionView"/> — a <c>record</c> with a <c>required</c> member
/// (<see cref="PlanName"/>) and <c>init</c>-only members, so the generated <c>JsonTypeInfo</c> must
/// construct instances through the init/required object-initializer path (R2.5) and round-trip them.
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

/// <summary>
/// Writable typed Style B view over <see cref="SubscriptionEntity"/> declaring a non-key <c>MapWritable</c>
/// whitelist over safe scalars. The <c>ViewJsonContextGenerator</c> emits a per-view context covering
/// <c>TRow</c>, <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and the record
/// <see cref="SubscriptionCrud"/> — the writable Serializable_DTO_Set — with no developer
/// <c>App_Json_Context</c> (R6.1, R6.3).
/// </summary>
public partial class SubscriptionView : View<SubscriptionRow, SubscriptionCrud>
{
    /// <summary>Globally-unique view name; the key the generated context is stored under.</summary>
    public const string ViewName = "jsonctx-subscriptions";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<SubscriptionRow, SubscriptionCrud> builder)
    {
        builder
            .Named(ViewName)
            .From<SubscriptionEntity>(s => new SubscriptionRow
            {
                SubscriptionId = s.SubscriptionId,
                PlanName = s.PlanName,
                SeatCount = s.SeatCount,
                RenewsOn = s.RenewsOn,
                Tier = s.Tier,
            })
            .Field(x => x.SubscriptionId, f => f.PrimaryKey());

        builder
            .CrudOn<SubscriptionEntity>()
            .MapWritable(c => c.PlanName, e => e.PlanName)
            .MapWritable(c => c.SeatCount, e => e.SeatCount)
            .MapWritable(c => c.RenewsOn, e => e.RenewsOn)
            .MapWritable(c => c.Tier, e => e.Tier);
    }
}
