// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.GeneratorJsonContextSample;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// The DTO round-trip property test for the per-view <c>JsonTypeInfo</c> phase
/// (spec source-generator-json-typeinfo, task 8.3; Decision Log D125/D126; Property 2). Because
/// serialization is a parse/print pair, a round-trip property is mandatory: <b>deserializing the
/// serialization of any DTO value through the Generated_View_Context reproduces an equivalent value.</b>
/// <para>
/// For each covered typed Style B view in the referenced <c>a2n.Vista.GeneratorJsonContextSample</c>
/// assembly (<see cref="CatalogItemView"/> — read-only single-key, scalar + nullable + enum + collection +
/// <c>byte[]</c>; <see cref="GeoZoneView"/> — read-only composite-key; <see cref="SubscriptionView"/> —
/// writable, whose <c>TCrud</c> is a record with required + init-only members) and for any value of its
/// Serializable_DTO_Set (a <c>TRow</c>; a <see cref="ViewListResult{TRow}"/> with arbitrary rows, paging
/// and both totals; a <see cref="PagedResult{TRow}"/>; and — for the writable view — a <c>TCrud</c>), the
/// property serializes the value through the drained Generated_View_Context chain, deserializes the
/// resulting JSON back through the <b>same</b> generated chain, and asserts the reconstructed value is
/// equivalent to the original — explicitly exercising records, init-only / required members
/// (<see cref="SubscriptionCrud"/>), nullable members (<c>decimal?</c> / <c>DateTime?</c>), enums,
/// collections (<c>List&lt;string&gt;</c>), <c>byte[]</c>, and the envelope paging/total fields.
/// </summary>
/// <remarks>
/// <para>
/// <b>Equivalence by oracle re-serialization.</b> POCOs, collections and <c>byte[]</c> lack structural
/// equality (records have it), so equivalence between the round-tripped value and the original is proven by
/// re-serializing the round-tripped value through the Behavioral_Oracle (a
/// <see cref="DefaultJsonTypeInfoResolver"/> under the same <see cref="JsonSerializerOptions"/>) and
/// asserting byte-equality with the oracle serialization of the original — mirroring the equivalence
/// approach of the sibling master parity test (task 8.2).
/// </para>
/// <para>
/// <b>Compile-once, quantify-over-values.</b> The fixture assembly's typed Style B views are compiled once
/// (the <c>ViewJsonContextGenerator</c> emits a real per-view <see cref="IJsonTypeInfoResolver"/> for each,
/// registered into <see cref="GeneratedJsonContextStore"/> by a <c>[ModuleInitializer]</c> at module load).
/// The property never re-compiles per iteration; it resolves the generated contexts from the store and
/// varies only the DTO <b>values</b> (design "The parity oracle" / "Cost control").
/// </para>
/// <para>
/// <b>Shared-static isolation.</b> <see cref="VistaJson.Options"/> is a process-wide static whose resolver
/// chain freezes on first use, so each configuration is built as a <b>fresh</b>
/// <see cref="JsonSerializerOptions"/> mirroring the seam construction exactly (web defaults,
/// case-insensitive matching, the enum + <c>FilterNodeJsonConverter</c> converters) and the seam order
/// (<c>Static_Envelope_Context</c> → generated contexts drained from <see cref="GeneratedJsonContextStore"/>
/// → reflection fallback), draining the store through the very same opaque-handle →
/// <see cref="IJsonTypeInfoResolver"/> cast the AspNetCore seam performs.
/// </para>
/// <para>
/// <b>Known phase boundary (task 6.3).</b> A generated per-view context resolves only the top-level DTO
/// types; scalar leaf converters (for example <c>decimal</c>) and collection element metadata come from the
/// rest of the chain. The reflection fallback therefore stays present in the "generated" options for leaf
/// coverage — the no-fallback AOT case is task 9.1. An upfront guard nevertheless asserts every covered
/// top-level DTO type is served by a Generated_View_Context (never the reflection fallback), so the
/// round-trip being proven genuinely rides the generated path for the top-level DTOs.
/// </para>
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The oracle drives the reflection resolver by design; trimming is not used for tests.")]
[SuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with RequiresDynamicCodeAttribute may break functionality when AOT compiling",
    Justification = "The oracle drives the reflection resolver by design; AOT is not used for tests.")]
