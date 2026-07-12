using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.GeneratorHttpSurfaceSample;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Unit examples for the coexistence and non-regression contract of the generated per-view
/// <c>JsonTypeInfo</c> seam integration (spec source-generator-json-typeinfo, task 6.3; Decision Log
/// D125/D126; Requirements 5.2, 5.3, 5.4, 5.5, 10.2, 10.3). They assert four things about the seam once
/// the source-generated per-view contexts are chained ahead of the reflection fallback:
/// <list type="number">
///   <item><description>a covered view's DTO resolves from a <c>Generated_View_Context</c> — not the
///   reflection fallback — <b>whether or not</b> a developer <c>App_Json_Context</c> is registered, and
///   the JSON is byte-for-byte identical either way and equal to the reflection oracle (R5.2, R5.3,
///   R10.2);</description></item>
///   <item><description>a type no chained context covers still (de)serializes through the reflection
///   fallback, exactly as before this feature (R5.5, R10.3);</description></item>
///   <item><description>opting the reflection fallback out removes the reflection branch — a covered DTO
///   still resolves from its generated context while an uncovered type no longer resolves at all
///   (R5.5);</description></item>
///   <item><description>the shipped <see cref="VistaStaticJsonContext"/> retains precedence for the fixed
///   envelope/response types even with the generated contexts chained (R5.4).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared-static isolation.</b> <see cref="VistaJson.Options"/> is a process-wide static whose resolver
/// chain freezes on first use, and <see cref="VistaJson.DisableReflectionFallback"/> mutates it
/// irreversibly. Mutating it here would corrupt the shared static every other test (and the sibling
/// seam-resolution property test) depends on. Each example therefore builds a <b>fresh</b>
/// <see cref="JsonSerializerOptions"/> that mirrors the seam chain exactly — the same construction
/// <see cref="VistaJson"/> performs (<see cref="VistaStaticJsonContext"/> first, then the generated
/// per-view contexts drained from <see cref="GeneratedJsonContextStore"/>, then any developer context,
/// then the opt-out-able reflection fallback) — proving the behavior without touching global state.
/// </para>
/// <para>
/// <b>Covered fixtures.</b> The covered views come from the referenced <c>a2n.Vista.GeneratorHttpSurfaceSample</c>
/// assembly, whose <c>[ModuleInitializer]</c>s register the generated per-view contexts into the
/// <see cref="GeneratedJsonContextStore"/> at module load. <see cref="ProductView"/> (read-only) and
/// <see cref="EmployeeView"/> (writable) provide the covered read DTO (<see cref="ProductRow"/>) and write
/// DTO (<see cref="EmployeeCrud"/>) exercised here. No new fixtures are declared, so there is no first-wins
/// store collision with the sibling seam tasks.
/// </para>
/// </remarks>
public sealed class SeamGeneratedContextCoexistenceTests
{
    // -- R5.2/R5.3/R10.2: a covered read DTO resolves from the generated context, developer absent OR ---
    // -- present, byte-equal either way and equal to the reflection oracle. ----------------------------

