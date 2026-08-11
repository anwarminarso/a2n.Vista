using System.Linq.Expressions;

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

    /// <summary>
    /// Registers a read-only view from a <b>separate</b> source query and projection — the §4.1-aligned
    /// shape (Decision Log D152). Prefer this overload whenever the view needs row-level security: it is
    /// the only Style A form that lets server-trusted predicates over the source entity
    /// (<c>WithRowFilter&lt;TSource&gt;</c> and the per-request scope from
    /// <c>IViewAuthorizer.ShapeQuery</c>/<c>ShapeQueryAsync</c>) be AND-ed <em>before</em> the projection
    /// and pushed down to SQL.
    /// </summary>
    /// <typeparam name="TSource">
    /// The EF source entity type the query is rooted on, inferred from <paramref name="source"/>.
    /// </typeparam>
    /// <typeparam name="TRow">
    /// The projected (read) row type, inferred from <paramref name="projection"/>. May be anonymous, so
    /// no DTO is required (Requirement R2.1, Decision Log D37).
    /// </typeparam>
    /// <param name="name">
    /// The unique view name used for registration and routing (<c>{root}/{name}</c>, §5.6).
    /// </param>
    /// <param name="source">
    /// The source query over the data source and the request <see cref="IServiceProvider"/>. Core
    /// captures this delegate verbatim; it is materialized later by the EF execution layer, never by Core.
    /// </param>
    /// <param name="projection">
    /// The projection from the source entity to the row type, applied after every server-trusted
    /// predicate.
    /// </param>
    /// <returns>A builder for configuring the view's read facets and optional Write facet.</returns>
    /// <remarks>
    /// The combined <see cref="AddView{TRow}"/> overload stays supported and unchanged; it erases
    /// <typeparamref name="TSource"/> behind the projection, so a view registered that way must fail
    /// closed as soon as a server-trusted row filter exists (Decision Log D141).
    /// </remarks>
    IReadViewBuilder<TRow> AddView<TSource, TRow>(
        string name,
        Func<TDbContext, IServiceProvider, IQueryable<TSource>> source,
        Expression<Func<TSource, TRow>> projection)
        where TSource : class
        where TRow : class;
}
