using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;

namespace a2n.Vista.EntityFrameworkCore;

/// <summary>
/// Default <see cref="IVistaBuilder"/>. Writes view metadata into the shared <see cref="IViewRegistry"/>
/// and execution plans into the shared <see cref="IViewExecutionPlanRegistry"/> — the same singleton
/// instances the request-scoped executor reads back — and records the captured <c>DbContext</c> type in
/// <see cref="VistaDbContextAccessor"/>. Created and driven by <c>AddVista</c>; not intended for direct
/// construction by application code.
/// </summary>
internal sealed class VistaBuilder : IVistaBuilder
{
    private readonly IViewRegistry _registry;
    private readonly IViewExecutionPlanRegistry _planRegistry;
    private readonly VistaDbContextAccessor _contextAccessor;
    private string _routeRoot;

    internal VistaBuilder(
        IViewRegistry registry,
        IViewExecutionPlanRegistry planRegistry,
        VistaDbContextAccessor contextAccessor,
        string routeRoot)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(planRegistry);
        ArgumentNullException.ThrowIfNull(contextAccessor);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeRoot);

        _registry = registry;
        _planRegistry = planRegistry;
        _contextAccessor = contextAccessor;
        _routeRoot = routeRoot;
    }

    /// <inheritdoc />
    public IVistaBuilder RouteRoot(string routeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeRoot);
        _routeRoot = routeRoot;
        return this;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Gaya A authoring enumerates the (possibly anonymous) projection row type via reflection to derive field metadata; use the source generator path for AOT.")]
    public IVistaBuilder RegisterTemplate<TTemplate, TDbContext>()
        where TTemplate : ViewTemplate<TDbContext>, new()
        where TDbContext : class
    {
        // Record the context type up front so the scoped executor resolves the right DbContext (D11).
        _contextAccessor.Capture(typeof(TDbContext));

        var template = new TTemplate();
        var definitions = template.BuildViews(_routeRoot);

        foreach (var definition in definitions)
        {
            // Metadata first (EF-free transport surface). Throws on duplicate name (R1.3).
            _registry.Add(definition.Metadata);

            // Then the matching EF execution plan, keyed by the same view name.
            var plan = ViewExecutionPlan.FromTemplateDefinition(definition);
            _planRegistry.Add(plan);
        }

        return this;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Gaya B registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    public IVistaBuilder Register<TView>()
        where TView : class, new()
    {
        var metadata = BuildViewMetadata<TView>();

        // Metadata-only registration: the view is discoverable, but no execution plan is built because
        // Gaya B does not yet surface its source/projection to the EF layer (flagged limitation). The
        // executor throws a clear "no plan registered" error if this view is executed. Pair it with
        // Register<TView>(IViewExecutionPlan) to make it executable.
        _registry.Add(metadata);
        return this;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Gaya B registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    public IVistaBuilder Register<TView>(IViewExecutionPlan plan)
        where TView : class, new()
    {
        ArgumentNullException.ThrowIfNull(plan);

        var metadata = BuildViewMetadata<TView>();

        if (!string.Equals(plan.ViewName, metadata.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The supplied execution plan is for view '{plan.ViewName}', but '{typeof(TView)}' builds to " +
                $"view '{metadata.Name}'. The plan's ViewName must match the view's name.",
                nameof(plan));
        }

        _registry.Add(metadata);
        _planRegistry.Add(plan);
        return this;
    }

    /// <summary>
    /// Builds a Gaya B view's <see cref="ViewMetadata"/> through the internal
    /// <see cref="IViewMetadataSource"/> seam (visible to this assembly via <c>InternalsVisibleTo</c>),
    /// applying the configured route root. Only metadata is built here; the captured execution state is
    /// not touched.
    /// </summary>
    [RequiresUnreferencedCode("Gaya B registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    private ViewMetadata BuildViewMetadata<TView>()
        where TView : class, new()
    {
        var instance = new TView();

        if (instance is not IViewMetadataSource source)
        {
            throw new ArgumentException(
                $"'{typeof(TView)}' must derive from View<TQuery> or View<TQuery, TCrud> to be registered " +
                "as a class-per-view (Gaya B) view.",
                nameof(TView));
        }

        return source.BuildMetadata(_routeRoot);
    }
}
