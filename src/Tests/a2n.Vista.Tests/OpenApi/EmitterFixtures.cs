// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Compile-once representative fixtures for the OpenAPI emitter property/example tests
// (spec openapi-emitter, task 8.1; Requirements 1.1, 4.1, 14.1). These MIRROR the D125 per-view
// JsonTypeInfo fixtures (a2n.Vista.GeneratorJsonContextSample) so the emitter's schema/wire-parity
// oracle exercises the same DTO shape spectrum the serializer already round-trips:
//
//   * CatalogItem  — a READ-ONLY SINGLE-KEY view whose TRow spans the read-DTO shape spectrum: a scalar
//                    PK (int), a plain scalar (string), a nullable value type (int?), a nullable reference
//                    (string?), an enum, a collection (List<string>), and a byte[] member.
//   * GeoZone      — a READ-ONLY COMPOSITE-KEY view: a two-field key (RegionId, ZoneCode).
//   * Subscription — a WRITABLE view whose TCrud is a RECORD with a `required` member and `init`-only
//                    members, plus a source entity carrying a concurrency token (Version) so the
//                    token-gated 428/409 + write-ETag scenarios (R3.5, R6.4) can be exercised by later
//                    tasks via the write-facet registry.
//
// The emitter consumes ViewMetadata (via IViewRegistry) + the serialization seam options + the optional
// WriteFacetRegistry — never the Style B View<TRow> authoring types — so these fixtures build ViewMetadata
// directly (the same construction OpenApiDocumentBuilderTests / OpenApiSecurityAndErrorsTests use), keyed
// by real CLR row/CRUD types the RUC DtoSchemaGenerator reflects over. They are COMPILE-ONCE:
// schema/wire-parity properties (tasks 8.4/8.5) quantify over random VALUES of these fixed types for cost
// control, never over generated types.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;

namespace a2n.Vista.Tests;

/// <summary>
/// The compile-once representative views the OpenAPI emitter property/example tests share (spec
/// openapi-emitter, task 8.1). Exposes the concrete DTO/CRUD types, each view's <see cref="ViewMetadata"/>,
/// an <see cref="IViewRegistry"/> over all three, a matching <see cref="WriteFacetRegistry"/> that gives the
/// writable view a concurrency token, and the serialization seam options (both a local mirror and the real
/// <see cref="VistaJson.Options"/>) so parity tests can validate schemas against the true wire shape.
/// </summary>
public static class EmitterFixtures
{
    // ===== Case 1 — READ-ONLY, SINGLE-KEY row spanning the read-DTO shape spectrum =====================

    /// <summary>Availability state for <see cref="CatalogItemRow"/> — an enum member shape.</summary>
    public enum CatalogItemStatus
    {
        /// <summary>Not yet published.</summary>
        Draft = 0,

        /// <summary>Available.</summary>
        Active = 1,

        /// <summary>No longer offered.</summary>
        Discontinued = 2,
    }

    /// <summary>
    /// Projected read row for the read-only single-key view: a scalar PK, a plain scalar, a nullable value
    /// type, a nullable reference, an enum, a collection, and a <c>byte[]</c> — the full read-DTO shape
    /// spectrum the schema/wire-parity oracle must cover (R4.1–R4.4).
    /// </summary>
    public sealed class CatalogItemRow
    {
        /// <summary>Primary key — a scalar member.</summary>
        public int ItemId { get; init; }

        /// <summary>Display name — a plain string scalar.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Optional rating count — a nullable value type.</summary>
        public int? RatingCount { get; init; }

        /// <summary>Optional nickname — a nullable reference type.</summary>
        public string? Nickname { get; init; }

        /// <summary>Availability — an enum member (string on the wire via <see cref="JsonStringEnumConverter"/>).</summary>
        public CatalogItemStatus Status { get; init; }

        /// <summary>Tags — a collection member.</summary>
        public List<string> Tags { get; init; } = new();

        /// <summary>Thumbnail — a <c>byte[]</c> member (base64 string on the wire).</summary>
        public byte[] Thumbnail { get; init; } = Array.Empty<byte>();
    }

    // ===== Case 2 — READ-ONLY, COMPOSITE-KEY row =======================================================

    /// <summary>Projected read row for the read-only composite-key view (RegionId, ZoneCode).</summary>
    public sealed class GeoZoneRow
    {
        /// <summary>First primary-key component.</summary>
        public int RegionId { get; init; }

