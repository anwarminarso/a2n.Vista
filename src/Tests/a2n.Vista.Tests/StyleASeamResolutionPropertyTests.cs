// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
/// Property-based test for the serialization seam's resolution of the source-generated per-view
/// <c>JsonTypeInfo</c> emitted for the covered <b>Style A</b> (central-template) views, and the resulting
/// optionality of the developer <c>App_Json_Context</c> (spec style-a-coverage, task 6.2; Decision Log
/// D129/D130; Property 4; Requirements 5.3, 10.2).
/// <para>
/// The referenced <c>a2n.Vista.GeneratorStyleASample</c> assembly hosts three representative Style A views
/// authored as <c>AddView&lt;TRow&gt;(name, projection)</c> call sites in a single
/// <c>ViewTemplate&lt;TDbContext&gt;</c>. The fifth incremental generator (<c>StyleAShapeGenerator</c>,
/// D129) emits a reflection-free <see cref="IJsonTypeInfoResolver"/> per <b>covered</b> view — built by hand
/// via <see cref="System.Text.Json.Serialization.Metadata.JsonMetadataServices"/> (no
/// <c>[JsonSerializable]</c> attribute route) — and registers it into the Core-resident
/// <see cref="GeneratedJsonContextStore"/> from a <c>[ModuleInitializer]</c>, keyed by the <b>constant</b>
/// <c>AddView</c> name (the D129 difference from D125's <c>new View().Name</c> keying). The covered DTO set
/// this property quantifies over is therefore:
/// </para>
/// <list type="bullet">
///   <item><description><c>stylea-catalog-items</c> (read-only, <b>named</b> row) →
///   <see cref="CatalogItemRow"/>, <see cref="ViewListResult{TRow}"/>, <see cref="PagedResult{TRow}"/>
///   over it (scalar + nullable + enum + collection + <c>byte[]</c> members);</description></item>
///   <item><description><c>stylea-subscriptions</c> (writable, named row) → <see cref="SubscriptionRow"/>
///   + its two envelopes + the record <see cref="SubscriptionCrud"/> (required + init-only
///   members);</description></item>
///   <item><description><c>stylea-audit-entries</c> (writable, <b>anonymous</b> row) →
///   <see cref="AuditEntryCrud"/> <b>only</b> — the D96 asymmetry: the write model is nameable and covered
///   while the anonymous read row stays on the reflection path by design.</description></item>
/// </list>
/// <para>
/// Drained into the seam ahead of both a developer <c>App_Json_Context</c> and the reflection fallback,
/// this property proves that:
/// </para>
/// <list type="number">
///   <item><description>for any runtime type in a covered Style A view's covered DTO set, the seam resolves
///   the type's <see cref="JsonTypeInfo"/> from the drained Style A <c>Generated_View_Context</c> — never
///   the reflection fallback — <b>whether or not</b> a developer <c>App_Json_Context</c> is also registered
///   (R5.3);</description></item>
///   <item><description>when both a generated Style A context and a developer <c>App_Json_Context</c> cover
///   the same type, resolution is deterministic by the defined chain order and the JSON produced is
///   byte-for-byte identical whichever resolver wins — and identical to the reflection oracle (R10.2).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared-static isolation.</b> <see cref="VistaJson.Options"/> is a process-wide static whose resolver
/// chain freezes on first use, so mutating it (or relying on the exact set of contexts the real seam drained
/// at first use) would be fragile across the whole test process. Each case therefore builds a <b>fresh</b>
/// <see cref="JsonSerializerOptions"/> that mirrors the seam construction exactly — the same configuration
/// <see cref="VistaJson"/> performs (web defaults, case-insensitive matching, the enum +
/// <c>FilterNodeJsonConverter</c> converters) and the same order (<c>Static_Envelope_Context</c> →
/// generated contexts drained from <see cref="GeneratedJsonContextStore"/> → optional developer
/// <c>App_Json_Context</c> → reflection fallback) — draining the store through the very same opaque-handle →
/// <see cref="IJsonTypeInfoResolver"/> cast the AspNetCore seam performs (R5.3).
/// </para>
/// <para>
/// <b>Known phase boundary.</b> The generated Style A context resolves the covered <b>top-level</b> DTO
/// types (and the leaf/element metadata they reach). The reflection fallback stays present in the mirrored
/// options for any type no chained context covers; the property's claim — a covered top-level DTO resolves
/// from a generated context — is proven by walking the mirrored chain slot-by-slot (the exact first-non-null
/// rule the combined resolver applies) and asserting the winning slot is a drained generated context, not
/// the fallback or the developer context. This mirrors the D125 seam-resolution test exactly.
/// </para>
/// <para>
/// <b>Resolution is by type; parity is by value.</b> Which resolver serves a type depends only on the type,
/// so the resolution half is asserted once, up front, over the fixed covered set. The byte-for-byte parity
/// half depends on the DTO value, so it is sampled over random DTO values (minimum 100 iterations).
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
public sealed class StyleASeamResolutionPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    /// <summary>The role of a resolver slot in the mirrored seam chain.</summary>
    private enum ResolverKind
    {
        /// <summary>No slot covered the type.</summary>
        None,

        /// <summary>The shipped <see cref="VistaStaticJsonContext"/> (fixed envelopes), always first.</summary>
        Static,

        /// <summary>A source-generated per-view context drained from <see cref="GeneratedJsonContextStore"/>.</summary>
        Generated,

        /// <summary>A developer-authored <c>App_Json_Context</c> chained by <c>AddVistaJsonContext</c>.</summary>
        Developer,

        /// <summary>The reflection fallback (<see cref="DefaultJsonTypeInfoResolver"/>), always last.</summary>
        Reflection,
    }

    /// <summary>The reflection oracle resolver (RUC). Held once; trimming/AOT are not used for tests.</summary>
    private static readonly IJsonTypeInfoResolver ReflectionResolver = new DefaultJsonTypeInfoResolver();

    /// <summary>
    /// The covered Serializable_DTO_Set top-level types across the three Style A fixture views. Each must
    /// resolve from a drained Style A <c>Generated_View_Context</c> — never the reflection fallback. Note
    /// there is deliberately no audit-entries read row: that view's read projection is anonymous, so only
    /// its named <see cref="AuditEntryCrud"/> write model is covered (the D96 asymmetry).
    /// </summary>
    private static readonly Type[] CoveredTypes =
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

    // Feature: style-a-coverage, Property 4: Seam resolves covered Style A DTOs from the generated context.
    //
    // Validates: Requirements 5.3, 10.2
    [Test]
    public void Seam_Resolves_Covered_StyleA_DTOs_From_Generated_Context_Making_Developer_Context_Optional()
    {
        // Feature: style-a-coverage, Property 4: Seam resolves covered Style A DTOs from the generated
        // context.

        // Half 1 (resolution — type-based, deterministic): every covered Style A DTO type resolves from a
        // drained Generated_View_Context — never the reflection fallback or a developer context — whether or
        // not a developer App_Json_Context is also registered (R5.3). This also fails fast, with a clear
        // message, if the fixture assembly's [ModuleInitializer] did not register a covered view's context.
        AssertCoveredTypesResolveFromGeneratedContext();

        // Half 2 (parity — value-based): for random DTO values, the JSON is byte-for-byte identical whether
        // the developer context is absent, present-behind (generated wins), or present-ahead (developer
        // wins) — all equal to the reflection oracle (R10.2).
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

                // stylea-catalog-items — read-only, named row: TRow + ViewListResult<TRow> + PagedResult<TRow>.
                AssertByteForByteParity(catRow);
                AssertByteForByteParity(BuildPaged(catRows, paging));
                AssertByteForByteParity(BuildListResult(catRows, paging));

                // stylea-subscriptions — writable, named row: read DTOs + the record TCrud (required + init).
                AssertByteForByteParity(subRow);
                AssertByteForByteParity(BuildPaged(subRows, paging));
                AssertByteForByteParity(BuildListResult(subRows, paging));
                AssertByteForByteParity(subCrud);

                // stylea-audit-entries — writable, ANONYMOUS row: the named TCrud only (the D96 asymmetry).
                AssertByteForByteParity(auditCrud);
            },
            iter: Iterations);
    }

    // -- Resolution (Half 1) ----------------------------------------------------------------------------

    /// <summary>
    /// Asserts that every covered Style A DTO type resolves from a drained <c>Generated_View_Context</c>
    /// (never the reflection fallback or a developer context) both when a developer <c>App_Json_Context</c>
    /// is absent and when it is present-behind the generated contexts — the real seam order — so the
    /// developer context is optional (R5.3).
    /// </summary>
    private static void AssertCoveredTypesResolveFromGeneratedContext()
    {
        foreach (var type in CoveredTypes)
        {
            foreach (var developerPresent in new[] { false, true })
            {
                var winner = WinningKind(type, developerPresent: developerPresent, developerFirst: false);
                if (winner == ResolverKind.None)
                {
                    throw new Exception(
                        $"Covered Style A DTO type '{type}' resolved to no JsonTypeInfo in the mirrored seam " +
                        $"(developerPresent={developerPresent}). Its generated per-view context must be " +
                        "registered by the a2n.Vista.GeneratorStyleASample [ModuleInitializer] and drained " +
                        "from GeneratedJsonContextStore.");
                }

                if (winner != ResolverKind.Generated)
                {
                    throw new Exception(
                        $"Covered Style A DTO type '{type}' was served from the {winner} resolver " +
                        $"(developerPresent={developerPresent}); a covered Style A DTO must resolve from the " +
                        "drained Generated_View_Context — never the reflection fallback or a developer " +
                        "context — so the developer App_Json_Context is optional (R5.3, R10.2).");
                }
            }
        }
    }

    // -- Parity (Half 2) --------------------------------------------------------------------------------

    /// <summary>
    /// Serializes <paramref name="value"/> through the reflection oracle and through three seam
    /// configurations — developer context absent; developer context present behind the generated contexts
    /// (generated wins); developer context present ahead of the generated contexts (developer wins) — and
    /// asserts all four JSON strings are byte-for-byte identical (R10.2).
    /// </summary>
    private static void AssertByteForByteParity<T>(T value)
    {
        var oracle = JsonSerializer.Serialize(value, BuildOracleOptions());

        var generatedNoDeveloper =
            JsonSerializer.Serialize(value, BuildSeamOptions(developerPresent: false, developerFirst: false));
        var generatedWinsWithDeveloper =
            JsonSerializer.Serialize(value, BuildSeamOptions(developerPresent: true, developerFirst: false));
        var developerWins =
            JsonSerializer.Serialize(value, BuildSeamOptions(developerPresent: true, developerFirst: true));

        if (!string.Equals(generatedNoDeveloper, oracle, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Serializing '{typeof(T)}' through the seam with NO developer context produced JSON " +
                $"differing from the reflection oracle.\n  generated: {generatedNoDeveloper}\n  oracle:    {oracle}");
        }

        if (!string.Equals(generatedWinsWithDeveloper, oracle, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Serializing '{typeof(T)}' through the seam with the generated Style A context winning ahead " +
                $"of a registered developer context produced JSON differing from the oracle.\n" +
                $"  generated: {generatedWinsWithDeveloper}\n  oracle:    {oracle}");
        }

        if (!string.Equals(developerWins, oracle, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Serializing '{typeof(T)}' through the seam with the developer context winning ahead of the " +
                $"generated Style A context produced JSON differing from the oracle — resolution must yield " +
                $"the same JSON whichever resolver wins.\n  developer: {developerWins}\n  oracle:    {oracle}");
        }
    }

    // -- Chain construction (mirrors VistaJson) ---------------------------------------------------------

    /// <summary>
    /// Walks the mirrored seam chain slot-by-slot and returns the kind of the first slot that provides a
    /// <see cref="JsonTypeInfo"/> for <paramref name="type"/> — the exact first-non-null rule the combined
    /// <see cref="JsonSerializerOptions.TypeInfoResolver"/> applies.
    /// </summary>
    private static ResolverKind WinningKind(Type type, bool developerPresent, bool developerFirst)
    {
        // A throwaway options instance the resolver slots build their JsonTypeInfo against. Only per-slot
        // GetTypeInfo is invoked (never serialization), so it is never frozen.
        var probeOptions = BuildSeamOptions(developerPresent, developerFirst);

        foreach (var (kind, resolver) in BuildChain(developerPresent, developerFirst))
        {
            if (resolver.GetTypeInfo(type, probeOptions) is not null)
            {
                return kind;
            }
        }

        return ResolverKind.None;
    }

    /// <summary>
    /// Builds the ordered, mirrored seam resolver chain: <c>Static_Envelope_Context</c> first, then the
    /// generated per-view contexts drained from <see cref="GeneratedJsonContextStore"/> (ahead of the
    /// developer context and the reflection fallback), the optional developer <c>App_Json_Context</c>, and
    /// the reflection fallback last. <paramref name="developerFirst"/> places the developer context ahead of
    /// the generated contexts to exercise the "developer wins" resolution and prove the JSON is identical
    /// either way.
    /// </summary>
    private static List<(ResolverKind Kind, IJsonTypeInfoResolver Resolver)> BuildChain(
        bool developerPresent,
        bool developerFirst)
    {
        var chain = new List<(ResolverKind, IJsonTypeInfoResolver)>
        {
            (ResolverKind.Static, VistaStaticJsonContext.Default),
        };

        if (developerPresent && developerFirst)
        {
            chain.Add((ResolverKind.Developer, StyleASeamDeveloperJsonContext.Default));
        }

        // Drain the Core-resident store exactly as VistaJson does — casting each opaque handle to
        // IJsonTypeInfoResolver (the drain contract, R5.3). The covered Style A contexts are among them.
        foreach (var handle in GeneratedJsonContextStore.All)
        {
            chain.Add((ResolverKind.Generated, (IJsonTypeInfoResolver)handle));
        }

        if (developerPresent && !developerFirst)
        {
            chain.Add((ResolverKind.Developer, StyleASeamDeveloperJsonContext.Default));
        }

        chain.Add((ResolverKind.Reflection, ReflectionResolver));
        return chain;
    }

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration and the
    /// given chain shape. The reflection fallback stays enabled so any type no chained context covers still
    /// resolves — the property's claim (a covered top-level Style A DTO resolves from a generated context) is
    /// proven by <see cref="WinningKind"/> even with the fallback present.
    /// </summary>
    private static JsonSerializerOptions BuildSeamOptions(bool developerPresent, bool developerFirst)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());

        foreach (var (_, resolver) in BuildChain(developerPresent, developerFirst))
        {
            options.TypeInfoResolverChain.Add(resolver);
        }

        return options;
    }

    /// <summary>
    /// Builds the reflection oracle: the same seam <see cref="JsonSerializerOptions"/> configuration but with
    /// only the reflection resolver in the chain, so its output is the Behavioral_Oracle every seam
    /// configuration must match byte-for-byte.
    /// </summary>
    private static JsonSerializerOptions BuildOracleOptions()
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

    // -- Envelope builders ------------------------------------------------------------------------------

    private static PagedResult<T> BuildPaged<T>(IReadOnlyList<T> rows, PagingModel paging) =>
        new(rows, paging.TotalRows, paging.PageIndex, paging.PageSize, paging.TotalPages);

    private static ViewListResult<T> BuildListResult<T>(IReadOnlyList<T> rows, PagingModel paging) =>
        new(BuildPaged(rows, paging), paging.TotalRowsUnfiltered);

    // -- Value generators (mirror the JsonTypeInfo-phase parity generators) -----------------------------

    private static readonly string[] TextPool =
        { "", "Alice", "Bob", "naïve café", "a\"quoted\"b", "back\\slash", "tab\tend", "  spaced  " };

    private static Gen<string> Pick(string[] values) => Gen.Int[0, values.Length - 1].Select(i => values[i]);

    private static readonly Gen<byte[]> GenBytes =
        Gen.Int[0, 255].Select(i => (byte)i).Array[0, 8];

    // An optional DateTime with a fixed Kind (Unspecified) so the value space is well-defined; the STJ
    // default DateTime converter governs both the generated and oracle paths identically, so parity holds
    // regardless of the concrete instant.
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
}

/// <summary>
/// A developer-authored <c>App_Json_Context</c> (built by the built-in System.Text.Json source generator)
/// covering the same Serializable_DTO_Set as the covered Style A views' generated contexts. It stands in for
/// a still-registered developer context so the property can prove the generated Style A context wins ahead of
/// it (optionality) and that the JSON is identical whichever resolver serves the type (R10.2).
/// </summary>
[JsonSerializable(typeof(CatalogItemRow))]
[JsonSerializable(typeof(ViewListResult<CatalogItemRow>))]
[JsonSerializable(typeof(PagedResult<CatalogItemRow>))]
[JsonSerializable(typeof(SubscriptionRow))]
[JsonSerializable(typeof(ViewListResult<SubscriptionRow>))]
[JsonSerializable(typeof(PagedResult<SubscriptionRow>))]
[JsonSerializable(typeof(SubscriptionCrud))]
[JsonSerializable(typeof(AuditEntryCrud))]
internal sealed partial class StyleASeamDeveloperJsonContext : JsonSerializerContext
{
}
