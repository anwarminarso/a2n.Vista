// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the resolution order of the Vista serialization seam
/// (spec source-generator-http-surface, task 7.5; Decision Log D124; Requirement 5.3).
/// <para>
/// The seam is <see cref="VistaJson.Options"/> configured with a
/// <see cref="JsonSerializerOptions.TypeInfoResolverChain"/> whose deterministic order is:
/// the shipped <see cref="VistaStaticJsonContext"/> (the <c>Static_Envelope_Context</c>) first, then any
/// developer-authored <c>App_Json_Context</c>(s) chained by <c>AddVistaJsonContext</c> in registration
/// order, and the reflection fallback (<see cref="DefaultJsonTypeInfoResolver"/>) appended last. This
/// property proves that, for a runtime type covered by a chained source-generated context, the seam
/// resolves the type's <see cref="JsonTypeInfo"/> from that source-gen context (never the reflection
/// fallback), and that the winning resolver is always the earliest chain position that covers the type —
/// static context, then app contexts, then reflection fallback last.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared-static isolation.</b> <see cref="VistaJson.Options"/> is a process-wide static whose resolver
/// chain freezes on first use, and <see cref="VistaJson.AddContext"/> /
/// <see cref="VistaJson.DisableReflectionFallback"/> mutate it irreversibly. Repeatedly mutating it across
/// property iterations would corrupt the shared static every other test in the process depends on. Each
/// iteration therefore builds a <b>fresh</b> <see cref="JsonSerializerOptions"/> that mirrors the seam
/// chain exactly (the same construction <c>VistaJson</c> performs), wrapping every chained resolver in a
/// <see cref="TrackingResolver"/> so the test can observe which chain position actually served the type.
/// </para>
/// <para>
/// <b>Winner observation.</b> A <see cref="JsonSerializerOptions.TypeInfoResolver"/> combined from a chain
/// queries each resolver in order and returns the first non-<see langword="null"/>
/// <see cref="JsonTypeInfo"/>. Each <see cref="TrackingResolver"/> records its own chain index when it is
/// the one that returns a non-null result; because the combined resolver stops at the first hit, the
/// recorded index is exactly the winning position.
/// </para>
/// </remarks>
public sealed class SeamResolutionOrderPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    /// <summary>Identifies a resolver slot's role in the mirrored seam chain.</summary>
    private enum ResolverKind
    {
        /// <summary>The shipped <see cref="VistaStaticJsonContext"/> (fixed envelopes), always first.</summary>
        Static,

        /// <summary>Developer-authored <c>App_Json_Context</c> A.</summary>
        AppA,

        /// <summary>Developer-authored <c>App_Json_Context</c> B.</summary>
        AppB,

        /// <summary>The reflection fallback (<see cref="DefaultJsonTypeInfoResolver"/>), always last.</summary>
        Reflection,
    }

    // The runtime types the property probes, drawn from three coverage regions:
    //   * a fixed envelope covered only by the static context,
    //   * view DTOs covered by the app contexts (one overlapping both to exercise app-order precedence),
    //   * a plain DTO covered by no source-gen context (reflection-only).
    private static readonly Type[] ProbeTypes =
    {
        typeof(VistaFieldMetadataResponse),      // Static context only (among source-gen)
        typeof(WidgetRow),                       // AppA and AppB (overlap)
        typeof(ViewListResult<WidgetRow>),       // AppA only
        typeof(GadgetRow),                       // AppB only
        typeof(PagedResult<GadgetRow>),          // AppB only
        typeof(PlainDto),                        // no source-gen context — reflection only
    };

    // Feature: source-generator-http-surface, Property 3: The Serialization_Seam resolves covered types
    // from the chained source-gen contexts.
    //
    // Validates: Requirements 5.3
    [Test]
    public void Seam_Resolves_Covered_Types_From_SourceGen_Contexts_In_Deterministic_Order()
    {
        // Feature: source-generator-http-surface, Property 3: The Serialization_Seam resolves covered
        // types from the chained source-gen contexts.
        var genCase =
            // Which developer contexts are chained, and in what registration order (0..4), varying both
            // the app-context set and the relative order of the overlapping ones (WidgetRow ∈ A ∩ B).
            from appMode in Gen.Int[0, 4]
            // Whether the reflection fallback is present (an AOT-clean app opts it out).
            from reflectionEnabled in Gen.Bool
            select (appMode, reflectionEnabled);

        genCase.Sample(
            tuple =>
            {
                var (appMode, reflectionEnabled) = tuple;
                var order = AppOrderFor(appMode);

                // The deterministic seam chain: static context first, app contexts in registration order,
                // reflection fallback last (when enabled) — the exact order VistaJson builds.
                var chain = new List<ResolverKind> { ResolverKind.Static };
                chain.AddRange(order);
                if (reflectionEnabled)
                {
                    chain.Add(ResolverKind.Reflection);
                }

                // Two independently-built options with the same configuration prove build determinism:
                // identical chains must select the identical winning resolver for every probe type.
                var recorderA = new WinnerRecorder();
                var recorderB = new WinnerRecorder();
                var optionsA = BuildSeamOptions(chain, recorderA);
                var optionsB = BuildSeamOptions(chain, recorderB);
                var combinedA = optionsA.TypeInfoResolver!;
                var combinedB = optionsB.TypeInfoResolver!;

                foreach (var type in ProbeTypes)
                {
                    var expectedIndex = FirstCoveringIndex(chain, type);

                    recorderA.Index = -1;
                    var infoA = combinedA.GetTypeInfo(type, optionsA);

                    recorderB.Index = -1;
                    var infoB = combinedB.GetTypeInfo(type, optionsB);

                    if (expectedIndex < 0)
                    {
                        // No chained resolver covers the type (reflection opted out, source-gen miss):
                        // the seam yields no JsonTypeInfo — the type simply cannot be (de)serialized.
                        if (infoA is not null || recorderA.Index != -1)
                        {
                            throw new Exception(
                                $"Type '{type}' should resolve to nothing (chain [{Describe(chain)}] covers " +
                                $"it nowhere), but the seam served it from position {recorderA.Index}.");
                        }

                        if (infoB is not null)
                        {
                            throw new Exception(
                                $"Type '{type}' resolved to null in one build but non-null in an identically " +
                                "configured build — resolution is not deterministic.");
                        }

                        continue;
                    }

                    // A covering resolver exists: the seam must serve a JsonTypeInfo, and it must come from
                    // the EARLIEST covering chain position (static before app before reflection).
                    if (infoA is null)
                    {
                        throw new Exception(
                            $"Type '{type}' is covered at position {expectedIndex} of chain " +
                            $"[{Describe(chain)}] but the seam returned no JsonTypeInfo.");
                    }

                    if (recorderA.Index != expectedIndex)
                    {
                        throw new Exception(
                            $"Type '{type}' was served from position {recorderA.Index} " +
                            $"({chain[recorderA.Index]}) but the earliest covering position is " +
                            $"{expectedIndex} ({chain[expectedIndex]}); chain [{Describe(chain)}].");
                    }

                    // The core guarantee (R5.3): whenever a source-gen context covers the type, the seam
                    // resolves it from that context, NOT from the reflection fallback.
                    var winner = chain[recorderA.Index];
                    if (SourceGenCovers(type, chain) && winner == ResolverKind.Reflection)
                    {
                        throw new Exception(
                            $"Type '{type}' is covered by a chained source-gen context but the seam served " +
                            $"it from the reflection fallback; chain [{Describe(chain)}].");
                    }

                    // Build determinism: the second, identically-configured build selects the same winner.
                    if (infoB is null || recorderB.Index != recorderA.Index)
                    {
                        throw new Exception(
                            $"Type '{type}' resolved from position {recorderA.Index} in one build but " +
                            $"{recorderB.Index} in an identically configured build — resolution is not " +
                            "deterministic.");
                    }
                }
            },
            iter: Iterations);
    }

    // -- Chain construction & coverage model ------------------------------------------------------------

    /// <summary>Maps the generated <paramref name="appMode"/> to a developer-context registration order.</summary>
    private static IReadOnlyList<ResolverKind> AppOrderFor(int appMode) => appMode switch
    {
        0 => Array.Empty<ResolverKind>(),
        1 => new[] { ResolverKind.AppA },
        2 => new[] { ResolverKind.AppB },
        3 => new[] { ResolverKind.AppA, ResolverKind.AppB },
        _ => new[] { ResolverKind.AppB, ResolverKind.AppA },
    };

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration (web
    /// defaults, case-insensitive matching, the enum + <see cref="FilterNodeJsonConverter"/> converters)
    /// and the given resolver <paramref name="chain"/>, wrapping every slot in a
    /// <see cref="TrackingResolver"/> so the winning position can be observed.
    /// </summary>
    private static JsonSerializerOptions BuildSeamOptions(IReadOnlyList<ResolverKind> chain, WinnerRecorder recorder)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());

        for (var i = 0; i < chain.Count; i++)
        {
            options.TypeInfoResolverChain.Add(new TrackingResolver(i, ResolverFor(chain[i]), recorder));
        }

        return options;
    }

    /// <summary>Returns the underlying resolver instance backing a chain slot of the given kind.</summary>
    private static IJsonTypeInfoResolver ResolverFor(ResolverKind kind) => kind switch
    {
        ResolverKind.Static => VistaStaticJsonContext.Default,
        ResolverKind.AppA => SeamAppContextA.Default,
        ResolverKind.AppB => SeamAppContextB.Default,
        _ => ReflectionResolver,
    };

    /// <summary>The index of the earliest chain slot whose resolver covers <paramref name="type"/>, or -1.</summary>
    private static int FirstCoveringIndex(IReadOnlyList<ResolverKind> chain, Type type)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            if (Covers(chain[i], type))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether some source-generated (non-reflection) slot present in the chain covers the type.</summary>
    private static bool SourceGenCovers(Type type, IReadOnlyList<ResolverKind> chain)
    {
        foreach (var kind in chain)
        {
            if (kind != ResolverKind.Reflection && Covers(kind, type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The known coverage of each resolver over the probe-type pool. The app contexts declare their sets
    /// via <c>[JsonSerializable]</c> below; this mirror lets the property compute the expected winner
    /// without depending on the reflection fallback to disambiguate.
    /// </summary>
    private static bool Covers(ResolverKind kind, Type type) => kind switch
    {
        ResolverKind.Static => type == typeof(VistaFieldMetadataResponse),
        ResolverKind.AppA =>
            type == typeof(WidgetRow)
            || type == typeof(ViewListResult<WidgetRow>)
            || type == typeof(PagedResult<WidgetRow>),
        ResolverKind.AppB =>
            type == typeof(GadgetRow)
            || type == typeof(ViewListResult<GadgetRow>)
            || type == typeof(PagedResult<GadgetRow>)
            || type == typeof(WidgetRow),
        // The reflection fallback resolves every concrete, serializable type in the probe pool.
        _ => true,
    };

    private static string Describe(IReadOnlyList<ResolverKind> chain) => string.Join(" → ", chain);

    // -- Infrastructure ---------------------------------------------------------------------------------

    /// <summary>
    /// The reflection fallback resolver, exercised deliberately by the test (trimming is not used for
    /// tests). Held as a single shared instance mirroring the seam's own singleton fallback.
    /// </summary>
    private static readonly IJsonTypeInfoResolver ReflectionResolver = new DefaultJsonTypeInfoResolver();

    /// <summary>Holds the chain index of the resolver that last served a non-null <see cref="JsonTypeInfo"/>.</summary>
    private sealed class WinnerRecorder
    {
        public int Index = -1;
    }

    /// <summary>
    /// A pass-through <see cref="IJsonTypeInfoResolver"/> that records its chain position into the shared
    /// <see cref="WinnerRecorder"/> whenever it produces a non-null <see cref="JsonTypeInfo"/>. Because the
    /// combined chain resolver stops at the first non-null result, the recorded index is the winner.
    /// </summary>
    private sealed class TrackingResolver : IJsonTypeInfoResolver
    {
        private readonly int _index;
        private readonly IJsonTypeInfoResolver _inner;
        private readonly WinnerRecorder _recorder;

        public TrackingResolver(int index, IJsonTypeInfoResolver inner, WinnerRecorder recorder)
        {
            _index = index;
            _inner = inner;
            _recorder = recorder;
        }

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var info = _inner.GetTypeInfo(type, options);
            if (info is not null)
            {
                _recorder.Index = _index;
            }

            return info;
        }
    }

    // -- Probe DTOs and developer App_Json_Contexts -----------------------------------------------------

    /// <summary>A representative projected row type covered by both app contexts (overlap probe).</summary>
    public sealed record WidgetRow(int Id, string Name);

    /// <summary>A representative projected row type covered only by app context B.</summary>
    public sealed record GadgetRow(long Code, string Label);

    /// <summary>A plain DTO covered by no source-gen context — it can only ride the reflection fallback.</summary>
    public sealed record PlainDto(int Number, string Text);
}

/// <summary>
/// A developer-authored <c>App_Json_Context</c> (source-generated) covering the <c>WidgetRow</c> view DTOs
/// — the exact <c>[JsonSerializable]</c> set VISTA0041 guidance names for such a view (<c>TRow</c>,
/// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>).
/// </summary>
[JsonSerializable(typeof(SeamResolutionOrderPropertyTests.WidgetRow))]
[JsonSerializable(typeof(ViewListResult<SeamResolutionOrderPropertyTests.WidgetRow>))]
[JsonSerializable(typeof(PagedResult<SeamResolutionOrderPropertyTests.WidgetRow>))]
internal sealed partial class SeamAppContextA : JsonSerializerContext
{
}

/// <summary>
/// A second developer-authored <c>App_Json_Context</c> covering the <c>GadgetRow</c> view DTOs and, to
/// exercise app-context ordering precedence, also <c>WidgetRow</c> (overlapping context A).
/// </summary>
[JsonSerializable(typeof(SeamResolutionOrderPropertyTests.GadgetRow))]
[JsonSerializable(typeof(ViewListResult<SeamResolutionOrderPropertyTests.GadgetRow>))]
[JsonSerializable(typeof(PagedResult<SeamResolutionOrderPropertyTests.GadgetRow>))]
[JsonSerializable(typeof(SeamResolutionOrderPropertyTests.WidgetRow))]
internal sealed partial class SeamAppContextB : JsonSerializerContext
{
}
