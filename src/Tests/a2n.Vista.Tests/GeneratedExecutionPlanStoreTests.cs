// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for <see cref="GeneratedExecutionPlanStore"/> and the <see cref="ICompiledViewExecutionPlan"/>
/// contract values (source-generator Phase 2 / Decision Log D118; Requirements 1.3, 4.2). Verifies
/// first-wins idempotent registration under concurrent <c>Add</c> and that a stored plan round-trips its
/// <see cref="ICompiledViewExecutionPlan.ViewName"/> and <see cref="ICompiledViewExecutionPlan.RowType"/>
/// contract values through <see cref="GeneratedExecutionPlanStore.TryGet"/>.
/// </summary>
/// <remarks>
/// <see cref="GeneratedExecutionPlanStore"/> is a process-wide static store, so every test uses a unique
/// view name to stay isolated from sibling tests and from any module-initializer registrations present in
/// the process.
/// </remarks>
public sealed class GeneratedExecutionPlanStoreTests
{
    private static string UniqueViewName(string hint) => $"{hint}-{Guid.NewGuid():N}";

    /// <summary>
    /// A minimal <see cref="ICompiledViewExecutionPlan"/> test double. Only the contract values exercised
    /// by these tests (<see cref="ViewName"/>, <see cref="RowType"/>, and an identity tag) are meaningful;
    /// the execution members throw because store idempotence and contract round-trip never invoke them.
    /// </summary>
    private sealed class FakeCompiledPlan : ICompiledViewExecutionPlan
    {
        public FakeCompiledPlan(string viewName, Type rowType, int tag = 0)
        {
            ViewName = viewName;
            RowType = rowType;
            Tag = tag;
        }

        /// <summary>Identity marker used to prove which competing instance won the first-wins race.</summary>
        public int Tag { get; }

        public string ViewName { get; }

        public Type RowType { get; }

        public Type SourceType => typeof(void);

        public bool IsSingleSource => false;

        public IReadOnlyList<MaskAccessor> MaskAccessors => Array.Empty<MaskAccessor>();

        public IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope) =>
            throw new NotSupportedException("Execution is out of scope for store/contract tests.");

        public bool TryGetMemberAccess(string fieldName, out LambdaExpression accessor) =>
            throw new NotSupportedException("Execution is out of scope for store/contract tests.");

        public IOrderedQueryable ApplyPrimarySort(IQueryable source, string fieldName, bool descending) =>
            throw new NotSupportedException("Execution is out of scope for store/contract tests.");

        public IOrderedQueryable ApplyThenSort(IOrderedQueryable source, string fieldName, bool descending) =>
            throw new NotSupportedException("Execution is out of scope for store/contract tests.");
    }

    // A representative TQuery row type so RowType == typeof(TQuery) can be asserted.
    private sealed class CustomerRow
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    [Test]
    public async Task TryGet_RoundTrips_ViewName_And_RowType_Contract_Values()
    {
        var view = UniqueViewName("customers");
        var plan = new FakeCompiledPlan(view, typeof(CustomerRow));

        GeneratedExecutionPlanStore.Add(view, plan);

        var found = GeneratedExecutionPlanStore.TryGet(view, out var stored);

        await Assert.That(found).IsTrue();
        await Assert.That(stored).IsNotNull();
        // R1.3: ViewName == the view's runtime Name and RowType == typeof(TQuery) survive the round-trip.
        await Assert.That(stored!.ViewName).IsEqualTo(view);
        await Assert.That(stored.RowType).IsEqualTo(typeof(CustomerRow));
    }

    [Test]
    public async Task TryGet_Returns_False_For_Unregistered_View()
    {
        var found = GeneratedExecutionPlanStore.TryGet(UniqueViewName("nope"), out var stored);

        await Assert.That(found).IsFalse();
        await Assert.That(stored).IsNull();
    }

    [Test]
    public async Task Add_Is_FirstWins_Idempotent_For_Same_View_Name()
    {
        var view = UniqueViewName("orders");
        var first = new FakeCompiledPlan(view, typeof(CustomerRow), tag: 1);
        var second = new FakeCompiledPlan(view, typeof(CustomerRow), tag: 2);

        GeneratedExecutionPlanStore.Add(view, first);
        GeneratedExecutionPlanStore.Add(view, second); // ignored — first wins.

        GeneratedExecutionPlanStore.TryGet(view, out var stored);

        await Assert.That(((FakeCompiledPlan)stored!).Tag).IsEqualTo(1);
    }

    [Test]
    public async Task Add_Is_FirstWins_Idempotent_Under_Concurrent_Add()
    {
        var view = UniqueViewName("concurrent");
        const int competitors = 64;

        // Each thread races to register a distinctly-tagged competing plan instance for the same name.
        var plans = Enumerable.Range(1, competitors)
            .Select(tag => new FakeCompiledPlan(view, typeof(CustomerRow), tag))
            .ToArray();

        // Add must never throw and must never leave torn state regardless of which thread wins.
        Parallel.For(0, competitors, i => GeneratedExecutionPlanStore.Add(view, plans[i]));

        var found = GeneratedExecutionPlanStore.TryGet(view, out var stored);

        await Assert.That(found).IsTrue();
        await Assert.That(stored).IsNotNull();
        // Exactly one competitor won, and the winner is one of the instances we tried to add (no torn state).
        var winningTag = ((FakeCompiledPlan)stored!).Tag;
        await Assert.That(plans.Any(p => ReferenceEquals(p, stored))).IsTrue();
        await Assert.That(winningTag is >= 1 and <= competitors).IsTrue();

        // The winner is stable: repeated reads return the same instance (first-wins, no later overwrite).
        GeneratedExecutionPlanStore.TryGet(view, out var again);
        await Assert.That(ReferenceEquals(stored, again)).IsTrue();
    }

    [Test]
    public async Task Add_Null_Arguments_Throw()
    {
        var plan = new FakeCompiledPlan(UniqueViewName("v"), typeof(CustomerRow));

        await Assert.That(() => GeneratedExecutionPlanStore.Add(null!, plan))
            .Throws<ArgumentNullException>();
        await Assert.That(() => GeneratedExecutionPlanStore.Add("v", null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TryGet_Null_ViewName_Throws()
    {
        await Assert.That(() => GeneratedExecutionPlanStore.TryGet(null!, out _))
            .Throws<ArgumentNullException>();
    }
}
