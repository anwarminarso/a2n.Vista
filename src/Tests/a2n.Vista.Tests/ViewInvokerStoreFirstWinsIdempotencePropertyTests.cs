// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the first-wins idempotence of <see cref="ViewInvokerStore"/>
/// (source-generator-http-surface task 1.3; Decision Log D123). The store is the process-wide,
/// thread-safe, Core-resident sink the generated dispatch <c>[ModuleInitializer]</c>s populate at
/// assembly load — before DI exists — keyed by the view's runtime name. Because a view's module may be
/// initialized more than once in a process (multiple hosts/test assemblies, repeated <c>AddVista</c>
/// calls), the store must keep the <em>first</em> invoker registered under each name, ignore later
/// registrations for that name, and never disturb invokers registered under other names
/// (Requirement 4.5).
/// </summary>
/// <remarks>
/// <see cref="ViewInvokerStore"/> is a process-wide static store, so every generated case uses a fresh
/// <see cref="Guid"/>-unique name prefix to stay isolated from sibling tests and from any
/// module-initializer registrations already present in the process. Each registered invoker is a
/// <see cref="TagInvoker"/> carrying a unique integer tag, so the stored invoker's registration identity
/// is directly observable (cast + read <see cref="TagInvoker.Tag"/>): looking it up reveals which
/// registration won. The property covers sequential repeated registration (deterministic first-wins) and
/// a concurrent-registration segment (exactly one winner, stable across reads and immune to later adds).
/// </remarks>
public sealed class ViewInvokerStoreFirstWinsIdempotencePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    // Feature: source-generator-http-surface, Property 6: ViewInvokerStore first-wins idempotence.
    //
    // For any sequence of registrations into ViewInvokerStore (with repeated view names, in any order and
    // count, sequential or concurrent), the store retains the first invoker registered under each name,
    // discards later registrations for that name, returns that first invoker from TryGet consistently, and
    // leaves invokers registered under other names unchanged.
    //
    // Validates: Requirements 4.5
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
                    .Select(i => $"prop6-{prefix}-{i}")
                    .ToArray();

                // The expected winner per name = the tag of its FIRST registration in declaration order.
                var expectedFirstTag = new int?[nameCount];

                for (var seq = 0; seq < sequence.Count; seq++)
                {
                    var nameIndex = sequence[seq];
                    var tag = seq; // Unique per registration, so the winner is unambiguous.

                    ViewInvokerStore.Register(names[nameIndex], new TagInvoker(tag));

                    expectedFirstTag[nameIndex] ??= tag;
                }

                // First-wins retention + later-registration discard: every name that was registered at
                // least once must resolve to the invoker from its FIRST registration, regardless of how
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
                        if (!ViewInvokerStore.TryGet(names[i], out var stored))
                        {
                            throw new Exception(
                                $"Expected an invoker registered for '{names[i]}', but the store had none.");
                        }

                        var actual = ((TagInvoker)stored).Tag;
                        if (actual != expected)
                        {
                            throw new Exception(
                                $"Name '{names[i]}' retained the invoker tagged {actual}, expected the " +
                                $"first-registered invoker tagged {expected} (first-wins violated).");
                        }
                    }
                }

                // Non-interference: a fresh name that was never registered in this case must be absent —
                // no registration under any participating name may leak onto an unrelated name.
                var neverRegistered = $"prop6-{prefix}-absent";
                if (ViewInvokerStore.TryGet(neverRegistered, out _))
                {
                    throw new Exception(
                        $"Unregistered name '{neverRegistered}' unexpectedly resolved to an invoker; " +
                        "registrations must not affect other names.");
                }

                // Concurrent registration: many threads race to register distinct invokers under ONE
                // fresh name. First-wins under contention means exactly one registration must win, its
                // identity must be stable across reads, and any registration attempted afterwards must be
                // ignored (idempotence tolerates a module initializer running more than once).
                var concurrentName = $"prop6-{prefix}-concurrent";
                const int racers = 8;

                Parallel.For(0, racers, tag => ViewInvokerStore.Register(concurrentName, new TagInvoker(tag)));

                if (!ViewInvokerStore.TryGet(concurrentName, out var winner))
                {
                    throw new Exception(
                        $"Concurrent registrations under '{concurrentName}' left the store empty; " +
                        "at least one registration must win.");
                }

                var winningTag = ((TagInvoker)winner).Tag;
                if (winningTag < 0 || winningTag >= racers)
                {
                    throw new Exception(
                        $"Concurrent winner tag {winningTag} is not one of the registered tags [0, {racers}).");
                }

                // A late registration under the already-won name must be discarded, and repeated reads
                // must keep returning the same winning instance.
                ViewInvokerStore.Register(concurrentName, new TagInvoker(racers + 1));

                for (var read = 0; read < 3; read++)
                {
                    if (!ViewInvokerStore.TryGet(concurrentName, out var stillWinner) ||
                        !ReferenceEquals(stillWinner, winner))
                    {
                        throw new Exception(
                            $"The winning invoker for '{concurrentName}' changed after a later " +
                            "registration; first-wins under concurrency was violated.");
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// A minimal <see cref="IViewInvoker"/> whose only purpose is to carry a unique
    /// <see cref="Tag"/> so the store's retained registration identity is directly observable. Its
    /// dispatch members are never invoked by this property (the store never calls into an invoker), so
    /// they throw to make any accidental call obvious.
    /// </summary>
    private sealed class TagInvoker : IViewInvoker
    {
        public TagInvoker(int tag) => Tag = tag;

        /// <summary>The registration identity used to tell which registration the store kept.</summary>
        public int Tag { get; }

        public bool IsWritable => false;

        public Task<ViewInvocationListResult> ListAsync(
            IViewExecutor executor,
            ViewMetadata view,
            ViewQueryRequest request,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Tag invoker does not dispatch; it only carries an identity tag.");

        public Task<object?> DetailAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object key,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Tag invoker does not dispatch; it only carries an identity tag.");

        public Task<object> CreateAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object model,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Tag invoker does not dispatch; it only carries an identity tag.");

        public Task<bool> UpdateAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object key,
            object model,
            IViewScope scope,
            string? concurrencyToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Tag invoker does not dispatch; it only carries an identity tag.");
    }
}
