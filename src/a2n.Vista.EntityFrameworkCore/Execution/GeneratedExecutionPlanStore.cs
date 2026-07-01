// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Collections.Concurrent;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Process-wide, thread-safe, first-wins idempotent sink of source-generated
/// <see cref="ICompiledViewExecutionPlan"/>s, keyed by view name (source-generator Phase 2 /
/// Decision Log D118). It mirrors the Phase 1 <c>a2n.Vista.Metadata.ViewAccessorRegistry</c> rationale:
/// generated <c>[ModuleInitializer]</c>s populate it at assembly load — <em>before</em> DI exists —
/// and <c>AddVista</c> drains it into the per-app <see cref="IViewExecutionPlanRegistry"/> at
/// registration time. It is never read on the request hot path.
/// </summary>
/// <remarks>
/// First-wins (idempotent) registration tolerates a view's module being initialized more than once
/// (for example across multiple <c>AddVista</c> calls or test hosts in one process) without throwing;
/// genuine duplicate <em>view names</em> are still caught by the DI
/// <see cref="IViewExecutionPlanRegistry"/> fail-fast when the drained plan is added.
/// </remarks>
public static class GeneratedExecutionPlanStore
{
    private static readonly ConcurrentDictionary<string, ICompiledViewExecutionPlan> Plans =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a generated plan for <paramref name="viewName"/>. First registration wins; later
    /// registrations for the same name are ignored (idempotent).
    /// </summary>
    /// <param name="viewName">The view's runtime name (matches <c>ViewMetadata.Name</c>).</param>
    /// <param name="plan">The generated compiled execution plan.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewName"/> or <paramref name="plan"/> is null.</exception>
    public static void Add(string viewName, ICompiledViewExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(plan);
        Plans.TryAdd(viewName, plan);
    }

    /// <summary>
    /// Looks up a generated plan by view name.
    /// </summary>
    /// <param name="viewName">The view's runtime name.</param>
    /// <param name="plan">The registered plan when present.</param>
    /// <returns><see langword="true"/> when a generated plan exists for the view.</returns>
    public static bool TryGet(string viewName, out ICompiledViewExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        return Plans.TryGetValue(viewName, out plan!);
    }
}
