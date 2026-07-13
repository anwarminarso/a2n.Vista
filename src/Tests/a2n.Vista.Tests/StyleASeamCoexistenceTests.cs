// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Export;
using a2n.Vista.GeneratorStyleASample;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Unit examples for the coexistence and non-regression contract of the Style A (anonymous) coverage phase
/// (spec style-a-coverage, task 6.3; Decision Log D129/D130; Requirements 10.2, 10.3, 10.4). They assert
/// that adding covered Style A entries to the existing Core stores (<see cref="GeneratedJsonContextStore"/>,
/// <see cref="ViewAccessorRegistry"/>) changes nothing observable about the seam and export path except that
/// a covered Style A DTO/field now resolves from a generated artifact instead of reflection:
/// <list type="number">
///   <item><description>a covered Style A read DTO (<see cref="CatalogItemRow"/>) and the always-nameable
///   write models (<see cref="SubscriptionCrud"/>, <see cref="AuditEntryCrud"/>) resolve from a
///   <c>Generated_View_Context</c> — not the reflection fallback — <b>whether or not</b> a developer
///   <c>App_Json_Context</c> is registered, and the JSON is byte-for-byte identical either way and equal to
///   the reflection oracle (R10.2);</description></item>
///   <item><description>the <b>anonymous</b> audit read row — the type that stays RUC by design (D96/D130)
///   — is covered by no chained context and still (de)serializes through the reflection fallback, exactly
///   as before this feature (R10.3);</description></item>
///   <item><description>opting the reflection fallback out removes the reflection branch — a covered Style A
///   DTO still resolves from its generated context while the uncovered anonymous row no longer resolves at
///   all (R10.3, opt-out preserved);</description></item>
///   <item><description>the shipped <see cref="VistaStaticJsonContext"/> keeps precedence for the fixed
///   envelope/response types even with the Style A generated contexts chained (R10.4);</description></item>
///   <item><description>a covered named-<c>TRow</c> view's export field read goes through the generated
///   accessor registered in <see cref="ViewAccessorRegistry"/> (cast + member read), not reflection, while
///   the anonymous-row view has no generated accessor and stays on the reflection read (R10.3).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared-static isolation.</b> <see cref="VistaJson.Options"/> is a process-wide static whose resolver
/// chain freezes on first use, and <see cref="VistaJson.DisableReflectionFallback"/> mutates it
/// irreversibly. Mutating it here would corrupt the shared static every other test (and the sibling
/// seam-resolution property test, task 6.2) depends on. Each example therefore builds a <b>fresh</b>
/// <see cref="JsonSerializerOptions"/> that mirrors the seam chain exactly — the same construction
/// <see cref="VistaJson"/> performs (<see cref="VistaStaticJsonContext"/> first, then the generated per-view
/// contexts drained from <see cref="GeneratedJsonContextStore"/>, then any developer context, then the
/// opt-out-able reflection fallback) — proving the behavior without touching global state.
/// </para>
/// <para>
/// <b>Covered fixtures.</b> The covered Style A views come from the referenced
/// <c>a2n.Vista.GeneratorStyleASample</c> assembly (task 8.1), whose <c>[ModuleInitializer]</c>s register
/// the generated per-view contexts and accessor maps into the Core stores at module load, keyed by the
/// <b>constant</b> <c>AddView</c> name (the D129 difference from Style B's <c>new View().Name</c> keying).
/// The static constructor forces those module initializers to run so the stores are populated before any
/// example reads them — deterministic whether this class runs in isolation or as part of the full suite. No
/// new fixtures/contexts are registered into the shared stores here (the developer context below is only
/// added to a local chain), so there is no first-wins store collision with the sibling seam tasks.
/// </para>
/// </remarks>
public sealed class StyleASeamCoexistenceTests
{
    // The constant AddView names the generated Style A artifacts are keyed under (D129).
    private const string CatalogItemsView = GeneratorStyleASampleViews.CatalogItemsViewName;   // "stylea-catalog-items"
    private const string SubscriptionsView = GeneratorStyleASampleViews.SubscriptionsViewName; // "stylea-subscriptions"
    private const string AuditEntriesView = GeneratorStyleASampleViews.AuditEntriesViewName;   // "stylea-audit-entries"

