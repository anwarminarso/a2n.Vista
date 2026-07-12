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
/// Property-based test for the first-wins idempotence of <see cref="GeneratedJsonContextStore"/>
/// (source-generator-json-typeinfo task 1.2; Decision Log D125). The store is the process-wide,
/// thread-safe, Core-resident sink the generated per-view <c>JsonTypeInfo</c> <c>[ModuleInitializer]</c>s
/// populate at assembly load — before DI exists — keyed by the view's runtime name, holding each context
/// as a serializer-neutral opaque handle. Because a view's module may be initialized more than once in a
/// process (multiple hosts/test assemblies, repeated <c>AddVista</c> calls), the store must keep the
/// <em>first</em> context registered under each name, ignore later registrations for that name, and never
/// disturb contexts registered under other names (Requirement 4.2).
/// </summary>
/// <remarks>
/// <see cref="GeneratedJsonContextStore"/> is a process-wide static store, so every generated case uses a
/// fresh <see cref="Guid"/>-unique name prefix to stay isolated from sibling tests and from any
/// module-initializer registrations already present in the process. Each registered context is a
/// <see cref="TagContext"/> carrying a unique integer tag — and, faithful to the store's contract, it is
/// a real <see cref="IJsonTypeInfoResolver"/> handle — so the stored context's registration identity is
/// directly observable (cast + read <see cref="TagContext.Tag"/>): looking it up reveals which
/// registration won. The property covers sequential repeated registration (deterministic first-wins) and
/// a concurrent-registration segment (exactly one winner, stable across reads and immune to later adds).
/// </remarks>
public sealed class GeneratedJsonContextStoreFirstWinsIdempotencePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    // Feature: source-generator-json-typeinfo, Property 6: GeneratedJsonContextStore first-wins idempotence.
    //
    // For any sequence of registrations into GeneratedJsonContextStore (with repeated view names, in any
    // order and count, sequential or concurrent), the store retains the first context registered under each
    // name, discards later registrations for that name, returns that first context from TryGet consistently,
    // and leaves contexts registered under other names unchanged.
    //
    // Validates: Requirements 4.2
    [Test]
    public void Store_Retains_First_Registration_Per_Name_And_Leaves_Other_Names_Unchanged()
    {
        // A case is: how many distinct view names participate, and the ordered registration sequence
        // (each element names which of those views is being registered). Repeats within the sequence are
        // exactly the "same name registered again" scenario the first-wins rule must survive.
        var genCase =
            from nameCount in Gen.Int[1, 4]
            from sequence in Gen.Int[0, nameCount - 1].List[1, 15]
            select (nameCount, sequence);

        genCase.Sample(
            tuple =>
            {
                var (nameCount, sequence) = tuple;

                // Guid-unique per case so this case's names never collide with any other case, test, or
                // pre-existing module-initializer registration in the process.
                var prefix = Guid.NewGuid().ToString("N");
                var names = Enumerable
                    .Range(0, nameCount)
                    .Select(i => $"prop6-json-{prefix}-{i}")
                    .ToArray();

                // The expected winner per name = the tag of its FIRST registration in declaration order.
                var expectedFirstTag = new int?[nameCount];

                for (var seq = 0; seq < sequence.Count; seq++)
                {
                    var nameIndex = sequence[seq];
                    var tag = seq; // Unique per registration, so the winner is unambiguous.

                    GeneratedJsonContextStore.Register(names[nameIndex], new TagContext(tag));

                    expectedFirstTag[nameIndex] ??= tag;
                }

                // First-wins retention + later-registration discard: every name that was registered at
                // least once must resolve to the context from its FIRST registration, regardless of how
                // many later registrations targeted the same name — and must do so consistently on
                // repeated lookups.
                for (var i = 0; i < nameCount; i++)
                {
                    if (expectedFirstTag[i] is not int expected)
                    {
                        continue; // This name never appeared in the sequence.
                    }

                    for (var read = 0; read < 3; read++)
                    {
                        if (!GeneratedJsonContextStore.TryGet(names[i], out var stored))
                        {
                            throw new Exception(
                                $"Expected a context registered for '{names[i]}', but the store had none.");
                        }

                        var actual = ((TagContext)stored).Tag;
                        if (actual != expected)
                        {
                            throw new Exception(
                                $"Name '{names[i]}' retained the context tagged {actual}, expected the " +
                                $"first-registered context tagged {expected} (first-wins violated).");
                        }
                    }
                }

                // Non-interference: a fresh name that was never registered in this case must be absent —
                // no registration under any participating name may leak onto an unrelated name.
                var neverRegistered = $"prop6-json-{prefix}-absent";
                if (GeneratedJsonContextStore.TryGet(neverRegistered, out _))
                {
                    throw new Exception(
                        $"Unregistered name '{neverRegistered}' unexpectedly resolved to a context; " +
                        "registrations must not affect other names.");
                }

                // Concurrent registration: many threads race to register distinct contexts under ONE
                // fresh name. First-wins under contention means exactly one registration must win, its
                // identity must be stable across reads, and any registration attempted afterwards must be
                // ignored (idempotence tolerates a module initializer running more than once).
                var concurrentName = $"prop6-json-{prefix}-concurrent";
                const int racers = 8;

                Parallel.For(0, racers, tag => GeneratedJsonContextStore.Register(concurrentName, new TagContext(tag)));

                if (!GeneratedJsonContextStore.TryGet(concurrentName, out var winner))
                {
                    throw new Exception(
                        $"Concurrent registrations under '{concurrentName}' left the store empty; " +
                        "at least one registration must win.");
                }

                var winningTag = ((TagContext)winner).Tag;
                if (winningTag < 0 || winningTag >= racers)
                {
                    throw new Exception(
                        $"Concurrent winner tag {winningTag} is not one of the registered tags [0, {racers}).");
                }

                // A late registration under the already-won name must be discarded, and repeated reads
                // must keep returning the same winning instance.
                GeneratedJsonContextStore.Register(concurrentName, new TagContext(racers + 1));

                for (var read = 0; read < 3; read++)
                {
                    if (!GeneratedJsonContextStore.TryGet(concurrentName, out var stillWinner) ||
                        !ReferenceEquals(stillWinner, winner))
                    {
                        throw new Exception(
                            $"The winning context for '{concurrentName}' changed after a later " +
                            "registration; first-wins under concurrency was violated.");
                    }
                }

                // The All snapshot must surface each participating name's winning context exactly once, so
                // the AspNetCore drain sees the retained (first-wins) handles and no discarded duplicates.
                var all = GeneratedJsonContextStore.All;
                for (var i = 0; i < nameCount; i++)
                {
                    if (expectedFirstTag[i] is not int expected)
                    {
                        continue;
                    }

                    if (!GeneratedJsonContextStore.TryGet(names[i], out var stored) ||
                        !all.Contains(stored))
                    {
                        throw new Exception(
                            $"The All snapshot omitted the retained context for '{names[i]}' " +
                            $"(tagged {expected}); the drain would miss it.");
                    }
                }
            },
            iter: Iterations);
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
