using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// The result of authoring a single Gaya A (central template) view: the produced
/// <see cref="ViewMetadata"/> plus the metadata-adjacent state the EF execution layer (Task 9) needs
/// to resolve and run the view. Authoritative shape: docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TDbContext">
/// The developer's data-source type the projection is expressed against. See the type-parameter
/// remarks on <see cref="ViewTemplate{TDbContext}"/> for why Core constrains this to <c>class</c>
/// rather than EF Core's <c>DbContext</c>.
/// </typeparam>
/// <remarks>
/// <para>
/// Core captures — but never invokes — the anonymous projection. The query factory is type-erased to
/// a non-generic <see cref="IQueryable"/> here because the projected row type (<c>TRow</c>) is often an
/// anonymous type that the EF layer cannot name at compile time; the layer works against the
/// non-generic queryable and uses <see cref="ViewMetadata.QueryType"/> for the row type. This deferral
/// resolves Open Question #3 / Decision Log D11 (source resolution belongs to the EF layer).
/// </para>
/// <para>
/// The <see cref="Crud"/> facet is <see langword="null"/> for a read-only view; when present the view's
/// <see cref="ViewMetadata.IsReadOnly"/> is <see langword="false"/> and the metadata's
/// <see cref="ViewMetadata.CrudType"/> / <see cref="ViewMetadata.CrudEntityType"/> are populated from it.
/// </para>
/// </remarks>
public sealed class TemplateViewDefinition<TDbContext>
    where TDbContext : class
{
    private readonly Func<TDbContext, IServiceProvider, IQueryable> _queryFactory;

    /// <summary>
    /// Initializes a new <see cref="TemplateViewDefinition{TDbContext}"/>.
    /// </summary>
    /// <param name="metadata">The produced view metadata.</param>
    /// <param name="queryFactory">
    /// The captured anonymous projection, type-erased to a non-generic <see cref="IQueryable"/>.
    /// </param>
    /// <param name="rowFilters">The captured server-trusted row filters, in declaration order.</param>
    /// <param name="crud">The captured typed Write facet, or <see langword="null"/> when read-only.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="metadata"/>, <paramref name="queryFactory"/>, or <paramref name="rowFilters"/>
    /// is <see langword="null"/>.
    /// </exception>
    public TemplateViewDefinition(
        ViewMetadata metadata,
        Func<TDbContext, IServiceProvider, IQueryable> queryFactory,
        IReadOnlyList<TemplateRowFilter> rowFilters,
        CrudFacetDefinition? crud)
        : this(metadata, queryFactory, rowFilters, crud, sourceProjection: null)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="TemplateViewDefinition{TDbContext}"/> that also carries the
    /// §4.1-aligned source/projection split (Decision Log D152).
    /// </summary>
    /// <param name="metadata">The produced view metadata.</param>
    /// <param name="queryFactory">
    /// The captured anonymous projection, type-erased to a non-generic <see cref="IQueryable"/>. For a
    /// split view this is the equivalent combined query (<c>source.Select(projection)</c>), so
    /// <see cref="CreateQuery"/> behaves the same for both overloads.
    /// </param>
    /// <param name="rowFilters">The captured server-trusted row filters, in declaration order.</param>
    /// <param name="crud">The captured typed Write facet, or <see langword="null"/> when read-only.</param>
    /// <param name="sourceProjection">
    /// The separately-held source query and projection, or <see langword="null"/> when the view was
    /// authored through the combined single-delegate <c>AddView&lt;TRow&gt;</c> overload.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="metadata"/>, <paramref name="queryFactory"/>, or <paramref name="rowFilters"/>
    /// is <see langword="null"/>.
    /// </exception>
    public TemplateViewDefinition(
        ViewMetadata metadata,
        Func<TDbContext, IServiceProvider, IQueryable> queryFactory,
        IReadOnlyList<TemplateRowFilter> rowFilters,
        CrudFacetDefinition? crud,
        TemplateSourceProjection<TDbContext>? sourceProjection)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(queryFactory);
        ArgumentNullException.ThrowIfNull(rowFilters);

        Metadata = metadata;
        _queryFactory = queryFactory;
        RowFilters = rowFilters;
        Crud = crud;
        SourceProjection = sourceProjection;
    }

    /// <summary>The metadata produced for this view (the shape both authoring styles emit).</summary>
    public ViewMetadata Metadata { get; }

    /// <summary>The server-trusted row filters captured for this view, in declaration order.</summary>
    public IReadOnlyList<TemplateRowFilter> RowFilters { get; }

    /// <summary>
    /// The §4.1-aligned source/projection split, or <see langword="null"/> when the view was authored
    /// through the combined single-delegate <c>AddView&lt;TRow&gt;</c> overload (Decision Log D152).
    /// </summary>
    /// <remarks>
    /// When present, the execution layer can apply server-trusted predicates — the authored
    /// <see cref="RowFilters"/> and the per-request scope from <c>IViewAuthorizer.ShapeQuery</c>/
    /// <c>ShapeQueryAsync</c> — <b>pre-projection</b> over the source entity. When absent, the source type
    /// is hidden behind the captured projection, so a view that carries any server-trusted row filter
    /// fails closed instead of returning unscoped rows (Decision Log D141).
    /// </remarks>
    public TemplateSourceProjection<TDbContext>? SourceProjection { get; }

    /// <summary>
    /// The typed Write facet, or <see langword="null"/> when the view is read-only
    /// (anonymous projection with no <c>WithCrud</c>, Decision Log D38).
    /// </summary>
    public CrudFacetDefinition? Crud { get; }

    /// <summary>
    /// Materializes the captured projection against a data-source instance. Invoked by the EF
    /// execution layer at query time; Core itself never calls this.
    /// </summary>
    /// <param name="dbContext">The data-source instance supplying the queryable.</param>
    /// <param name="services">The request <see cref="IServiceProvider"/>.</param>
    /// <returns>The projected, not-yet-enumerated <see cref="IQueryable"/> for the view.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dbContext"/> or <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    public IQueryable CreateQuery(TDbContext dbContext, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(services);
        return _queryFactory(dbContext, services);
    }
}
