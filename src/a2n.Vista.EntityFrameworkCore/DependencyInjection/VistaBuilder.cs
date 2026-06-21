using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
    /// <summary>The default route root applied to views registered outside any <c>RouteGroup</c> (D44/D103).</summary>
    internal const string DefaultRouteRoot = "/api/views";

    private readonly IViewRegistry _registry;
    private readonly IViewExecutionPlanRegistry _planRegistry;
    private readonly VistaDbContextAccessor _contextAccessor;

    /// <summary>The active route-group prefix, or <see langword="null"/> to use <see cref="DefaultRouteRoot"/>.</summary>
    private string? _currentPrefix;

    internal VistaBuilder(
        IViewRegistry registry,
        IViewExecutionPlanRegistry planRegistry,
        VistaDbContextAccessor contextAccessor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(planRegistry);
        ArgumentNullException.ThrowIfNull(contextAccessor);

        _registry = registry;
        _planRegistry = planRegistry;
        _contextAccessor = contextAccessor;
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
        var definitions = template.BuildViews();

        foreach (var definition in definitions)
        {
            // Metadata first (EF-free transport surface), with the full route composed from the active
            // group prefix (D101/D103). Throws on duplicate name (R1.3).
            _registry.Add(WithComposedRoute(definition.Metadata));

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
        var metadata = WithComposedRoute(BuildViewMetadata<TView>());

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

        _registry.Add(WithComposedRoute(metadata));
        _planRegistry.Add(plan);
        return this;
    }

    /// <inheritdoc />
    public IVistaBuilder RouteGroup(string prefix, Action<IVistaBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(configure);

        var previous = _currentPrefix;
        _currentPrefix = CombinePrefix(previous, prefix);
        try
        {
            configure(this);
        }
        finally
        {
            _currentPrefix = previous;
        }

        return this;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Assembly scanning enumerates all types via reflection and introspects each view type's metadata; use explicit Register<TView> or the source generator for AOT.")]
    public IVistaBuilder RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsClass)
            {
                continue;
            }

            // Gaya B view types implement the internal IViewMetadataSource seam (visible to this
            // assembly via InternalsVisibleTo) and must be parameterless-constructable.
            if (!typeof(IViewMetadataSource).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            var source = (IViewMetadataSource)Activator.CreateInstance(type)!;
            _registry.Add(WithComposedRoute(source.BuildMetadata()));
        }

        return this;
    }

    /// <summary>
    /// Returns <paramref name="metadata"/> with its <see cref="ViewMetadata.Route"/> set to the full
    /// route composed from the active group prefix (or the default root) and the view name. Registration
    /// is the single source of a view's route (D101/D103).
    /// </summary>
    private ViewMetadata WithComposedRoute(ViewMetadata metadata) =>
        metadata with { Route = $"{_currentPrefix ?? DefaultRouteRoot}/{metadata.Name}" };

    /// <summary>
    /// Combines an outer group prefix with an inner one. A top-level group ignores the default root and
    /// uses its own absolute prefix; a nested group appends to its parent. Slashes are normalized so the
    /// result has a single leading slash and no trailing slash.
    /// </summary>
    private static string CombinePrefix(string? outer, string inner)
    {
        var trimmedInner = inner.Trim().Trim('/');
        return string.IsNullOrEmpty(outer)
            ? "/" + trimmedInner
            : $"{outer}/{trimmedInner}";
    }

    /// <summary>
    /// Builds a Gaya B view's <see cref="ViewMetadata"/> through the internal
    /// <see cref="IViewMetadataSource"/> seam (visible to this assembly via <c>InternalsVisibleTo</c>).
    /// The route is the relative segment (view name); the global route root is owned by the AspNetCore
    /// layer (D101). Only metadata is built here; the captured execution state is not touched.
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

        return source.BuildMetadata();
    }
}