    [Test]
    public async Task CoveredReadDto_ResolvesFromGeneratedContext_WhetherDeveloperContextIsAbsentOrPresent()
    {
        // RegionTerritoryRow's members are all leaf types the Static_Envelope_Context covers (int/string),
        // so a generated + static chain serializes it AOT-clean with no reflection fallback needed.
        var row = new RegionTerritoryRow { RegionId = 3, TerritoryId = "98101", Description = "Seattle" };

        // The shipped envelope context covers no per-view DTO — so if a generated + static chain resolves
        // it, the resolver that served the DTO can only be a Generated_View_Context (R5.2).
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(RegionTerritoryRow))).IsNull();

        var developerAbsent = BuildSeam(includeGenerated: true, developer: null, includeReflection: false);
        var developerPresent = BuildSeam(includeGenerated: true, developer: RegionTerritoryJsonContext.Default, includeReflection: false);

        // A generated context covers the read DTO and its List/Paged envelopes over TRow.
        await Assert.That(AnyGeneratedContextCovers(typeof(RegionTerritoryRow), developerAbsent)).IsTrue();
        await Assert.That(AnyGeneratedContextCovers(typeof(ViewListResult<RegionTerritoryRow>), developerAbsent)).IsTrue();
        await Assert.That(AnyGeneratedContextCovers(typeof(PagedResult<RegionTerritoryRow>), developerAbsent)).IsTrue();

        // Coexistence: the JSON is identical whether or not the developer App_Json_Context is registered
        // (the generated context wins in both chains), and identical to the reflection oracle (R5.3/R10.2).
        var jsonAbsent = JsonSerializer.Serialize(row, developerAbsent);
        var jsonPresent = JsonSerializer.Serialize(row, developerPresent);
        var jsonOracle = JsonSerializer.Serialize(row, BuildReflectionOracle());

        await Assert.That(jsonAbsent).IsEqualTo(jsonPresent);
        await Assert.That(jsonAbsent).IsEqualTo(jsonOracle);
    }

    // -- R5.2/R5.3/R10.2 (write side): a covered write DTO resolves from the generated context too. -----

    [Test]
    public async Task CoveredWriteDto_ResolvesFromGeneratedContext_WhetherDeveloperContextIsAbsentOrPresent()
    {
        var crud = new EmployeeCrud { FullName = "Ada Lovelace", Title = "Engineer", ReportsTo = 7 };

        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(EmployeeCrud))).IsNull();

        var developerAbsent = BuildSeam(includeGenerated: true, developer: null, includeReflection: false);
        var developerPresent = BuildSeam(includeGenerated: true, developer: EmployeeJsonContext.Default, includeReflection: false);

        await Assert.That(AnyGeneratedContextCovers(typeof(EmployeeCrud), developerAbsent)).IsTrue();

        var jsonAbsent = JsonSerializer.Serialize(crud, developerAbsent);
        var jsonPresent = JsonSerializer.Serialize(crud, developerPresent);
        var jsonOracle = JsonSerializer.Serialize(crud, BuildReflectionOracle());

        await Assert.That(jsonAbsent).IsEqualTo(jsonPresent);
        await Assert.That(jsonAbsent).IsEqualTo(jsonOracle);
    }

    // -- R5.5/R10.3: a type no chained context covers still rides the reflection fallback, unchanged. ---

    [Test]
    public async Task UncoveredType_StillFallsBackToReflectionResolver_And_RoundTrips()
    {
        var seam = BuildSeam(includeGenerated: true, developer: null, includeReflection: true);

        // Neither the envelope context nor any generated per-view context covers this app DTO.
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(UncoveredDto))).IsNull();
        await Assert.That(AnyGeneratedContextCovers(typeof(UncoveredDto), seam)).IsFalse();

        // Yet the seam still resolves and round-trips it — via the reflection fallback (R5.5/R10.3).
        await Assert.That(seam.GetTypeInfo(typeof(UncoveredDto))).IsNotNull();

        var value = new UncoveredDto(7, "reflection", true);
        var json = JsonSerializer.Serialize(value, seam);
        var back = JsonSerializer.Deserialize<UncoveredDto>(json, seam);
        await Assert.That(back).IsEqualTo(value);
    }

    // -- R5.5: opting the reflection fallback out removes the reflection branch. ------------------------

    [Test]
    public async Task DisablingReflectionFallback_RemovesReflectionBranch_But_CoveredDtoStillResolves()
    {
        // Mirror the chain WITHOUT the reflection fallback — the shape DisableReflectionFallback leaves.
        var noFallback = BuildSeam(includeGenerated: true, developer: null, includeReflection: false);

        // A covered view DTO still resolves from its generated context with no reflection branch present,
        // and serializes AOT-clean (its leaf members are covered by the Static_Envelope_Context).
        await Assert.That(AnyGeneratedContextCovers(typeof(RegionTerritoryRow), noFallback)).IsTrue();
        var row = new RegionTerritoryRow { RegionId = 1, TerritoryId = "T1", Description = "Still covered" };
        await Assert.That(() => JsonSerializer.Serialize(row, noFallback)).ThrowsNothing();

        // An uncovered type no longer resolves: the reflection branch is gone.
        var resolver = noFallback.TypeInfoResolver;
        await Assert.That(resolver).IsNotNull();
        await Assert.That(resolver!.GetTypeInfo(typeof(UncoveredDto), noFallback)).IsNull();

        // Serialization of an uncovered type therefore throws instead of silently reflecting.
        await Assert.That(() => JsonSerializer.Serialize(new UncoveredDto(1, "x", false), noFallback))
            .Throws<NotSupportedException>();
    }

    // -- R5.4: the shipped envelope context keeps precedence with generated contexts chained. -----------

    [Test]
    public async Task EnvelopeTypes_StillResolveFromStaticEnvelopeContext_WithGeneratedContextsChained()
    {
        var seam = BuildSeam(includeGenerated: true, developer: null, includeReflection: true);

        // The fixed envelope/response type is served by the shipped Static_Envelope_Context, which is
        // first in the chain, and is NOT claimed by any per-view generated context (R5.4).
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

        // The envelope still serializes byte-for-byte as the shipped context alone produces it, so the
        // generated per-view contexts changed only the resolution of per-view DTOs, not the envelopes.
        var envelopeOnly = BuildSeam(includeGenerated: false, developer: null, includeReflection: false);
        await Assert.That(JsonSerializer.Serialize(field, seam))
            .IsEqualTo(JsonSerializer.Serialize(field, envelopeOnly));
    }

    // -- Helpers ----------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration and
    /// resolver order (Decision Log D124/D126): the shipped <see cref="VistaStaticJsonContext"/> first,
    /// then — when <paramref name="includeGenerated"/> — every generated per-view context drained from
    /// <see cref="GeneratedJsonContextStore"/>, then an optional developer <c>App_Json_Context</c>, then —
    /// when <paramref name="includeReflection"/> — the opt-out-able reflection fallback last.
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
    /// A reflection-only mirror of the seam configuration (the Behavioral_Oracle): identical options with
    /// a single <see cref="DefaultJsonTypeInfoResolver"/>, used to prove byte-for-byte parity.
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
    /// <c>IJsonTypeInfoResolver</c> by contract) and reports whether any generated per-view context
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

    /// <summary>
    /// A pure-value DTO covered by no chained context — it exercises the reflection fallback. Members are
    /// deliberately scalar so record structural equality is a faithful round-trip check.
    /// </summary>
    public sealed record UncoveredDto(int Number, string Text, bool Flag);
}
