// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Results;
using CsCheck;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the deterministic resolver preference and coexistence contract of
/// <see cref="ViewRequestExecutor"/> (source-generator-http-surface task 8.4; Decision Log D123). The
/// executor's resolve step is exercised directly (no compilation/generator) against the runtime seams
/// that already exist: <see cref="ViewInvokerStore"/> (the source-generated dispatch sink) and the
/// private reflection fallback that closes the generic <see cref="IViewExecutor"/> facet over the view's
/// runtime row type. Each generated case builds a random set of views — some marked <em>covered</em> (a
/// stub <see cref="IViewInvoker"/> registered in the store), some <em>uncovered</em> (no invoker) — and
/// asserts that, for every view, <see cref="ViewRequestExecutor.ListAsync"/>:
/// <list type="bullet">
/// <item>routes a <em>covered</em> view to the registered generated invoker and never touches the
/// reflection path (preference, R4.1);</item>
/// <item>routes an <em>uncovered</em> view to the reflection fallback on the <see cref="IViewExecutor"/>
/// and never touches an invoker (fallback, R4.1);</item>
/// <item>routes each view independently in a process that holds <em>both</em> kinds at once, so the two
/// mechanisms coexist without one kind forcing the other (coexistence, R4.4);</item>
/// <item>routes deterministically: every one of the many resolutions of a view (in forward, reverse, and
/// randomized order) takes the same mechanism, so the split is stable across count and order.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ViewInvokerStore"/> is a process-wide, first-wins static store, so every view in every
/// case uses a <see cref="Guid"/>-unique name (<see cref="UniqueViewName"/>) to stay isolated from
/// sibling tests and from any module-initializer registrations already present in the process. Mechanism
/// is observed behaviorally and disjointly: the stub <see cref="RecordingReadInvoker"/> records each
/// dispatch and <em>never</em> calls the executor, while the stub <see cref="RecordingReadExecutor"/>
/// records the view name of each reflection-path <c>ListAsync&lt;TRow&gt;</c> call. A covered view must
/// therefore accumulate invoker dispatches and zero executor calls; an uncovered view must accumulate
/// executor calls and involve no invoker.
/// </para>
/// <para>
/// The request is driven through the real <see cref="ViewRequestExecutor.ListAsync"/> with no authorizer
/// registered (access defaults to allow, R7.2), so the resolve step — the unit under test — runs exactly
/// as in production after the one-door pipeline. <see cref="ViewRequestExecutor.ListAsync"/> confines its
/// <c>[RequiresUnreferencedCode]</c> to the reflection branch behind a justified suppression, so the test
/// caller inherits no IL2026; trimming is not used for tests regardless, matching the sibling tests.
/// </para>
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The test drives the reflection-backed executor fallback by design; trimming is not used for tests.")]
public sealed class DeterministicResolverPreferencePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    private static readonly IReadOnlyList<SortSpec> ById = new[] { new SortSpec(nameof(WidgetRow.Id)) };

    // Feature: source-generator-http-surface, Property 7: Deterministic resolver preference and
    // coexistence.
    //
    // For any set of views, each independently marked covered (a generated IViewInvoker registered in
    // ViewInvokerStore for the view's Name) or uncovered (no invoker), ViewRequestExecutor resolves every
    // covered view to its generated invoker and every uncovered view to the reflection fallback, does so
    // for every resolution regardless of count or order (deterministic), and routes each view
    // independently so covered and uncovered views coexist in one process.
    //
    // Validates: Requirements 4.1, 4.4
    [Test]
    public void Executor_Prefers_Generated_Invoker_When_Registered_And_Falls_Back_To_Reflection_Otherwise()
    {
        // A case is: a random, non-empty coverage vector (each flag = "this view has a registered
        // invoker"), plus a random extra resolution order over the same view set to vary count and order.
        var genCase =
            from covered in Gen.Bool.List[1, 8]
            from extraOrder in Gen.Int[0, covered.Count - 1].List[0, 20]
            select (covered, extraOrder);

        genCase.Sample(
            tuple =>
            {
                var (covered, extraOrder) = tuple;
                var count = covered.Count;

                // Guid-unique per case so this case's names never collide with any other case, test, or
                // pre-existing module-initializer registration in the process-wide store.
                var prefix = Guid.NewGuid().ToString("N");
                var names = Enumerable
                    .Range(0, count)
                    .Select(i => UniqueViewName(prefix, i))
                    .ToArray();

                // One registry holding every view (covered and uncovered together), one recording
                // executor shared by all — the coexistence substrate (R4.4).
                var registry = new ViewRegistry();
                for (var i = 0; i < count; i++)
                {
                    registry.Add(WidgetTestHarness.BuildView(names[i]));
                }

                var executor = new RecordingReadExecutor();

                var services = new ServiceCollection();
                services.AddSingleton<IViewRegistry>(registry);
                services.AddSingleton<IViewExecutor>(executor);
                var provider = services.BuildServiceProvider();

                var http = new DefaultHttpContext
                {
                    RequestServices = provider,
                    User = new ClaimsPrincipal(new ClaimsIdentity()),
                };

                // Register a stub generated invoker for the covered views only.
                var invokers = new RecordingReadInvoker?[count];
                for (var i = 0; i < count; i++)
                {
                    if (covered[i])
                    {
                        var invoker = new RecordingReadInvoker();
                        invokers[i] = invoker;
                        ViewInvokerStore.Register(names[i], invoker);
                    }
                }

                var glue = new ViewRequestExecutor(registry);
                var request = new ViewQueryRequest(Filter: null, Sort: ById, Page: 0, PageSize: 10);

                // Resolution schedule touching each view many times, in several orders (forward, reverse,
                // forward again, then a random extra order) so determinism is proven across count/order.
                var schedule = new List<int>((3 * count) + extraOrder.Count);
                for (var i = 0; i < count; i++)
                {
                    schedule.Add(i);
                }

                for (var i = count - 1; i >= 0; i--)
                {
                    schedule.Add(i);
                }

                for (var i = 0; i < count; i++)
                {
                    schedule.Add(i);
                }

                schedule.AddRange(extraOrder);

                // Expected number of resolutions per view index.
                var expectedResolutions = new int[count];
                foreach (var index in schedule)
                {
                    expectedResolutions[index]++;
                }

                foreach (var index in schedule)
                {
                    // Block on the async facet inside the synchronous CsCheck sample; the resolve step
                    // (the unit under test) runs after the unchanged one-door pipeline.
                    _ = glue.ListAsync(http, names[index], request).GetAwaiter().GetResult();
                }

                // Verify preference, fallback, coexistence, and determinism per view.
                for (var i = 0; i < count; i++)
                {
                    var executorCalls = executor.CallCountFor(names[i]);

                    if (covered[i])
                    {
                        // Preference (R4.1): every resolution took the generated invoker and NONE fell
                        // through to the reflection executor — the split is deterministic across all
                        // resolutions of this view.
                        var invoker = invokers[i]!;
                        if (invoker.ListCallCount != expectedResolutions[i])
                        {
                            throw new Exception(
                                $"Covered view #{i} ('{names[i]}') dispatched the generated invoker " +
                                $"{invoker.ListCallCount} time(s) but was resolved {expectedResolutions[i]} " +
                                "time(s); a covered view must take the invoker on every resolution (R4.1).");
                        }

                        if (executorCalls != 0)
                        {
                            throw new Exception(
                                $"Covered view #{i} ('{names[i]}') hit the reflection executor " +
                                $"{executorCalls} time(s); a registered generated invoker must always win " +
                                "so the reflection fallback is never taken (R4.1).");
                        }
                    }
                    else
                    {
                        // Fallback (R4.1): every resolution took the reflection executor, and no invoker
                        // was ever involved for this view (none was registered).
                        if (executorCalls != expectedResolutions[i])
                        {
                            throw new Exception(
                                $"Uncovered view #{i} ('{names[i]}') hit the reflection executor " +
                                $"{executorCalls} time(s) but was resolved {expectedResolutions[i]} " +
                                "time(s); an uncovered view must fall back to reflection on every " +
                                "resolution (R4.1).");
                        }

                        if (ViewInvokerStore.TryGet(names[i], out _))
                        {
                            throw new Exception(
                                $"Uncovered view #{i} ('{names[i]}') unexpectedly has a registered " +
                                "invoker; it must stay on the reflection fallback (R4.1).");
                        }
                    }
                }

                // Coexistence (R4.4): in a process holding both kinds, the routing partitions exactly by
                // coverage — the union of invoker-served and reflection-served resolutions equals the
                // whole schedule, with no overlap and nothing lost.
                var totalScheduled = schedule.Count;
                var totalInvokerServed = 0;
                var totalReflectionServed = 0;
                for (var i = 0; i < count; i++)
                {
                    totalInvokerServed += covered[i] ? invokers[i]!.ListCallCount : 0;
                    totalReflectionServed += executor.CallCountFor(names[i]);
                }

                if (totalInvokerServed + totalReflectionServed != totalScheduled)
                {
                    throw new Exception(
                        $"Coexistence violated: {totalInvokerServed} invoker-served + " +
                        $"{totalReflectionServed} reflection-served resolutions do not sum to the " +
                        $"{totalScheduled} scheduled resolutions; every request must route to exactly one " +
                        "mechanism (R4.4).");
                }
            },
            iter: Iterations);
    }

    private static string UniqueViewName(string prefix, int index) => $"prop7-{prefix}-{index}";

    /// <summary>
    /// A stub <see cref="IViewInvoker"/> standing in for a source-generated read invoker: it records how
    /// many times List was dispatched and returns a canned, empty result <em>without</em> calling the
    /// executor, so a covered view that reaches this invoker leaves the reflection executor untouched —
    /// making the served mechanism directly and disjointly observable. Detail/Create/Update are not
    /// exercised by this property and throw; write facets also match a read-only view's invoker (R3.3).
    /// </summary>
    private sealed class RecordingReadInvoker : IViewInvoker
    {
        private int _listCallCount;

        public int ListCallCount => Volatile.Read(ref _listCallCount);

        public bool IsWritable => false;

        public Task<ViewInvocationListResult> ListAsync(
            IViewExecutor executor,
            ViewMetadata view,
            ViewQueryRequest request,
            IViewScope scope,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _listCallCount);

            // A generated invoker would forward to executor.ListAsync<TRow>; this stub deliberately does
            // not, so "executor was called" uniquely means the reflection fallback was taken.
            var page = new PagedResult<WidgetRow>(Array.Empty<WidgetRow>(), 0, 0, 10, 0);
            var result = new ViewListResult<WidgetRow>(page, 0);
            return Task.FromResult(
                new ViewInvocationListResult(result, Array.Empty<object?>(), 0, 0));
        }

        public Task<object?> DetailAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object key,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This property exercises only the List resolve step.");

        public Task<object> CreateAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object model,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A read-only view invoker is not writable (R3.3).");

        public Task<bool> UpdateAsync(
            IViewExecutor executor,
            ViewMetadata view,
            object key,
            object model,
            IViewScope scope,
            string? concurrencyToken,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A read-only view invoker is not writable (R3.3).");
    }

    /// <summary>
    /// A stub <see cref="IViewExecutor"/> that records the view name of each reflection-path
    /// <c>ListAsync&lt;TRow&gt;</c> call and returns a canned, empty result. Because the generated invoker
    /// stub never calls the executor, a recorded call here uniquely identifies a view served by the
    /// reflection fallback. The other facets are not exercised by this property and throw.
    /// </summary>
    private sealed class RecordingReadExecutor : IViewExecutor
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, int> _listCallsByName = new(StringComparer.Ordinal);

        public int CallCountFor(string viewName)
        {
            lock (_gate)
            {
                return _listCallsByName.TryGetValue(viewName, out var count) ? count : 0;
            }
        }

        public Task<ViewListResult<TRow>> ListAsync<TRow>(
            ViewMetadata view,
            ViewQueryRequest request,
            IViewScope scope,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _listCallsByName.TryGetValue(view.Name, out var count);
                _listCallsByName[view.Name] = count + 1;
            }

            var page = new PagedResult<TRow>(Array.Empty<TRow>(), 0, 0, 10, 0);
            return Task.FromResult(new ViewListResult<TRow>(page, 0));
        }

        public Task<TRow?> DetailAsync<TRow>(
            ViewMetadata view,
            object key,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This property exercises only the List resolve step.");

        public Task<object> CreateAsync<TCrud>(
            ViewMetadata view,
            TCrud model,
            IViewScope scope,
            CancellationToken cancellationToken)
            where TCrud : class =>
            throw new NotSupportedException("This property exercises only the List resolve step.");

        public Task<bool> UpdateAsync<TCrud>(
            ViewMetadata view,
            object key,
            TCrud model,
            IViewScope scope,
            string? concurrencyToken,
            CancellationToken cancellationToken)
            where TCrud : class =>
            throw new NotSupportedException("This property exercises only the List resolve step.");

        [RequiresUnreferencedCode("Delete key resolution is built from metadata at runtime; out of scope for this property.")]
        public Task<bool> DeleteAsync(
            ViewMetadata view,
            object key,
            IViewScope scope,
            string? concurrencyToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This property exercises only the List resolve step.");
    }
}
