namespace a2n.Vista.Authoring;

/// <summary>
/// The registration surface passed to <see cref="ViewTemplate{TDbContext}.Configure"/>. A developer
/// registers many views in one place by calling <see cref="AddView{TRow}"/> with an inline projection,
/// the DynData-style "central template" authoring experience (Gaya A). Authoritative shape:
/// docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TDbContext">
/// The developer's data-source type the projections are expressed against. See the type-parameter
/// remarks on <see cref="ViewTemplate{TDbContext}"/> for why Core constrains this to <c>class</c>
/// rather than EF Core's <c>DbContext</c>.
/// </typeparam>
public interface IViewTemplateBuilder<TDbContext>
    where TDbContext : class
{
    /// <summary>
    /// Registers a read-only view from an inline projection. The row type <typeparamref name="TRow"/>
    /// is inferred by the compiler from the projection body and may be an anonymous type, so no DTO is
    /// required (Requirement R2.1, Decision Log D37). The resulting view is read-only until
    /// <see cref="IReadViewBuilder{TRow}.WithCrud{TCrud, TEntity}"/> attaches a typed Write facet
    /// (Requirements R3.1, R3.3, Decision Log D38).
    /// </summary>
    /// <typeparam name="TRow">The projected (read) row type, inferred from <paramref name="query"/>.</typeparam>
    /// <param name="name">
    /// The unique view name used for registration and routing (<c>{root}/{name}</c>, §5.6).
    /// </param>
    /// <param name="query">
    /// The projection, expressed as a queryable over the data source and the request
    /// <see cref="IServiceProvider"/>. Core captures this delegate verbatim; it is materialized later
    /// by the EF execution layer (Decision Log D11), never by Core.
    /// </param>
    /// <returns>A builder for configuring the view's read facets and optional Write facet.</returns>
    IReadViewBuilder<TRow> AddView<TRow>(
        string name,
        Func<TDbContext, IServiceProvider, IQueryable<TRow>> query)
        where TRow : class;
}
