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
/// The <b>master</b> oracle-parity property test for the source-generated per-view <c>JsonTypeInfo</c>
/// emitted for the covered <b>Style A</b> (central-template) views (spec style-a-coverage, task 8.2;
/// Decision Log D129/D130; Property 1; Requirements 3.1, 3.2, 3.3, 4.1, 4.3, 6.1, 6.2, 6.5, 10.4).
/// <para>
/// Unlike the seam-resolution property (task 6.2, Property 4) — which proves the drained Style A contexts
/// win the resolver chain and that the JSON is identical whichever chain slot serves a type — this property
/// exercises each covered view's generated context <b>directly, in isolation</b>: it resolves the view's
/// <see cref="IJsonTypeInfoResolver"/> from the Core-resident <see cref="GeneratedJsonContextStore"/> by its
/// <b>constant</b> <c>AddView</c> name and installs it as the <em>sole</em>
/// <see cref="JsonSerializerOptions.TypeInfoResolver"/> — <b>no</b> reflection fallback behind it. Because the
/// generated context is a self-contained type closure (it also emits the leaf/element/nullable/enum/collection
/// metadata every covered DTO reaches), using it alone both proves it is complete and pins the comparison to
/// the generated metadata rather than any fallback (R6.1, R6.2, R10.4).
/// </para>
/// <para>
/// The reference implementation is the <b>Behavioral_Oracle</b>: the same
/// <see cref="JsonSerializerOptions"/> configuration (web defaults, case-insensitive matching, the
/// <see cref="JsonStringEnumConverter"/> + <see cref="FilterNodeJsonConverter"/> converters) but with only
/// the reflection resolver (<see cref="DefaultJsonTypeInfoResolver"/>) in the chain. For every covered view
/// and every random value of its covered DTO set the property asserts:
/// </para>
/// <list type="number">
///   <item><description><b>Serialization parity</b> — the JSON produced through the generated context is
///   byte-for-byte identical to the JSON produced through the oracle under the same options (R3.1, R3.3,
///   R4.1, R6.1, R6.5).</description></item>
///   <item><description><b>Deserialization parity</b> — deserializing any valid body (the oracle's own JSON)
///   through the generated context yields an object equivalent to the one the oracle produces from the same
///   body, where equivalence is decided by re-serializing both through the oracle and comparing byte-for-byte
///   (R3.2, R4.3, R6.2).</description></item>
/// </list>
/// <para>
/// The covered DTO set quantified over is: <c>stylea-catalog-items</c> (read-only, <b>named</b> row) →
/// <see cref="CatalogItemRow"/> + its <see cref="ViewListResult{TRow}"/> / <see cref="PagedResult{TRow}"/>
/// envelopes; <c>stylea-subscriptions</c> (writable, named row) → <see cref="SubscriptionRow"/> + its two
/// envelopes + the record <see cref="SubscriptionCrud"/> (required + init-only members); and
/// <c>stylea-audit-entries</c> (writable, <b>anonymous</b> row) → <see cref="AuditEntryCrud"/> <b>only</b> —
/// the D96 asymmetry: the write model is nameable and covered while the anonymous read row stays on the
/// reflection path by design. The fixtures are compiled once; the property quantifies over random DTO
/// <em>values</em>, never re-compiling per iteration (design "The parity oracle" / cost control).
/// </para>
/// </summary>
/// <remarks>
/// The reflection oracle is <see cref="RequiresUnreferencedCode"/> by nature (that is the whole point of the
/// generated path); trimming/AOT are not used for the test host, so the trim/AOT analyzers are suppressed
/// here exactly as in the sibling Style A parity tests.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The oracle drives the reflection resolver by design; trimming is not used for tests.")]
[SuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with RequiresDynamicCodeAttribute may break functionality when AOT compiling",
    Justification = "The oracle drives the reflection resolver by design; AOT is not used for tests.")]
