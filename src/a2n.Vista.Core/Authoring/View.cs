using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Internal, non-generic bridge that lets infrastructure (the registry, DI wiring) materialize a
/// class-per-view definition's <see cref="ViewMetadata"/> without knowing its <c>TQuery</c>/<c>TCrud</c>
/// type parameters. Implemented by <see cref="View{TQuery}"/> and <see cref="View{TQuery, TCrud}"/>.
/// </summary>
internal interface IViewMetadataSource
{
    /// <summary>
    /// Runs the view's <c>Configure</c> against an internal builder and produces its metadata snapshot.
    /// </summary>
    /// <param name="routeRoot">
    /// The global route root to prefix the view route with, or <see langword="null"/> to emit the view
    /// name as the route segment (the AspNetCore layer applies the configured root).
    /// </param>
    /// <returns>The built <see cref="ViewMetadata"/>.</returns>
    ViewMetadata BuildMetadata(string? routeRoot);
}

/// <summary>
/// Base class for a read-only class-per-view ("Gaya B") definition. Derive from it, override
/// <see cref="Configure"/>, and configure the view with a strongly-typed
/// <see cref="IViewBuilder{TQuery}"/>. The resulting view has only read facets (List, and Detail by
/// primary key) and is therefore read-only. Authoritative shape: docs/spec/01-view.md §5.1.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type sent to clients.</typeparam>
/// <remarks>
/// <para>
/// This type is deliberately <b>not</b> a subclass of <see cref="View{TQuery, TCrud}"/>: keeping the two
/// base classes separate means the read-only builder never exposes <c>CrudOn</c>, so a write facet
/// cannot be added by accident (Decision Log D26).
/// </para>
/// <para>
/// The projection supplied via <c>From</c>/<c>FromQuery</c> must be an object initializer
/// (<c>new TQuery { ... }</c>) or a constructor/anonymous projection with named members; the field set
/// is derived from it. Identity projections (<c>x =&gt; x</c>) are not supported in this release.
/// </para>
/// </remarks>
public abstract class View<TQuery> : IConfiguredView, IViewMetadataSource
    where TQuery : class
{
    private ViewMetadata? _metadata;

    /// <summary>
    /// Configures this view. Called once by the registry/DI at startup. Implementations declare the
    /// projection (<c>From</c>/<c>FromQuery</c>), the view name (<c>Named</c>), and any per-field
    /// customization.
    /// </summary>
    /// <param name="builder">The strongly-typed read-only builder.</param>
    protected internal abstract void Configure(IViewBuilder<TQuery> builder);

    /// <inheritdoc />
    public Type QueryType => typeof(TQuery);

    /// <inheritdoc />
    public virtual Type? CrudType => null;

    /// <inheritdoc />
    public string Name => GetOrBuildMetadata().Name;

    /// <inheritdoc />
    void IConfiguredView.ConfigureCore(IViewBuilderCore builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder is IViewBuilder<TQuery> typed)
        {
            Configure(typed);
            return;
        }

        throw new ArgumentException(
            $"The builder must implement IViewBuilder<{typeof(TQuery).Name}>.", nameof(builder));
    }

    ViewMetadata IViewMetadataSource.BuildMetadata(string? routeRoot) => BuildMetadataCore(routeRoot);

    /// <summary>
    /// Builds the <see cref="ViewMetadata"/> for this view by running <see cref="Configure"/> against an
    /// internal builder. Intended for the registry and DI wiring.
    /// </summary>
    /// <param name="routeRoot">Optional global route root; see <see cref="IViewMetadataSource.BuildMetadata"/>.</param>
    /// <returns>The built metadata.</returns>
    internal ViewMetadata BuildMetadata(string? routeRoot = null) => BuildMetadataCore(routeRoot);

    /// <summary>
    /// Creates the builder, runs <see cref="Configure"/>, and emits metadata. Overridden by
    /// <see cref="View{TQuery, TCrud}"/> to use the write-capable builder.
    /// </summary>
    private protected virtual ViewMetadata BuildMetadataCore(string? routeRoot)
    {
        var builder = new ViewBuilder<TQuery>();
        Configure(builder);
        return builder.Build(routeRoot);
    }

    private ViewMetadata GetOrBuildMetadata() => _metadata ??= BuildMetadataCore(routeRoot: null);
}

/// <summary>
/// Base class for a class-per-view ("Gaya B") definition that also has a typed write facet. Derive from
/// it, override <see cref="Configure"/>, and configure the view with an
/// <see cref="IViewBuilder{TQuery, TCrud}"/>, calling <c>CrudOn</c> to declare the write target.
/// Authoritative shape: docs/spec/01-view.md §5.1.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type sent to clients.</typeparam>
/// <typeparam name="TCrud">The typed write contract received from clients.</typeparam>
/// <remarks>
/// The write facet is always strongly typed, never anonymous, which closes mass-assignment by design
/// (Decision Log D38). The view must call <c>CrudOn</c> at least once and whitelist at least one field
/// with <c>MapWritable</c>; otherwise building its metadata throws (Requirements R3.2, R4.4).
/// </remarks>
public abstract class View<TQuery, TCrud> : IConfiguredView, IViewMetadataSource
    where TQuery : class
    where TCrud : class
{
    private ViewMetadata? _metadata;

    /// <summary>
    /// Configures this view, including its typed write facet. Called once by the registry/DI at startup.
    /// </summary>
    /// <param name="builder">The strongly-typed write-capable builder.</param>
    protected internal abstract void Configure(IViewBuilder<TQuery, TCrud> builder);

    /// <inheritdoc />
    public Type QueryType => typeof(TQuery);

    /// <inheritdoc />
    public Type? CrudType => typeof(TCrud);

    /// <inheritdoc />
    public string Name => GetOrBuildMetadata().Name;

    /// <inheritdoc />
    void IConfiguredView.ConfigureCore(IViewBuilderCore builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder is IViewBuilder<TQuery, TCrud> typed)
        {
            Configure(typed);
            return;
        }

        throw new ArgumentException(
            $"The builder must implement IViewBuilder<{typeof(TQuery).Name}, {typeof(TCrud).Name}>.",
            nameof(builder));
    }

    ViewMetadata IViewMetadataSource.BuildMetadata(string? routeRoot) => BuildMetadataCore(routeRoot);

    /// <summary>
    /// Builds the <see cref="ViewMetadata"/> for this view by running <see cref="Configure"/> against an
    /// internal builder. Intended for the registry and DI wiring.
    /// </summary>
    /// <param name="routeRoot">Optional global route root; see <see cref="IViewMetadataSource.BuildMetadata"/>.</param>
    /// <returns>The built metadata.</returns>
    internal ViewMetadata BuildMetadata(string? routeRoot = null) => BuildMetadataCore(routeRoot);

    private ViewMetadata BuildMetadataCore(string? routeRoot)
    {
        var builder = new ViewBuilder<TQuery, TCrud>();
        Configure(builder);
        return builder.Build(routeRoot);
    }

    private ViewMetadata GetOrBuildMetadata() => _metadata ??= BuildMetadataCore(routeRoot: null);
}
