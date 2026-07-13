// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the first-wins idempotence of the two Core stores this feature reuses to hold
/// generated Style A artifacts — <see cref="ViewAccessorRegistry"/> (Decision Log D117) and
/// <see cref="GeneratedJsonContextStore"/> (Decision Log D125) — keyed by a Style A view name
/// (style-a-coverage task 1.2; Decision Log D129). Style A views are <c>AddView("name", ...)</c> call
/// sites, so their generated accessor maps and per-view JSON contexts are registered by
/// <c>[ModuleInitializer]</c>s keyed by the <em>constant</em> <c>AddView</c> name — a valid store key with
/// no contract change. Because a view's module may be initialized more than once in a process (multiple
/// hosts/test assemblies, repeated <c>AddVista</c> calls), each store must keep the <em>first</em> artifact
/// registered under each name, ignore later registrations for that name, and never disturb artifacts
/// registered under other names (Requirements 5.1, 5.2, 10.1).
/// </summary>
/// <remarks>
/// Both stores are process-wide statics, so every generated case uses a fresh <see cref="Guid"/>-unique
/// name prefix to stay isolated from sibling tests and from any module-initializer registrations already
/// present in the process. This property exercises the reused stores DIRECTLY with unique per-iteration
/// Style A view names — no compilation is involved. Registration identity is made observable by tagging
/// each artifact with a unique integer: a <see cref="TagContext"/> for the JSON store (a real
/// <see cref="IJsonTypeInfoResolver"/>, faithful to the store's opaque-handle contract) and a single-field
/// accessor map that returns the tag for the accessor registry. The property covers sequential repeated
/// registration (deterministic first-wins) and a concurrent-registration segment (exactly one winner per
/// store, stable across reads and immune to later adds).
/// </remarks>
public sealed class StyleAStoreFirstWinsIdempotencePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The single field a tagged Style A accessor map exposes, carrying the registration tag.</summary>
    private const string TagField = "Tag";

    /// <summary>A shared, side-effect-free row object passed to tag accessors (which ignore their input).</summary>
    private static readonly object DummyRow = new();

    /// <summary>Character set for the randomly generated Style A-style view-name token.</summary>
    private static readonly char[] NameChars = "abcdefghijklmnopqrstuvwxyz0123456789_".ToCharArray();

    /// <summary>A random Style A-style view-name token (lowercase letters, digits, underscore).</summary>
    private static readonly Gen<string> GenNameToken =
        from length in Gen.Int[1, 12]
        from indices in Gen.Int[0, NameChars.Length - 1].Array[length]
        select new string(Array.ConvertAll(indices, i => NameChars[i]));

    // Feature: style-a-coverage, Property 7: Store first-wins idempotence for Style A keys
    //
    // For any sequence of registrations into ViewAccessorRegistry and GeneratedJsonContextStore (with
    // repeated Style A view names, in any order and count), each store retains the first artifact
    // registered under each name, discards later registrations for that name, and leaves other names
    // unchanged.
    //
    // Validates: Requirements 5.1, 5.2, 10.1
    [Test]
    public void Stores_Retain_First_Registration_Per_StyleA_Name_And_Leave_Other_Names_Unchanged()
    {
        // A case is: how many distinct Style A view names participate, a random Style A-style token per
        // name, and the ordered registration sequence (each element names which of those views is being
        // registered). Repeats within the sequence are exactly the "same Style A name registered again"
        // scenario the first-wins rule must survive.
        var genCase =
            from nameCount in Gen.Int[1, 4]
            from tokens in GenNameToken.Array[nameCount]
            from sequence in Gen.Int[0, nameCount - 1].List[1, 15]
            select (nameCount, tokens, sequence);

        genCase.Sample(
            tuple =>
            {
                var (nameCount, tokens, sequence) = tuple;

                // Guid-unique per case so this case's names never collide with any other case, test, or
                // pre-existing module-initializer registration in the process. The random token gives the
                // names their "Style A" flavor; the trailing index guarantees within-case distinctness
                // even when two random tokens happen to coincide.
                var prefix = Guid.NewGuid().ToString("N");
                var names = new string[nameCount];
                for (var i = 0; i < nameCount; i++)
                {
                    names[i] = $"styleA-{prefix}-view{i}-{tokens[i]}";
                }

                // The expected winner per name = the tag of its FIRST registration in declaration order.
                var expectedFirstTag = new int?[nameCount];

                for (var seq = 0; seq < sequence.Count; seq++)
                {
                    var nameIndex = sequence[seq];
                    var tag = seq; // Unique per registration, so the winner is unambiguous.

                    // Register the SAME tag into BOTH reused stores under the SAME Style A name.
                    GeneratedJsonContextStore.Register(names[nameIndex], new TagContext(tag));
                    ViewAccessorRegistry.Register(names[nameIndex], TaggedAccessorMap(tag));

                    expectedFirstTag[nameIndex] ??= tag;
                }

                // First-wins retention + later-registration discard: every name registered at least once
                // must resolve — in BOTH stores — to the artifact from its FIRST registration, regardless
                // of how many later registrations targeted the same name, and must do so consistently on
                // repeated lookups. Each name keeping its own first tag also demonstrates that a
                // registration under one name leaves the other names unchanged.
                for (var i = 0; i < nameCount; i++)
                {
                    if (expectedFirstTag[i] is not int expected)
                    {
                        continue; // This name never appeared in the sequence.
                    }

                    for (var read = 0; read < 3; read++)
                    {
                        if (!GeneratedJsonContextStore.TryGet(names[i], out var storedContext))
                        {
                            throw new Exception(
                                $"GeneratedJsonContextStore had no context for '{names[i]}', expected the " +
                                $"first-registered context tagged {expected}.");
                        }

                        var actualContextTag = ((TagContext)storedContext).Tag;
                        if (actualContextTag != expected)
                        {
                            throw new Exception(
                                $"GeneratedJsonContextStore retained the context tagged {actualContextTag} " +
                                $"for '{names[i]}', expected the first-registered {expected} (first-wins violated).");
                        }

                        var actualAccessorTag = ReadAccessorTag(names[i]);
                        if (actualAccessorTag != expected)
                        {
                            throw new Exception(
                                $"ViewAccessorRegistry retained the accessor tagged {actualAccessorTag} " +
                                $"for '{names[i]}', expected the first-registered {expected} (first-wins violated).");
                        }
                    }
                }

                // Non-interference: a fresh name that was never registered in this case must be absent from
                // BOTH stores — no registration under any participating name may leak onto an unrelated name.
                var neverRegistered = $"styleA-{prefix}-absent";
                if (GeneratedJsonContextStore.TryGet(neverRegistered, out _))
                {
                    throw new Exception(
                        $"Unregistered name '{neverRegistered}' unexpectedly resolved in GeneratedJsonContextStore; " +
                        "registrations must not affect other names.");
                }

                if (ViewAccessorRegistry.TryGetAccessor(neverRegistered, TagField, out _))
                {
                    throw new Exception(
                        $"Unregistered name '{neverRegistered}' unexpectedly resolved in ViewAccessorRegistry; " +
                        "registrations must not affect other names.");
                }

                // Concurrent registration: many threads race to register distinct artifacts under ONE fresh
                // name in BOTH stores. First-wins under contention means each store must keep exactly one
                // winner, its identity must be stable across reads, and any registration attempted afterwards
                // must be ignored (idempotence tolerates a module initializer running more than once). The
                // two stores race independently, so their winning tags need not agree — each is asserted on
                // its own.
                var concurrentName = $"styleA-{prefix}-concurrent";
                const int racers = 8;

                Parallel.For(0, racers, tag =>
                {
                    GeneratedJsonContextStore.Register(concurrentName, new TagContext(tag));
                    ViewAccessorRegistry.Register(concurrentName, TaggedAccessorMap(tag));
                });

                if (!GeneratedJsonContextStore.TryGet(concurrentName, out var contextWinner))
                {
                    throw new Exception(
                        $"Concurrent registrations under '{concurrentName}' left GeneratedJsonContextStore empty; " +
                        "at least one registration must win.");
                }

                var contextWinningTag = ((TagContext)contextWinner).Tag;
                if (contextWinningTag < 0 || contextWinningTag >= racers)
                {
                    throw new Exception(
                        $"GeneratedJsonContextStore concurrent winner tag {contextWinningTag} is not one of " +
                        $"the registered tags [0, {racers}).");
                }

                if (!ViewAccessorRegistry.TryGetAccessor(concurrentName, TagField, out var accessorWinner))
                {
                    throw new Exception(
                        $"Concurrent registrations under '{concurrentName}' left ViewAccessorRegistry empty; " +
                        "at least one registration must win.");
                }

                var accessorWinningTag = (int)accessorWinner(DummyRow)!;
                if (accessorWinningTag < 0 || accessorWinningTag >= racers)
                {
                    throw new Exception(
                        $"ViewAccessorRegistry concurrent winner tag {accessorWinningTag} is not one of the " +
                        $"registered tags [0, {racers}).");
                }

                // A late registration under the already-won name must be discarded by BOTH stores, and
                // repeated reads must keep returning the same winning artifact.
                GeneratedJsonContextStore.Register(concurrentName, new TagContext(racers + 1));
                ViewAccessorRegistry.Register(concurrentName, TaggedAccessorMap(racers + 1));

                for (var read = 0; read < 3; read++)
                {
                    if (!GeneratedJsonContextStore.TryGet(concurrentName, out var stillContext) ||
                        !ReferenceEquals(stillContext, contextWinner))
                    {
                        throw new Exception(
                            $"The winning context for '{concurrentName}' changed after a later registration; " +
                            "first-wins under concurrency was violated in GeneratedJsonContextStore.");
                    }

                    if (ReadAccessorTag(concurrentName) != accessorWinningTag)
                    {
                        throw new Exception(
                            $"The winning accessor for '{concurrentName}' changed after a later registration; " +
                            "first-wins under concurrency was violated in ViewAccessorRegistry.");
                    }
                }

                // The GeneratedJsonContextStore.All snapshot must surface each participating name's winning
                // context exactly as TryGet returns it, so the AspNetCore drain sees the retained
                // (first-wins) handles and no discarded duplicates. (ViewAccessorRegistry exposes no
                // snapshot; its retention is already covered by the per-name reads above.)
                var all = GeneratedJsonContextStore.All;
                for (var i = 0; i < nameCount; i++)
                {
                    if (expectedFirstTag[i] is not int)
                    {
                        continue;
                    }

                    if (!GeneratedJsonContextStore.TryGet(names[i], out var stored) || !all.Contains(stored))
                    {
                        throw new Exception(
                            $"The All snapshot omitted the retained context for '{names[i]}'; the drain would miss it.");
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Builds a single-field Style A accessor map whose accessor returns <paramref name="tag"/>, so the
    /// registration the <see cref="ViewAccessorRegistry"/> retained is directly observable.
    /// </summary>
    private static IReadOnlyDictionary<string, Func<object, object?>> TaggedAccessorMap(int tag) =>
        new Dictionary<string, Func<object, object?>>(StringComparer.Ordinal)
        {
            [TagField] = _ => tag,
        };

    /// <summary>
    /// Reads the tag carried by the accessor the <see cref="ViewAccessorRegistry"/> retained for
    /// <paramref name="viewName"/>, throwing when no accessor is registered.
    /// </summary>
    private static int ReadAccessorTag(string viewName)
    {
        if (!ViewAccessorRegistry.TryGetAccessor(viewName, TagField, out var accessor))
        {
            throw new Exception(
                $"ViewAccessorRegistry had no accessor for '{viewName}'; expected a retained first registration.");
        }

        return (int)accessor(DummyRow)!;
    }

    /// <summary>
    /// A minimal <see cref="IJsonTypeInfoResolver"/> whose only purpose is to carry a unique
    /// <see cref="Tag"/> so the store's retained registration identity is directly observable. It is a
    /// real resolver to honor the store's contract (only an <see cref="IJsonTypeInfoResolver"/> is ever
    /// registered), but the store never resolves through it, so <see cref="GetTypeInfo"/> returns
    /// <see langword="null"/> (defer to the next resolver) rather than throwing.
    /// </summary>
    private sealed class TagContext : IJsonTypeInfoResolver
    {
        public TagContext(int tag) => Tag = tag;

        /// <summary>The registration identity used to tell which registration the store kept.</summary>
        public int Tag { get; }

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
    }
}
