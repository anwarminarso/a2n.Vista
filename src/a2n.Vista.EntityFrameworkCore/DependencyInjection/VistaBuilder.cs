using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;

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
    private readonly WriteFacetRegistry _writeFacetRegistry;

    /// <summary>The active route-group prefix, or <see langword="null"/> to use <see cref="DefaultRouteRoot"/>.</summary>
    private string? _currentPrefix;

    internal VistaBuilder(
        IViewRegistry registry,
        IViewExecutionPlanRegistry planRegistry,
        VistaDbContextAccessor contextAccessor,
        WriteFacetRegistry writeFacetRegistry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(planRegistry);
        ArgumentNullException.ThrowIfNull(contextAccessor);
        ArgumentNullException.ThrowIfNull(writeFacetRegistry);

        _registry = registry;
        _planRegistry = planRegistry;
        _contextAccessor = contextAccessor;
        _writeFacetRegistry = writeFacetRegistry;
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

            // Publish the captured write facet (Gaya A already materializes it on the definition) into
            // the process write-facet registry so the EF execution layer can build the whitelisted
            // TCrud → TEntity assignment (Decision Log D119, R13.1). Read-only views carry no facet.
            if (definition.Crud is not null)
            {
                _writeFacetRegistry.Register(definition.Metadata.Name, definition.Crud);
            }
        }

        return this;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Gaya B registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    public IVistaBuilder Register<TView>()
        where TView : class, new()
    {
        var source = CreateViewSource<TView>();
        var built = source.BuildMetadata();

        // Source-generator Phase 2 (Decision Log D118): if the generator emitted a compiled execution
        // plan for this view, its [ModuleInitializer] published it into GeneratedExecutionPlanStore at
        // module load. Look it up by the view's runtime Name (no assembly/type enumeration, R4.2).
        var hasPlan = GeneratedExecutionPlanStore.TryGet(built.Name, out var compiled);

        // Key fail-fast deferral (Decision Log D105 / M11): a view backed by a generated plan may declare
        // no key and instead rely on startup model-derivation (single-source) — so its key is completed
        // later by VistaModelKeyDerivationService. Defer the registration-time key check for those views;
        // the startup hook derives a single-source key (R6.1) or fails closed for a keyless source (R6.4)
        // or a non-single-source keyless view (R6.6). Views with no generated plan stay metadata-only and
        // still require a declared key at registration (Decision Log D106), unchanged.
        var metadata = hasPlan
            ? ComposeRoute(built)
            : WithComposedRoute(built);

        // Metadata first (EF-free transport surface); the view is always discoverable.
        _registry.Add(metadata);

        // Masking runtime (Decision Log D118 / R7): publish the captured mask specs (predicate + masker)
        // for this view so the executor applies them at materialization. The runtime delegates are kept
        // off the EF-free ViewMetadata and delivered via the Core MaskSpecRegistry, matched to the
        // generated MaskAccessors by field name at apply time.
        RegisterMaskSpecs(metadata.Name, source);

        // Write facet (Decision Log D119 / R13.1): publish the captured MapWritable whitelist and
        // concurrency-token selector into the process write-facet registry so the EF execution layer
        // (the reflection write mapper) can build the whitelisted TCrud → TEntity assignment. The facet
        // shape was validated when metadata was built above (ValidateWriteFacet). Read-only views carry
        // no facet and register nothing.
        RegisterWriteFacet(metadata.Name, source);

        // Adopt the generated plan so the view becomes EXECUTABLE (List/Detail). Absent a generated plan,
        // the view stays metadata-only (DR5) and the executor fails fast on execution with a clear
        // "no plan" message. The existing Register<TView>(IViewExecutionPlan) overload is unchanged.
        if (hasPlan)
        {
            _planRegistry.Add(new CompiledExecutionPlanAdapter(compiled));
        }

        return this;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Gaya B registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    public IVistaBuilder Register<TView>(IViewExecutionPlan plan)
        where TView : class, new()
    {
        ArgumentNullException.ThrowIfNull(plan);

        var source = CreateViewSource<TView>();
        var metadata = source.BuildMetadata();

        if (!string.Equals(plan.ViewName, metadata.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The supplied execution plan is for view '{plan.ViewName}', but '{typeof(TView)}' builds to " +
                $"view '{metadata.Name}'. The plan's ViewName must match the view's name.",
                nameof(plan));
        }

        _registry.Add(WithComposedRoute(metadata));
        _planRegistry.Add(plan);

        // Publish the captured mask specs so masking is applied at materialization on this RUC plan too
        // (Decision Log D118 / R7); reflection supplies the read/write accessors on the RUC path.
        RegisterMaskSpecs(metadata.Name, source);

        // Publish the captured write facet (Decision Log D119 / R13.1) so the EF execution layer can
        // build the whitelisted TCrud → TEntity assignment on this explicit-plan path as well.
        RegisterWriteFacet(metadata.Name, source);
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
    /// route composed from the active group prefix (or the default root) and the view name, after
    /// enforcing the D106 key fail-fast. Registration is the single source of a view's route
    /// (D101/D103).
    /// </summary>
    private ViewMetadata WithComposedRoute(ViewMetadata metadata)
    {
        // Fail-fast (Decision Log D106): a registered view must declare a key so deterministic paging and
        // Detail-by-key can resolve. Keys come from .PrimaryKey() marks or an explicit Key(...) override
        // (Decision Log D104/D105). EF-model auto-derivation is not available at registration time (no
        // DbContext yet), so an explicit declaration is required on this path.
        if (metadata.KeyFields.Count == 0)
        {
            throw new InvalidOperationException(
                $"View '{metadata.Name}' has no key fields, so deterministic paging and Detail-by-key " +
                "cannot resolve. Mark a projected field with .PrimaryKey() or declare the key explicitly " +
                "with .Key(...) (Decision Log D104/D106).");
        }

        return ComposeRoute(metadata);
    }

    /// <summary>
    /// Returns <paramref name="metadata"/> with its <see cref="ViewMetadata.Route"/> composed from the
    /// active group prefix (or the default root) and the view name — <b>without</b> the D106 key
    /// fail-fast. Used for source-generated executable views whose key may be derived from the EF model
    /// at startup (Decision Log D105 / M11); the startup hook completes the key or fails closed.
    /// </summary>
    private ViewMetadata ComposeRoute(ViewMetadata metadata)
        => metadata with { Route = $"{_currentPrefix ?? DefaultRouteRoot}/{metadata.Name}" };

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
    /// Creates a Gaya B view instance and returns its internal <see cref="IViewMetadataSource"/> seam
    /// (visible to this assembly via <c>InternalsVisibleTo</c>), through which metadata and captured mask
    /// specs are read. The route is composed by the caller; the global route root is owned by the
    /// AspNetCore layer (D101).
    /// </summary>
    [RequiresUnreferencedCode("Gaya B registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    private static IViewMetadataSource CreateViewSource<TView>()
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

        return source;
    }

    /// <summary>
    /// Publishes the view's captured mask specs into the Core <see cref="MaskSpecRegistry"/> keyed by
    /// view name (Decision Log D118 / R7), so the executor can apply masking at materialization. A view
    /// that masks no field registers nothing. The runtime mask delegates are intentionally not placed on
    /// the EF-free <see cref="ViewMetadata"/>.
    /// </summary>
    private static void RegisterMaskSpecs(string viewName, IViewMetadataSource source)
    {
        var specs = source.GetMaskSpecs();
        if (specs.Count > 0)
        {
            MaskSpecRegistry.Register(viewName, specs);
        }
    }

    /// <summary>
    /// Publishes the view's captured write facet into the process <see cref="WriteFacetRegistry"/> keyed
    /// by view name (Decision Log D119 / R13.1), so the EF execution layer can build the whitelisted
    /// <c>TCrud → TEntity</c> assignment. A read-only view exposes no write facet and registers nothing.
    /// The runtime write mappings are intentionally kept off the EF-free <see cref="ViewMetadata"/>.
    /// </summary>
    private void RegisterWriteFacet(string viewName, IViewMetadataSource source)
    {
        var facet = source.GetCrudFacetDefinition();
        if (facet is not null)
        {
            _writeFacetRegistry.Register(viewName, facet);
        }
    }
}