    static StyleASeamCoexistenceTests()
    {
        // Force the fixture assembly's [ModuleInitializer]s to run (they register the generated Style A
        // per-view contexts into GeneratedJsonContextStore and the export accessor maps into
        // ViewAccessorRegistry). Referencing a type via typeof alone does not guarantee the module .cctor
        // has run, so run it explicitly — mirroring JsonContextLayeringGuardTests.
        RuntimeHelpers.RunModuleConstructor(typeof(CatalogItemRow).Assembly.ManifestModule.ModuleHandle);
    }

    // -- R10.2: a covered read DTO + its envelopes resolve from the generated context (developer absent). --

    // Feature: style-a-coverage — Requirement 10.2 (a covered Style A read DTO resolves from a
    // Generated_View_Context, not the reflection fallback, with no developer App_Json_Context present).
    [Test]
    public async Task CoveredReadDtoAndEnvelopes_ResolveFromGeneratedContext_DeveloperAbsent()
    {
        var row = FullyPopulatedCatalogRow();

        // The shipped envelope context covers no per-view DTO — so if a generated + static chain resolves
        // CatalogItemRow, the resolver that served it can only be a Generated_View_Context.
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(CatalogItemRow))).IsNull();

        var seam = BuildSeam(includeGenerated: true, developer: null, includeReflection: false);

        // A generated Style A context covers the read DTO and its List/Paged envelopes over TRow.
        await Assert.That(AnyGeneratedContextCovers(typeof(CatalogItemRow), seam)).IsTrue();
        await Assert.That(AnyGeneratedContextCovers(typeof(ViewListResult<CatalogItemRow>), seam)).IsTrue();
        await Assert.That(AnyGeneratedContextCovers(typeof(PagedResult<CatalogItemRow>), seam)).IsTrue();

        // The generated Style A context is self-contained (it emits the metadata for every member leaf —
        // int/string/int?/enum/collection/byte[] — plus the envelopes), so the read DTO and its envelopes
        // serialize AOT-clean with no reflection fallback in the chain.
        var list = BuildListResult(row);
        await Assert.That(() => JsonSerializer.Serialize(row, seam)).ThrowsNothing();
        await Assert.That(() => JsonSerializer.Serialize(list, seam)).ThrowsNothing();
    }

    // -- R10.2/R10.4 (write side): the always-nameable write models resolve from the generated context, ---
    // -- including AuditEntryCrud whose view's read row is anonymous (the D96 asymmetry). ------------------

    // Feature: style-a-coverage — Requirement 10.2 (a covered Style A write model TCrud resolves from a
    // Generated_View_Context — for the anonymous-row view too, since TCrud is always nameable, D38).
    [Test]
    public async Task CoveredWriteModels_ResolveFromGeneratedContext_IncludingAnonymousRowView()
    {
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(SubscriptionCrud))).IsNull();
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(AuditEntryCrud))).IsNull();

        var seam = BuildSeam(includeGenerated: true, developer: null, includeReflection: false);

        // Named-row writable view → TCrud covered.
        await Assert.That(AnyGeneratedContextCovers(typeof(SubscriptionCrud), seam)).IsTrue();

        // Anonymous-row writable view → TCrud STILL covered (its write body binds AOT-clean while the read
        // row stays on reflection — the D96 asymmetry within one view, D130).
        await Assert.That(AnyGeneratedContextCovers(typeof(AuditEntryCrud), seam)).IsTrue();

        var subCrud = new SubscriptionCrud { PlanName = "Pro", SeatCount = 25, RenewsOn = new DateTime(2027, 3, 1), Tier = SubscriptionTier.Premium };
        var auditCrud = new AuditEntryCrud { Action = "login", Severity = 2, OccurredAt = new DateTime(2026, 1, 2, 3, 4, 5), IsSensitive = true };
        await Assert.That(() => JsonSerializer.Serialize(subCrud, seam)).ThrowsNothing();
        await Assert.That(() => JsonSerializer.Serialize(auditCrud, seam)).ThrowsNothing();
    }

    // -- R10.2: coexistence — the generated context wins whether a developer App_Json_Context is present --
    // -- or absent, and the JSON is byte-for-byte identical either way and equal to the reflection oracle. -

    // Feature: style-a-coverage — Requirement 10.2 (developer-context optionality: a covered Style A DTO
    // resolves deterministically and produces identical JSON whether or not a developer context is chained).
    [Test]
    public async Task CoveredDtos_ByteForByteEqual_WhetherDeveloperContextAbsentOrPresent()
    {
        var row = FullyPopulatedCatalogRow();
        var crud = new SubscriptionCrud { PlanName = "Pro", SeatCount = 25, RenewsOn = new DateTime(2027, 3, 1), Tier = SubscriptionTier.Premium };

        // The developer context is chained AFTER the generated context, so the generated context wins for
        // every type it covers; the developer context is a decoy that must not change the output (R10.2).
        var developerAbsent = BuildSeam(includeGenerated: true, developer: null, includeReflection: false);
        var developerPresent = BuildSeam(includeGenerated: true, developer: DeveloperStyleAJsonContext.Default, includeReflection: false);
        var oracle = BuildReflectionOracle();

        await Assert.That(AnyGeneratedContextCovers(typeof(CatalogItemRow), developerAbsent)).IsTrue();
        await Assert.That(AnyGeneratedContextCovers(typeof(SubscriptionCrud), developerAbsent)).IsTrue();

        // Read side (CatalogItemRow): identical whether the developer context is present or absent, and
        // identical to the reflection oracle (byte-for-byte, no wire drift).
        var rowAbsent = JsonSerializer.Serialize(row, developerAbsent);
        var rowPresent = JsonSerializer.Serialize(row, developerPresent);
        var rowOracle = JsonSerializer.Serialize(row, oracle);
        await Assert.That(rowAbsent).IsEqualTo(rowPresent);
        await Assert.That(rowAbsent).IsEqualTo(rowOracle);

        // Write side (SubscriptionCrud): same coexistence guarantee.
        var crudAbsent = JsonSerializer.Serialize(crud, developerAbsent);
        var crudPresent = JsonSerializer.Serialize(crud, developerPresent);
        var crudOracle = JsonSerializer.Serialize(crud, oracle);
        await Assert.That(crudAbsent).IsEqualTo(crudPresent);
        await Assert.That(crudAbsent).IsEqualTo(crudOracle);
    }

    // -- R10.3: the uncovered anonymous audit read row still rides the reflection fallback, unchanged. -----

    // Feature: style-a-coverage — Requirement 10.3 (an uncovered Style A type — the anonymous read row that
    // is RUC by design, D96/D130 — is served by the reflection fallback exactly as before this feature).
    [Test]
    public async Task UncoveredAnonymousReadRow_StillFallsBackToReflectionResolver()
    {
        var seam = BuildSeam(includeGenerated: true, developer: null, includeReflection: true);

        // The anonymous read row of stylea-audit-entries is unnameable in generated source, so no generated
        // context covers it (D96/D130) and neither does the shipped envelope context.
        var anonRow = AnonymousAuditReadRow();
        var anonType = anonRow.GetType();
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(anonType)).IsNull();
        await Assert.That(AnyGeneratedContextCovers(anonType, seam)).IsFalse();

        // Yet the seam still resolves and serializes it — via the reflection fallback (non-regression).
        await Assert.That(seam.GetTypeInfo(anonType)).IsNotNull();
        await Assert.That(() => JsonSerializer.Serialize(anonRow, seam)).ThrowsNothing();
    }

    // -- R10.3: opting the reflection fallback out removes the reflection branch, Style A entries present. --

    // Feature: style-a-coverage — Requirement 10.3 (with Style A entries present, the reflection fallback is
    // still the only RUC branch and still opt-out-able: covered resolves, uncovered resolves to null).
    [Test]
    public async Task DisablingReflectionFallback_CoveredStyleADtoStillResolves_UncoveredResolvesToNull()
    {
        // Mirror the chain WITHOUT the reflection fallback — the shape DisableReflectionFallback leaves.
        var noFallback = BuildSeam(includeGenerated: true, developer: null, includeReflection: false);

        // A covered Style A view DTO still resolves from its generated context with no reflection branch
        // present, and serializes AOT-clean (the generated context is self-contained over its member leaves).
        await Assert.That(AnyGeneratedContextCovers(typeof(CatalogItemRow), noFallback)).IsTrue();
        await Assert.That(() => JsonSerializer.Serialize(FullyPopulatedCatalogRow(), noFallback)).ThrowsNothing();

        // The uncovered anonymous row no longer resolves: the reflection branch is gone (no RUC branch).
        var anonRow = AnonymousAuditReadRow();
        var resolver = noFallback.TypeInfoResolver;
        await Assert.That(resolver).IsNotNull();
        await Assert.That(resolver!.GetTypeInfo(anonRow.GetType(), noFallback)).IsNull();

        // Serialization of the uncovered type therefore throws instead of silently reflecting.
        await Assert.That(() => JsonSerializer.Serialize(anonRow, noFallback)).Throws<NotSupportedException>();
    }

    // -- R10.4: the shipped envelope context keeps precedence with Style A contexts chained. ---------------

    // Feature: style-a-coverage — Requirement 10.4 (the fixed request/response envelopes keep resolving from
    // the Static_Envelope_Context, unaffected by the added Style A entries; wire output unchanged).
    [Test]
    public async Task EnvelopeTypes_StillResolveFromStaticEnvelopeContext_WithStyleAContextsChained()
    {
        var seam = BuildSeam(includeGenerated: true, developer: null, includeReflection: true);

        // The fixed envelope/response type is served by the shipped Static_Envelope_Context (first in the
        // chain) and is NOT claimed by any Style A per-view context (R10.4).
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(VistaFieldMetadataResponse))).IsNotNull();
        await Assert.That(AnyGeneratedContextCovers(typeof(VistaFieldMetadataResponse), seam)).IsFalse();

        var field = new VistaFieldMetadataResponse(
            Name: "Balance",
            Label: "Account Balance",
            ClrType: "Decimal",
            IsFilterable: true,
            IsSortable: true,
            IsSearchable: false,
            IsScopable: false,
            IsHidden: false,
            IsPrimaryKey: false,
            AllowedOperators: "Equals");

        // The envelope still serializes byte-for-byte as the shipped context alone produces it, so the Style
        // A per-view contexts changed only the resolution of per-view DTOs, not the envelopes.
        var envelopeOnly = BuildSeam(includeGenerated: false, developer: null, includeReflection: false);
        await Assert.That(JsonSerializer.Serialize(field, seam))
            .IsEqualTo(JsonSerializer.Serialize(field, envelopeOnly));
    }

    // -- R10.3: a covered named-TRow view's export read goes through the generated accessor, not reflection.-

    // Feature: style-a-coverage — Requirement 10.3 (a covered named-row Style A view reads export values via
    // the generated ViewAccessorRegistry accessor; the anonymous-row view has none and stays on reflection).
    [Test]
    public async Task CoveredNamedRow_ExportFieldRead_GoesThroughGeneratedAccessor()
    {
        var row = FullyPopulatedCatalogRow();

        // The generated accessor map for the named-TRow view is registered under its constant view name.
        await Assert.That(ViewAccessorRegistry.TryGetAccessor(CatalogItemsView, "ItemId", out _)).IsTrue();
        await Assert.That(ViewAccessorRegistry.TryGetAccessor(CatalogItemsView, "Status", out _)).IsTrue();

        // ExportColumns.Value prefers the generated accessor (cast + member read) when one is registered, so
        // the value read equals the property value across the member-shape spectrum (scalar, string,
        // nullable, enum) — no reflection on this path.
        await Assert.That(ExportColumns.Value(CatalogItemsView, row, "ItemId")).IsEqualTo((object)row.ItemId);
        await Assert.That(ExportColumns.Value(CatalogItemsView, row, "Name")).IsEqualTo((object)row.Name);
        await Assert.That(ExportColumns.Value(CatalogItemsView, row, "ReorderLevel")).IsEqualTo((object?)row.ReorderLevel);
        await Assert.That(ExportColumns.Value(CatalogItemsView, row, "Status")).IsEqualTo((object)row.Status);

        // The anonymous-row view has NO generated accessor (its read row is unnameable, D96/D130), so its
        // export read stays on the reflection fallback — the export-path analogue of the serialization
        // asymmetry (R10.3 non-regression).
        await Assert.That(ViewAccessorRegistry.TryGetAccessor(AuditEntriesView, "Action", out _)).IsFalse();
        var anonRow = AnonymousAuditReadRow();
        await Assert.That(ExportColumns.Value(AuditEntriesView, anonRow, "Action")).IsEqualTo((object)"login");
    }

    // -- Fixtures & helpers -----------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="CatalogItemRow"/> with every member shape populated (a scalar, a string, a
    /// non-null nullable, an enum, a non-empty collection, and non-empty bytes) so parity/serialization
    /// exercises the full emittable-shape spectrum the generated context covers.
    /// </summary>
    private static CatalogItemRow FullyPopulatedCatalogRow() => new()
    {
        ItemId = 7,
        Name = "Widget",
        ReorderLevel = 3,
        Status = CatalogItemStatus.Active,
        Tags = new[] { "alpha", "beta" },
        Thumbnail = new byte[] { 1, 2, 3 },
    };

    private static ViewListResult<CatalogItemRow> BuildListResult(CatalogItemRow row)
    {
        // PagedResult(Items, TotalRows, PageIndex, PageSize, TotalPages); ViewListResult(Page, TotalRowsUnfiltered).
        var page = new PagedResult<CatalogItemRow>(new[] { row }, 1, 0, 10, 1);
        return new ViewListResult<CatalogItemRow>(page, 1);
    }

    /// <summary>
    /// Returns an instance whose runtime type mirrors the <c>stylea-audit-entries</c> anonymous read
    /// projection (<c>new { EntryId, Action, OccurredAt }</c>) — a genuinely uncovered type (its anonymous
    /// runtime type is unnameable in generated source, so no generated context or accessor exists for it).
    /// </summary>
    private static object AnonymousAuditReadRow() =>
        new { EntryId = 42, Action = "login", OccurredAt = (DateTime?)new DateTime(2026, 1, 2, 3, 4, 5) };

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration and
    /// resolver order (Decision Log D124/D126): the shipped <see cref="VistaStaticJsonContext"/> first,
    /// then — when <paramref name="includeGenerated"/> — every generated per-view context drained from
    /// <see cref="GeneratedJsonContextStore"/> (through the same opaque-handle → <see cref="IJsonTypeInfoResolver"/>
    /// cast the AspNetCore drain performs), then an optional developer <c>App_Json_Context</c>, then — when
    /// <paramref name="includeReflection"/> — the opt-out-able reflection fallback last.
    /// </summary>
    private static JsonSerializerOptions BuildSeam(
        bool includeGenerated,
        JsonSerializerContext? developer,
        bool includeReflection)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());

        options.TypeInfoResolverChain.Add(VistaStaticJsonContext.Default);
        if (includeGenerated)
        {
            foreach (var handle in GeneratedJsonContextStore.All)
            {
                options.TypeInfoResolverChain.Add((IJsonTypeInfoResolver)handle);
            }
        }

        if (developer is not null)
        {
            options.TypeInfoResolverChain.Add(developer);
        }

        if (includeReflection)
        {
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        }

        return options;
    }

    /// <summary>
    /// A reflection-only mirror of the seam configuration (the Behavioral_Oracle): identical options with a
    /// single <see cref="DefaultJsonTypeInfoResolver"/>, used to prove byte-for-byte parity.
    /// </summary>
    private static JsonSerializerOptions BuildReflectionOracle()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }

    /// <summary>
    /// Probes every registered <see cref="GeneratedJsonContextStore"/> handle (each an
    /// <see cref="IJsonTypeInfoResolver"/> by contract) and reports whether any generated per-view context
    /// provides a <see cref="JsonTypeInfo"/> for <paramref name="type"/>.
    /// </summary>
    private static bool AnyGeneratedContextCovers(Type type, JsonSerializerOptions options)
    {
        foreach (var handle in GeneratedJsonContextStore.All)
        {
            if (((IJsonTypeInfoResolver)handle).GetTypeInfo(type, options) is not null)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A developer-authored <c>App_Json_Context</c> that also covers the covered Style A DTOs, used purely as a
/// decoy in <see cref="StyleASeamCoexistenceTests"/> to prove the generated Style A context (chained ahead
/// of it) still wins — demonstrating developer-context optionality (Requirement 10.2). It is added only to a
/// local, per-test resolver chain and is never registered into any Core store, so it cannot collide with the
/// generated contexts registered by the fixture assembly.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CatalogItemRow))]
[JsonSerializable(typeof(SubscriptionCrud))]
[JsonSerializable(typeof(AuditEntryCrud))]
internal sealed partial class DeveloperStyleAJsonContext : JsonSerializerContext
{
}
