using a2n.Vista.Metadata;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Registration and resolution surface for <see cref="IViewExecutionPlan"/>s, keyed by view name. This
/// is the EF-layer parallel of the Core <see cref="a2n.Vista.Ports.IViewRegistry"/>: where the registry
/// answers "which views exist (metadata)", the plan registry answers "how do I execute view X". Keeping
/// them as two stores (rather than widening <see cref="ViewMetadata"/> with execution state) preserves
/// the EF-free Core transport surface (Requirement R11.1/R11.2).
/// </summary>
/// <remarks>
/// The registry is populated once at startup by the DI wiring (Task 9.4: <c>AddVista</c> /
/// <c>RegisterTemplate&lt;T&gt;</c> / <c>Register&lt;TView&gt;</c>) and read concurrently while serving
/// requests; like the Core registry it is not designed for registration after startup.
/// </remarks>
public interface IViewExecutionPlanRegistry
{
    /// <summary>
    /// Adds an execution plan. Plans are keyed by <see cref="IViewExecutionPlan.ViewName"/> (ordinal,
    /// case-sensitive), mirroring the Core registry's name semantics.
    /// </summary>
    /// <param name="plan">The plan to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A plan with the same <see cref="IViewExecutionPlan.ViewName"/> is already registered.
    /// </exception>
    void Add(IViewExecutionPlan plan);

    /// <summary>
    /// Resolves the execution plan for a view by name.
    /// </summary>
    /// <param name="viewName">The view name. Compared ordinally and case-sensitively.</param>
    /// <returns>The matching plan, or <see langword="null"/> when none is registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewName"/> is <see langword="null"/>.</exception>
    IViewExecutionPlan? Get(string viewName);

    /// <summary>All registered execution plans.</summary>
    IReadOnlyCollection<IViewExecutionPlan> All { get; }
}