        /// <summary>Second primary-key component.</summary>
        public string ZoneCode { get; init; } = string.Empty;

        /// <summary>Description.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>Active flag — a boolean scalar.</summary>
        public bool IsActive { get; init; }
    }

    // ===== Case 3 — WRITABLE view: TCrud is a record with required + init-only members =================

    /// <summary>Plan tier for <see cref="SubscriptionRow"/>/<see cref="SubscriptionCrud"/> — an enum.</summary>
    public enum SubscriptionTier
    {
        /// <summary>Free tier.</summary>
        Free = 0,

        /// <summary>Standard tier.</summary>
        Standard = 1,

        /// <summary>Premium tier.</summary>
        Premium = 2,
    }

    /// <summary>Projected read row for the writable view.</summary>
    public sealed class SubscriptionRow
    {
        /// <summary>Primary key.</summary>
        public int SubscriptionId { get; init; }

        /// <summary>Plan name.</summary>
        public string PlanName { get; init; } = string.Empty;

        /// <summary>Seat count.</summary>
        public int SeatCount { get; init; }

        /// <summary>Optional renewal date — a nullable value type.</summary>
        public DateTime? RenewsOn { get; init; }

        /// <summary>Plan tier — an enum.</summary>
        public SubscriptionTier Tier { get; init; }
    }

    /// <summary>
    /// Typed write contract for the writable view — a <c>record</c> with a <c>required</c> member
    /// (<see cref="PlanName"/>) and <c>init</c>-only members, exercising the init/required construction the
    /// emitted <c>TCrud</c> schema must describe.
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
    /// The underlying entity the writable view maps to. Carries a <see cref="Version"/> concurrency token
    /// selected by <see cref="WriteFacets"/> so the emitter can document the token-gated 428/409 responses
    /// and the write-facet ETag success header (R3.5, R6.4).
    /// </summary>
    public sealed class SubscriptionEntity
    {
        /// <summary>Primary key — never assigned by the write path.</summary>
        public int SubscriptionId { get; init; }

        /// <summary>Plan name.</summary>
        public string PlanName { get; init; } = string.Empty;

        /// <summary>Seat count.</summary>
        public int SeatCount { get; init; }

        /// <summary>Optimistic-concurrency token.</summary>
        public int Version { get; init; }
    }

    // ===== View names / routes (globally unique, identifier-safe, D101/D103) ===========================

    /// <summary>The read-only single-key view name.</summary>
    public const string CatalogItemName = "catalogItems";

    /// <summary>The read-only composite-key view name.</summary>
    public const string GeoZoneName = "geoZones";

    /// <summary>The writable view name.</summary>
    public const string SubscriptionName = "subscriptions";

    /// <summary>The read-only single-key view route.</summary>
    public const string CatalogItemRoute = "/api/views/" + CatalogItemName;

    /// <summary>The read-only composite-key view route.</summary>
    public const string GeoZoneRoute = "/api/views/" + GeoZoneName;

    /// <summary>The writable view route.</summary>
    public const string SubscriptionRoute = "/api/views/" + SubscriptionName;

    // ===== Serialization seam =========================================================================

    /// <summary>
    /// A local mirror of the serialization seam configuration (web defaults + <see cref="JsonStringEnumConverter"/>),
    /// matching the <c>SeamOptions()</c> pattern used by the existing emitter example tests. A fresh instance
    /// so tests never mutate a shared static.
    /// </summary>
    /// <returns>A <see cref="JsonSerializerOptions"/> mirroring the seam's naming/enum configuration.</returns>
    public static JsonSerializerOptions SeamOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>
    /// The REAL Vista serialization seam options (<see cref="VistaJson.Options"/>) — the authoritative
    /// schema/wire-parity oracle (D124/D126). Prefer this in parity tests so a naming-policy or converter
    /// change on the true seam is caught; read-only, never mutated.
    /// </summary>
    public static JsonSerializerOptions Seam => VistaJson.Options;

    // ===== ViewMetadata factories ======================================================================

