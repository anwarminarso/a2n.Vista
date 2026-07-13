// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.GeneratorStyleASample;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// The DTO round-trip property test for the M9 <b>Style A</b> (central-template) coverage phase
/// (spec style-a-coverage, task 8.3; Decision Log D129/D130; Property 2). Because serialization is a
/// parse/print pair, a round-trip property is mandatory: <b>deserializing the serialization of any covered
/// Style A DTO value through the generated per-view context reproduces an equivalent value.</b>
/// <para>
/// The referenced <c>a2n.Vista.GeneratorStyleASample</c> assembly hosts three representative Style A views
/// authored as <c>AddView&lt;TRow&gt;(name, projection)</c> call sites in a single
/// <c>ViewTemplate&lt;TDbContext&gt;</c>. The fifth incremental generator (<c>StyleAShapeGenerator</c>,
/// D129) emits, per <b>covered</b> view, a reflection-free <see cref="IJsonTypeInfoResolver"/> — built by
/// hand via <see cref="System.Text.Json.Serialization.Metadata.JsonMetadataServices"/> (no
/// <c>[JsonSerializable]</c> attribute route) — and registers it into the Core-resident
/// <see cref="GeneratedJsonContextStore"/> from a <c>[ModuleInitializer]</c>, keyed by the <b>constant</b>
/// <c>AddView</c> name (the D129 difference from D125's <c>new View().Name</c> keying). The covered DTO set
/// this property round-trips is therefore:
/// </para>
/// <list type="bullet">
///   <item><description><c>stylea-catalog-items</c> (read-only, <b>named</b> row) →
///   <see cref="CatalogItemRow"/> + its two envelopes (scalar + nullable value type + enum + collection +
///   <c>byte[]</c> members);</description></item>
///   <item><description><c>stylea-subscriptions</c> (writable, named row) → <see cref="SubscriptionRow"/>
///   + its two envelopes + the record <see cref="SubscriptionCrud"/> (a <c>required</c> member + init-only
///   members — the R3.4 parameterized/init construction path);</description></item>
///   <item><description><c>stylea-audit-entries</c> (writable, <b>anonymous</b> row) →
///   <see cref="AuditEntryCrud"/> <b>only</b> — the D96 asymmetry: the write model is nameable and covered
///   while the anonymous read row stays on the reflection path by design, so it is excluded here.</description></item>
/// </list>
/// <para>
/// For any value of that set (a <c>TRow</c>; a <see cref="ViewListResult{TRow}"/> with arbitrary rows,
/// paging and both totals; a <see cref="PagedResult{TRow}"/>; and the two <c>TCrud</c> write models), the
/// property serializes the value through the drained Style A generated-context chain, deserializes the
/// resulting JSON back through the <b>same</b> generated chain, and asserts the reconstructed value is
/// equivalent to the original — explicitly exercising records, init-only / required members
/// (<see cref="SubscriptionCrud"/>, <see cref="AuditEntryCrud"/>), nullable members (<c>int?</c> /
/// <c>DateTime?</c>), enums, collections (<see cref="IReadOnlyList{T}"/> of <c>string</c>), <c>byte[]</c>,
/// and the envelope paging/total fields (R6.3, R3.4).
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Equivalence by oracle re-serialization.</b> The Style A read rows are POCOs and their members include
/// collections and <c>byte[]</c>, which lack structural equality (records have it), so equivalence between
/// the round-tripped value and the original is proven by re-serializing the round-tripped value through the
/// Behavioral_Oracle (a <see cref="DefaultJsonTypeInfoResolver"/> under the same
/// <see cref="JsonSerializerOptions"/>) and asserting byte-equality with the oracle serialization of the
/// original — mirroring the equivalence approach of the sibling master parity test (task 8.2) and the
/// Style B round-trip test.
/// </para>
/// <para>
/// <b>Compile-once, quantify-over-values.</b> The fixture assembly's Style A views are compiled once (their
/// generated per-view contexts are registered into <see cref="GeneratedJsonContextStore"/> by a
/// <c>[ModuleInitializer]</c> at module load). The property never re-compiles per iteration; it resolves the
/// generated contexts from the store and varies only the DTO <b>values</b> (design "The parity oracle" /
/// "Cost control"), using the same value generators as the sibling seam-resolution property test for
/// consistency.
/// </para>
/// <para>
/// <b>Shared-static isolation.</b> <see cref="VistaJson.Options"/> is a process-wide static whose resolver
/// chain freezes on first use, so mutating it would be fragile across the whole test process. Each
/// configuration is therefore built as a <b>fresh</b> <see cref="JsonSerializerOptions"/> mirroring the seam
/// construction exactly (web defaults, case-insensitive matching, the enum + <c>FilterNodeJsonConverter</c>
/// converters) and the seam order (<c>Static_Envelope_Context</c> → generated contexts drained from
/// <see cref="GeneratedJsonContextStore"/> → reflection fallback), draining the store through the very same
/// opaque-handle → <see cref="IJsonTypeInfoResolver"/> cast the AspNetCore seam performs.
/// </para>
/// <para>
/// <b>Known phase boundary (task 6.3).</b> A generated per-view context resolves only the top-level DTO
/// types; scalar leaf converters and collection element metadata come from the rest of the chain. The
/// reflection fallback therefore stays present in the "generated" options for leaf coverage — the
/// no-fallback AOT case is task 9.1. An upfront guard nevertheless asserts every covered top-level DTO type
/// is served by a generated Style A context (never the reflection fallback), so the round-trip being proven
/// genuinely rides the generated path for the top-level DTOs.
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
public sealed class StyleARoundTripPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    static StyleARoundTripPropertyTests()
    {
        // Force the fixture assembly's [ModuleInitializer]s to run (they register the generated Style A
        // per-view contexts into GeneratedJsonContextStore, keyed by the constant AddView name). Because the
        // upfront guard drains the store before any fixture instance is constructed, referencing a type via
        // typeof alone is not enough — run the module .cctor explicitly so the store is populated whether
        // this class runs in isolation or as part of the full suite (mirrors the sibling task 8.2 test).
        RuntimeHelpers.RunModuleConstructor(typeof(CatalogItemRow).Assembly.ManifestModule.ModuleHandle);
    }

    // Feature: style-a-coverage, Property 2: DTO serialization round-trip.
    //
    // Validates: Requirements 6.3, 3.4
    [Test]
    public void Deserializing_The_Generated_StyleA_Serialization_Reproduces_An_Equivalent_Value()
    {
        // Feature: style-a-coverage, Property 2: DTO serialization round-trip.
        var generated = BuildGeneratedOptions();
        var oracle = BuildOracleOptions();

        // Upfront guard: every covered top-level Style A DTO type must resolve from a generated per-view
        // context (never the reflection fallback), so the round-trip proven below rides the generated path
        // for the top-level DTOs. Leaf/element metadata (IReadOnlyList<string>, byte[], IReadOnlyList<TRow>)
        // legitimately comes from the fallback (the known phase boundary), so only the top-level DTO types
        // are guarded. This also fails fast, with a clear message, if the fixture assembly's
        // [ModuleInitializer] did not register a covered view's context.
        foreach (var type in CoveredTopLevelTypes)
        {
            if (!ResolvedByGeneratedContext(type, generated))
            {
                throw new Exception(
                    $"Covered Style A DTO type '{type}' was not served by a generated per-view context in " +
                    "the drained seam chain; the round-trip property would otherwise ride the reflection " +
                    "fallback. Ensure the a2n.Vista.GeneratorStyleASample [ModuleInitializer] registered its " +
                    "context into GeneratedJsonContextStore.");
            }
        }

        var genCase =
            from catRow in GenCatalogRow
            from catRows in GenCatalogRow.List[0, 4]
            from subRow in GenSubRow
            from subRows in GenSubRow.List[0, 4]
            from subCrud in GenSubCrud
            from auditCrud in GenAuditCrud
            from paging in GenPaging
            select (catRow, catRows, subRow, subRows, subCrud, auditCrud, paging);

        genCase.Sample(
            tuple =>
            {
                var (catRow, catRows, subRow, subRows, subCrud, auditCrud, paging) = tuple;

                // stylea-catalog-items — read-only, named row: TRow (scalar + nullable int + enum +
                // IReadOnlyList<string> + byte[]), ViewListResult<TRow>, PagedResult<TRow> (paging/total).
                AssertRoundTrip(catRow, generated, oracle);
                AssertRoundTrip(BuildPaged(catRows, paging), generated, oracle);
                AssertRoundTrip(BuildListResult(catRows, paging), generated, oracle);

                // stylea-subscriptions — writable, named row: read DTOs (nullable DateTime + enum) + the
                // record TCrud with a required member + init-only members (the R3.4 construction path).
                AssertRoundTrip(subRow, generated, oracle);
                AssertRoundTrip(BuildPaged(subRows, paging), generated, oracle);
                AssertRoundTrip(BuildListResult(subRows, paging), generated, oracle);
                AssertRoundTrip(subCrud, generated, oracle);

                // stylea-audit-entries — writable, ANONYMOUS row: the named record TCrud only (the D96
                // asymmetry — its read row is unnameable, so no read DTO is covered).
                AssertRoundTrip(auditCrud, generated, oracle);
            },
            iter: Iterations);
    }

    // -- Round-trip assertion ---------------------------------------------------------------------------

    /// <summary>
    /// Asserts Property 2 for one value: serializing the value through the generated Style A chain and then
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
                $"Round-tripping '{typeof(T)}' through the generated Style A context chain (serialize → " +
                $"deserialize) produced a value differing from the original (compared by oracle " +
                $"re-serialization).\n  round-tripped: {reRoundTripped}\n  original:      {reOriginal}");
        }
    }

    // -- Chain construction (mirrors VistaJson) ---------------------------------------------------------

    /// <summary>
    /// Builds the "generated" seam options: a fresh <see cref="JsonSerializerOptions"/> mirroring the seam
    /// configuration and order — <c>Static_Envelope_Context</c> first, then every generated per-view context
    /// drained from <see cref="GeneratedJsonContextStore"/> (through the same opaque-handle cast the
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
    /// Reports whether a generated per-view context — one of the opaque handles drained from
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

    // -- Value generators (mirror the sibling StyleASeamResolutionPropertyTests generators) -------------

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

    private static readonly Gen<int?> GenOptionalInt =
        from present in Gen.Bool
        from value in Gen.Int[-100_000, 100_000]
        select present ? value : (int?)null;

    private static readonly Gen<CatalogItemStatus> GenCatalogStatus =
        Gen.Int[0, 2].Select(i => (CatalogItemStatus)i);

    private static readonly Gen<SubscriptionTier> GenSubscriptionTier =
        Gen.Int[0, 2].Select(i => (SubscriptionTier)i);

    private static readonly Gen<CatalogItemRow> GenCatalogRow =
        from itemId in Gen.Int[-100_000, 100_000]
        from name in Pick(TextPool)
        from reorderLevel in GenOptionalInt
        from status in GenCatalogStatus
        from tags in Pick(TextPool).List[0, 4]
        from thumbnail in GenBytes
        select new CatalogItemRow
        {
            ItemId = itemId,
            Name = name,
            ReorderLevel = reorderLevel,
            Status = status,
            Tags = tags,
            Thumbnail = thumbnail,
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

    private static readonly Gen<AuditEntryCrud> GenAuditCrud =
        from action in Pick(TextPool)
        from severity in Gen.Int[-100, 100]
        from occurredAt in GenOptionalDate
        from isSensitive in Gen.Bool
        select new AuditEntryCrud
        {
            Action = action,
            Severity = severity,
            OccurredAt = occurredAt,
            IsSensitive = isSensitive,
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

    /// <summary>
    /// The covered Style A top-level DTO types across the three fixture views. Each must resolve from a
    /// drained generated per-view context — never the reflection fallback. Note there is deliberately no
    /// audit-entries read row: that view's read projection is anonymous, so only its named
    /// <see cref="AuditEntryCrud"/> write model is covered (the D96 asymmetry).
    /// </summary>
    private static readonly Type[] CoveredTopLevelTypes =
    {
        typeof(CatalogItemRow),
        typeof(ViewListResult<CatalogItemRow>),
        typeof(PagedResult<CatalogItemRow>),
        typeof(SubscriptionRow),
        typeof(ViewListResult<SubscriptionRow>),
        typeof(PagedResult<SubscriptionRow>),
        typeof(SubscriptionCrud),
        typeof(AuditEntryCrud),
    };
}