public sealed class StyleAOracleParityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    // Feature: style-a-coverage, Property 1: Serialization parity with the reflection oracle (master,
    // model-based).
    //
    // For any covered Style A view and for any value of its covered DTO set (a named TRow with arbitrary
    // member values; a ViewListResult<TRow> with arbitrary rows, paging, and both totals; a
    // PagedResult<TRow>; and — for a writable view — a TCrud), the JSON produced by serializing the value
    // through the generated per-view context is byte-for-byte identical to the JSON produced by serializing
    // it through the Behavioral_Oracle under the same JsonSerializerOptions; and the object produced by
    // deserializing any valid body through the generated context is equivalent to the object produced by the
    // Behavioral_Oracle.
    //
    // Validates: Requirements 3.1, 3.2, 3.3, 4.1, 4.3, 6.1, 6.2, 6.5, 10.4
    [Test]
    public void Generated_StyleA_Context_Serialization_And_Deserialization_Match_The_Reflection_Oracle()
    {
        // Resolve each covered view's generated context up front (fails fast, with a clear message, if the
        // fixture assembly's [ModuleInitializer] did not register it — the same guard the seam property uses).
        var catalogOptions = GeneratedOptions(GeneratorStyleASampleViews.CatalogItemsViewName);
        var subscriptionOptions = GeneratedOptions(GeneratorStyleASampleViews.SubscriptionsViewName);
        var auditOptions = GeneratedOptions(GeneratorStyleASampleViews.AuditEntriesViewName);
        var oracleOptions = BuildOracleOptions();

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
                AssertParity(catRow, catalogOptions, oracleOptions);
                AssertParity(BuildPaged(catRows, paging), catalogOptions, oracleOptions);
                AssertParity(BuildListResult(catRows, paging), catalogOptions, oracleOptions);

                // stylea-subscriptions — writable, named row: read DTOs + the record TCrud (required + init).
                AssertParity(subRow, subscriptionOptions, oracleOptions);
                AssertParity(BuildPaged(subRows, paging), subscriptionOptions, oracleOptions);
                AssertParity(BuildListResult(subRows, paging), subscriptionOptions, oracleOptions);
                AssertParity(subCrud, subscriptionOptions, oracleOptions);

                // stylea-audit-entries — writable, ANONYMOUS row: the named TCrud only (the D96 asymmetry).
                AssertParity(auditCrud, auditOptions, oracleOptions);
            },
            iter: Iterations);
    }

    // -- Parity assertion -------------------------------------------------------------------------------

    /// <summary>
    /// Asserts full oracle parity for <paramref name="value"/>: (1) serializing it through the generated
    /// per-view context produces JSON byte-for-byte identical to the oracle's JSON (R6.1); and (2)
    /// deserializing that valid body through the generated context yields an object equivalent to the
    /// oracle's — proven by re-serializing both through the oracle and comparing byte-for-byte (R6.2).
    /// </summary>
    private static void AssertParity<T>(T value, JsonSerializerOptions generatedOptions, JsonSerializerOptions oracleOptions)
    {
        // (1) Serialization parity.
        var oracleJson = JsonSerializer.Serialize(value, oracleOptions);
        var generatedJson = JsonSerializer.Serialize(value, generatedOptions);

        if (!string.Equals(generatedJson, oracleJson, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Serializing '{typeof(T)}' through the generated Style A context produced JSON differing " +
                $"from the reflection oracle under the same options.\n" +
                $"  generated: {generatedJson}\n  oracle:    {oracleJson}");
        }

        // (2) Deserialization parity: the oracle JSON is a valid body; deserializing it through the generated
        // context must yield an object equivalent to the oracle's. Equivalence is decided structurally by
        // re-serializing both results through the oracle and comparing byte-for-byte.
        var fromGenerated = JsonSerializer.Deserialize<T>(oracleJson, generatedOptions);
        var fromOracle = JsonSerializer.Deserialize<T>(oracleJson, oracleOptions);

        var reserializedGenerated = JsonSerializer.Serialize(fromGenerated, oracleOptions);
        var reserializedOracle = JsonSerializer.Serialize(fromOracle, oracleOptions);

        if (!string.Equals(reserializedGenerated, reserializedOracle, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Deserializing a valid body of '{typeof(T)}' through the generated Style A context produced " +
                $"an object not equivalent to the reflection oracle's.\n" +
                $"  body:                {oracleJson}\n" +
                $"  generated (re-ser):  {reserializedGenerated}\n" +
                $"  oracle (re-ser):     {reserializedOracle}");
        }
    }

    // -- Options construction ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration (web
    /// defaults, case-insensitive matching, the enum + <c>FilterNodeJsonConverter</c> converters) whose
    /// <em>sole</em> resolver is the generated per-view context registered under <paramref name="viewName"/>
    /// in <see cref="GeneratedJsonContextStore"/>. No reflection fallback is chained behind it, so every type
    /// this options resolves is served by the generated metadata (or the serialization/deserialization fails,
    /// surfacing an incomplete generated closure).
    /// </summary>
    private static JsonSerializerOptions GeneratedOptions(string viewName)
    {
        if (!GeneratedJsonContextStore.TryGet(viewName, out var handle))
        {
            throw new Exception(
                $"No generated JsonTypeInfo context is registered for Style A view '{viewName}'. Its per-view " +
                "context must be registered by the a2n.Vista.GeneratorStyleASample [ModuleInitializer] into " +
                "GeneratedJsonContextStore, keyed by the constant AddView name.");
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        options.TypeInfoResolver = (IJsonTypeInfoResolver)handle;
        return options;
    }

    /// <summary>
    /// Builds the reflection oracle: the same seam <see cref="JsonSerializerOptions"/> configuration but with
    /// only the reflection resolver in the chain, so its output is the Behavioral_Oracle the generated path
    /// must match byte-for-byte.
    /// </summary>
    private static JsonSerializerOptions BuildOracleOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        return options;
    }

    // -- Envelope builders ------------------------------------------------------------------------------

    private static PagedResult<T> BuildPaged<T>(IReadOnlyList<T> rows, PagingModel paging) =>
        new(rows, paging.TotalRows, paging.PageIndex, paging.PageSize, paging.TotalPages);

    private static ViewListResult<T> BuildListResult<T>(IReadOnlyList<T> rows, PagingModel paging) =>
        new(BuildPaged(rows, paging), paging.TotalRowsUnfiltered);

    // -- Value generators (mirror the JsonTypeInfo-phase / seam-resolution parity generators) -----------

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
