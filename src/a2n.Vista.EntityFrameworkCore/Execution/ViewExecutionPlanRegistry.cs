namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Default in-memory <see cref="IViewExecutionPlanRegistry"/>. Stores plans in a dictionary keyed by
/// view name (ordinal, case-sensitive) and rejects duplicate names at registration time, mirroring the
/// Core <see cref="a2n.Vista.Ports.ViewRegistry"/> contract.
/// </summary>
/// <remarks>
/// Built once at the composition root and then read concurrently while serving requests; it is not
/// synchronized for concurrent writers (registration is a startup-only activity).
/// </remarks>
public sealed class ViewExecutionPlanRegistry : IViewExecutionPlanRegistry
{
    private readonly Dictionary<string, IViewExecutionPlan> _plans = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Add(IViewExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (_plans.ContainsKey(plan.ViewName))
        {
            throw new InvalidOperationException(
                $"An execution plan for view '{plan.ViewName}' is already registered. View names must be unique.");
        }

        _plans.Add(plan.ViewName, plan);
    }

    /// <inheritdoc />
    public IViewExecutionPlan? Get(string viewName)
    {
        ArgumentNullException.ThrowIfNull(viewName);

        return _plans.TryGetValue(viewName, out var plan) ? plan : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IViewExecutionPlan> All => _plans.Values;
}