public sealed class JsonContextRoundTripPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    // Feature: source-generator-json-typeinfo, Property 2: DTO serialization round-trip.
    //
    // Validates: Requirements 6.3, 2.5
    [Test]
    public void Deserializing_The_Generated_Serialization_Reproduces_An_Equivalent_Value()
    {
        // Feature: source-generator-json-typeinfo, Property 2: DTO serialization round-trip.
        var generated = BuildGeneratedOptions();
        var oracle = BuildOracleOptions();

        // Upfront guard: every covered top-level DTO type must resolve from a Generated_View_Context (never
        // the reflection fallback), so the round-trip proven below rides the generated path for the
        // top-level DTOs. Leaf/element metadata (List<string>, byte[], IReadOnlyList<TRow>) legitimately
        // comes from the fallback (the known phase boundary), so only the top-level DTO types are guarded.
        foreach (var type in CoveredTopLevelTypes)
        {
            if (!ResolvedByGeneratedContext(type, generated))
            {
                throw new Exception(
                    $"Covered DTO type '{type}' was not served by a Generated_View_Context in the drained " +
                    "seam chain; the round-trip property would otherwise ride the reflection fallback. " +
                    "Ensure the fixture assembly's [ModuleInitializer] registered its context.");
            }
        }

        var genCase =
            from catRow in GenCatalogRow
            from catRows in GenCatalogRow.List[0, 4]
            from geoRow in GenGeoRow
            from geoRows in GenGeoRow.List[0, 4]
            from subRow in GenSubRow
            from subRows in GenSubRow.List[0, 4]
            from subCrud in GenSubCrud
            from paging in GenPaging
            select (catRow, catRows, geoRow, geoRows, subRow, subRows, subCrud, paging);

        genCase.Sample(
            tuple =>
            {
                var (catRow, catRows, geoRow, geoRows, subRow, subRows, subCrud, paging) = tuple;

                // Case 1 — read-only single-key view: TRow (scalar + nullable decimal + enum + List<string>
                // + byte[]), ViewListResult<TRow>, PagedResult<TRow> (paging/total fields).
                AssertRoundTrip(catRow, generated, oracle);
                AssertRoundTrip(BuildPaged(catRows, paging), generated, oracle);
                AssertRoundTrip(BuildListResult(catRows, paging), generated, oracle);

                // Case 2 — read-only composite-key view.
                AssertRoundTrip(geoRow, generated, oracle);
                AssertRoundTrip(BuildPaged(geoRows, paging), generated, oracle);
                AssertRoundTrip(BuildListResult(geoRows, paging), generated, oracle);

                // Case 3 — writable view: read DTOs (nullable DateTime + enum) + the record TCrud with
                // required + init-only members.
                AssertRoundTrip(subRow, generated, oracle);
                AssertRoundTrip(BuildPaged(subRows, paging), generated, oracle);
                AssertRoundTrip(BuildListResult(subRows, paging), generated, oracle);
                AssertRoundTrip(subCrud, generated, oracle);
            },
            iter: Iterations);
    }

    // -- Round-trip assertion ---------------------------------------------------------------------------

    /// <summary>
    /// Asserts Property 2 for one value: serializing the value through the generated chain and then
    /// deserializing that JSON back through the <b>same</b> generated chain reproduces an equivalent value.
    /// Equivalence is proven by re-serializing the round-tripped value through the oracle and asserting
    /// byte-equality with the oracle serialization of the original (robust for POCOs, collections and
    /// <c>byte[]</c> that lack structural equality, and exact for records).
    /// </summary>
    private static void AssertRoundTrip<T>(T original, JsonSerializerOptions generated, JsonSerializerOptions oracle)
    {
        var json = JsonSerializer.Serialize(original, generated);
        var roundTripped = JsonSerializer.Deserialize<T>(json, generated);

        var reRoundTripped = JsonSerializer.Serialize(roundTripped, oracle);
        var reOriginal = JsonSerializer.Serialize(original, oracle);
        if (!string.Equals(reRoundTripped, reOriginal, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Round-tripping '{typeof(T)}' through the generated context chain (serialize → " +
                $"deserialize) produced a value differing from the original (compared by oracle " +
                $"re-serialization).\n  round-tripped: {reRoundTripped}\n  original:      {reOriginal}");
        }
    }

    // -- Chain construction (mirrors VistaJson) ---------------------------------------------------------

    /// <summary>
    /// Builds the "generated" seam options: a fresh <see cref="JsonSerializerOptions"/> mirroring the seam
    /// configuration and order — <c>Static_Envelope_Context</c> first, then every generated per-view
    /// context drained from <see cref="GeneratedJsonContextStore"/> (through the same opaque-handle cast the
    /// AspNetCore drain performs), then the reflection fallback for leaf/element coverage (the known phase
    /// boundary) — with <b>no developer <c>App_Json_Context</c></b>.
    /// </summary>
    private static JsonSerializerOptions BuildGeneratedOptions()
    {
        var options = SeamBaseOptions();
        options.TypeInfoResolverChain.Add(VistaStaticJsonContext.Default);
        foreach (var handle in GeneratedJsonContextStore.All)
        {
            options.TypeInfoResolverChain.Add((IJsonTypeInfoResolver)handle);
        }

        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }

    /// <summary>
    /// Builds the Behavioral_Oracle options: the same seam <see cref="JsonSerializerOptions"/> configuration
    /// with only the reflection resolver in the chain.
    /// </summary>
    private static JsonSerializerOptions BuildOracleOptions()
    {
        var options = SeamBaseOptions();
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }

    /// <summary>The shared seam options configuration (web defaults, case-insensitive, seam converters).</summary>
    private static JsonSerializerOptions SeamBaseOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        return options;
    }

    /// <summary>
    /// Reports whether a Generated_View_Context — one of the opaque handles drained from
    /// <see cref="GeneratedJsonContextStore"/>, ahead of the reflection fallback in the chain — is the
    /// resolver that serves <paramref name="type"/>. Walks the mirrored chain slot-by-slot (the shipped
    /// <c>Static_Envelope_Context</c> first, then the generated contexts) and returns <see langword="true"/>
    /// only when a generated context provides the type's info before the static context would.
    /// </summary>
    private static bool ResolvedByGeneratedContext(Type type, JsonSerializerOptions probeOptions)
    {
        if (VistaStaticJsonContext.Default.GetTypeInfo(type) is not null)
        {
            return false;
        }

        foreach (var handle in GeneratedJsonContextStore.All)
        {
            if (((IJsonTypeInfoResolver)handle).GetTypeInfo(type, probeOptions) is not null)
            {
                return true;
            }
        }

        return false;
    }

    // -- Envelope builders ------------------------------------------------------------------------------

    private static PagedResult<T> BuildPaged<T>(IReadOnlyList<T> rows, PagingModel paging) =>
        new(rows, paging.TotalRows, paging.PageIndex, paging.PageSize, paging.TotalPages);

    private static ViewListResult<T> BuildListResult<T>(IReadOnlyList<T> rows, PagingModel paging) =>
        new(BuildPaged(rows, paging), paging.TotalRowsUnfiltered);

    // -- Value generators -------------------------------------------------------------------------------

    private static readonly string[] TextPool =
        { "", "Alice", "Bob", "naïve café", "a\"quoted\"b", "back\\slash", "tab\tend", "  spaced  " };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    private static readonly Gen<byte[]> GenBytes =
        Gen.Int[0, 255].Select(i => (byte)i).Array[0, 8];

    // An optional DateTime with a fixed Kind (Unspecified) so the value space is well-defined; the STJ
    // default DateTime converter governs both the generated and oracle paths identically, so the round-trip
    // holds regardless of the concrete instant.
    private static readonly Gen<DateTime?> GenOptionalDate =
        from present in Gen.Bool
        from minutes in Gen.Int[0, 5_000_000]
        select present ? new DateTime(2000, 1, 1).AddMinutes(minutes) : (DateTime?)null;

    private static readonly Gen<decimal?> GenOptionalDecimal =
        from present in Gen.Bool
        from cents in Gen.Int[-1_000_000, 1_000_000]
        select present ? cents / 100m : (decimal?)null;

    private static readonly Gen<CatalogItemStatus> GenCatalogStatus =
        Gen.Int[0, 2].Select(i => (CatalogItemStatus)i);

    private static readonly Gen<SubscriptionTier> GenSubscriptionTier =
        Gen.Int[0, 2].Select(i => (SubscriptionTier)i);

    private static readonly Gen<CatalogItemRow> GenCatalogRow =
        from itemId in Gen.Int[-100_000, 100_000]
        from name in Pick(TextPool)
        from discount in GenOptionalDecimal
        from status in GenCatalogStatus
        from tags in Pick(TextPool).List[0, 4]
        from thumbnail in GenBytes
        select new CatalogItemRow
        {
            ItemId = itemId,
            Name = name,
            Discount = discount,
            Status = status,
            Tags = tags,
            Thumbnail = thumbnail,
        };

    private static readonly Gen<GeoZoneRow> GenGeoRow =
        from regionId in Gen.Int[-100_000, 100_000]
        from zoneCode in Pick(TextPool)
        from description in Pick(TextPool)
        from isActive in Gen.Bool
        select new GeoZoneRow
        {
            RegionId = regionId,
            ZoneCode = zoneCode,
            Description = description,
            IsActive = isActive,
        };

    private static readonly Gen<SubscriptionRow> GenSubRow =
        from id in Gen.Int[-100_000, 100_000]
        from planName in Pick(TextPool)
        from seatCount in Gen.Int[0, 10_000]
        from renewsOn in GenOptionalDate
        from tier in GenSubscriptionTier
        select new SubscriptionRow
        {
            SubscriptionId = id,
            PlanName = planName,
            SeatCount = seatCount,
            RenewsOn = renewsOn,
            Tier = tier,
        };

    private static readonly Gen<SubscriptionCrud> GenSubCrud =
        from planName in Pick(TextPool)
        from seatCount in Gen.Int[0, 10_000]
        from renewsOn in GenOptionalDate
        from tier in GenSubscriptionTier
        select new SubscriptionCrud
        {
            PlanName = planName,
            SeatCount = seatCount,
            RenewsOn = renewsOn,
            Tier = tier,
        };

    private static readonly Gen<PagingModel> GenPaging =
        from totalRows in Gen.Long[0, 5_000_000]
        from pageIndex in Gen.Int[0, 100]
        from pageSize in Gen.Int[1, 200]
        from totalPages in Gen.Long[0, 50_000]
        from unfiltered in Gen.Long[0, 5_000_000]
        select new PagingModel(totalRows, pageIndex, pageSize, totalPages, unfiltered);

    /// <summary>The paging/total fields shared by the <c>ViewListResult</c> / <c>PagedResult</c> envelopes.</summary>
    private readonly record struct PagingModel(
        long TotalRows,
        int PageIndex,
        int PageSize,
        long TotalPages,
        long TotalRowsUnfiltered);

    // -- The covered Serializable_DTO_Set top-level types (guarded to resolve from a generated context) --

    private static readonly Type[] CoveredTopLevelTypes =
    {
        typeof(CatalogItemRow),
        typeof(ViewListResult<CatalogItemRow>),
        typeof(PagedResult<CatalogItemRow>),
        typeof(GeoZoneRow),
        typeof(ViewListResult<GeoZoneRow>),
        typeof(PagedResult<GeoZoneRow>),
        typeof(SubscriptionRow),
        typeof(ViewListResult<SubscriptionRow>),
        typeof(PagedResult<SubscriptionRow>),
        typeof(SubscriptionCrud),
    };
}