    /// <summary>
    /// The read-only single-key view over <see cref="CatalogItemRow"/> (single key <c>ItemId</c>). The
    /// ctor is positional (Name, Route, QueryType, CrudType, CrudEntityType, Fields, Authorization, Limits,
    /// IsReadOnly) with <see cref="ViewMetadata.KeyFields"/> set via the init accessor.
    /// </summary>
    /// <returns>A fresh <see cref="ViewMetadata"/> for the read-only single-key view.</returns>
    public static ViewMetadata CatalogItemView() => new(
        Name: CatalogItemName,
        Route: CatalogItemRoute,
        QueryType: typeof(CatalogItemRow),
        CrudType: null,
        CrudEntityType: null,
        Fields: new[]
        {
            FieldMetadata.Create("ItemId", typeof(int), isPrimaryKey: true, allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("Name", typeof(string), allowedOperators: FilterOperator.Contains),
            FieldMetadata.Create("Status", typeof(CatalogItemStatus), allowedOperators: FilterOperator.Equals),
        },
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: true)
    {
        KeyFields = new[] { "ItemId" },
    };

    /// <summary>
    /// The read-only composite-key view over <see cref="GeoZoneRow"/> (two-part key
    /// <c>RegionId</c>, <c>ZoneCode</c>).
    /// </summary>
    /// <returns>A fresh <see cref="ViewMetadata"/> for the read-only composite-key view.</returns>
    public static ViewMetadata GeoZoneView() => new(
        Name: GeoZoneName,
        Route: GeoZoneRoute,
        QueryType: typeof(GeoZoneRow),
        CrudType: null,
        CrudEntityType: null,
        Fields: new[]
        {
            FieldMetadata.Create("RegionId", typeof(int), isPrimaryKey: true, allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("ZoneCode", typeof(string), isPrimaryKey: true, allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("Description", typeof(string), allowedOperators: FilterOperator.Contains),
        },
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: true)
    {
        KeyFields = new[] { "RegionId", "ZoneCode" },
    };

    /// <summary>
    /// The writable view over <see cref="SubscriptionRow"/> whose <c>TCrud</c> is the record
    /// <see cref="SubscriptionCrud"/> and whose entity is <see cref="SubscriptionEntity"/>.
    /// </summary>
    /// <returns>A fresh <see cref="ViewMetadata"/> for the writable view.</returns>
    public static ViewMetadata SubscriptionView() => new(
        Name: SubscriptionName,
        Route: SubscriptionRoute,
        QueryType: typeof(SubscriptionRow),
        CrudType: typeof(SubscriptionCrud),
        CrudEntityType: typeof(SubscriptionEntity),
        Fields: new[]
        {
            FieldMetadata.Create("SubscriptionId", typeof(int), isPrimaryKey: true, allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("PlanName", typeof(string), isWritable: true, allowedOperators: FilterOperator.Contains),
            FieldMetadata.Create("SeatCount", typeof(int), isWritable: true, allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("Tier", typeof(SubscriptionTier), isWritable: true, allowedOperators: FilterOperator.Equals),
        },
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: false)
    {
        KeyFields = new[] { "SubscriptionId" },
    };

    // ===== Registry accessors ==========================================================================

    /// <summary>
    /// An <see cref="IViewRegistry"/> containing the three representative views (globally-unique names and
    /// routes, D101/D103): the read-only single-key, read-only composite-key, and writable views.
    /// </summary>
    /// <returns>A populated <see cref="ViewRegistry"/>.</returns>
    public static IViewRegistry Registry()
    {
        var registry = new ViewRegistry();
        registry.Add(CatalogItemView());
        registry.Add(GeoZoneView());
        registry.Add(SubscriptionView());
        return registry;
    }

    /// <summary>
    /// A <see cref="WriteFacetRegistry"/> in which the writable <see cref="SubscriptionName"/> view declares
    /// an optimistic-concurrency token (<see cref="SubscriptionEntity.Version"/>). The token lives only on
    /// the write-facet registry (<see cref="CrudFacetDefinition.ConcurrencyToken"/>) — <see cref="ViewMetadata"/>
    /// carries no token concept — so supplying this to the builder is what enables the token-gated 428/409
    /// responses and the write ETag success header for the writable view (R3.5, R6.4).
    /// </summary>
    /// <returns>A write-facet registry with a token for the writable view.</returns>
    public static WriteFacetRegistry WriteFacets()
    {
        var registry = new WriteFacetRegistry();
        Expression<Func<SubscriptionEntity, int>> token = e => e.Version;
        registry.Register(SubscriptionName, new CrudFacetDefinition(
            CrudType: typeof(SubscriptionCrud),
            EntityType: typeof(SubscriptionEntity),
            WritableFields: Array.Empty<WritableFieldMapping>(),
            ConcurrencyToken: token,
            AllowsBulk: false));
        return registry;
    }
}
