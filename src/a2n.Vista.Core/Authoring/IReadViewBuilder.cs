using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Fluent builder for the read facets (List/Detail) of a Gaya A (central template) view, returned by
/// <see cref="IViewTemplateBuilder{TDbContext}.AddView{TRow}"/>. Field selectors stay strongly typed
/// even when <typeparamref name="TRow"/> is an anonymous type, because the lambda is evaluated in the
/// same scope as <c>AddView</c>. Authoritative shape: docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TRow">The projected (read) row type, inferred from the <c>AddView</c> projection.</typeparam>
/// <remarks>
/// <para>
/// There is deliberately no <c>Route()</c> or <c>RequireAuthorization()</c> here: routing is global
/// (<c>{root}/{viewName}</c>, §5.6) and authorization is centralized (§5.6). Every projected field is
/// filterable and sortable by default, and string fields are searchable by default (default-allow,
/// Decision Log D42); use <see cref="Field"/> to opt out or customize per field.
/// </para>
/// <para>
/// A view authored this way is <b>read-only</b> unless <see cref="WithCrud"/> is called: an anonymous
/// projection never serves writes (the per-facet typing invariant of §4.5 / Decision Log D38).
/// </para>
/// </remarks>
public interface IReadViewBuilder<TRow>
    where TRow : class
{
    /// <summary>
    /// Overrides the maximum page size enforced for this view (otherwise
    /// <see cref="a2n.Vista.Metadata.HardLimits.DefaultMaxPageSize"/>).
    /// </summary>
    /// <param name="rows">The maximum number of rows a single page may return; must be positive.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IReadViewBuilder<TRow> MaxPageSize(int rows);

    /// <summary>
    /// Overrides the maximum number of rows an export may produce for this view (otherwise
    /// <see cref="a2n.Vista.Metadata.HardLimits.DefaultMaxExportRows"/>).
    /// </summary>
    /// <param name="rows">The maximum export row count; must be positive.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IReadViewBuilder<TRow> MaxExportRows(int rows);

    /// <summary>
    /// Customizes a single projected field. Optional: every field gets safe-by-correct defaults
    /// (filterable + sortable, string fields searchable, auto-derived label). Use this to mark the
    /// primary key, hide a field, restrict operators, opt out of filter/sort/search, or allow client
    /// scoping.
    /// </summary>
    /// <typeparam name="TProp">The CLR type of the selected field.</typeparam>
    /// <param name="field">A simple member selector on the row, e.g. <c>x =&gt; x.ProductId</c>.</param>
    /// <param name="configure">A callback that mutates the field's <see cref="IFieldBuilder{TProp}"/>.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IReadViewBuilder<TRow> Field<TProp>(
        Expression<Func<TRow, TProp>> field,
        Action<IFieldBuilder<TProp>> configure);

    /// <summary>
    /// Explicitly declares the view's key fields, overriding the default derived from the fields marked
    /// <see cref="IFieldBuilder{TProp}.PrimaryKey"/> (Decision Log D104). Use for views over joins,
    /// unions, or other views where the key cannot be inferred (Decision Log D105). The order is the
    /// paging-tiebreaker order; every named field must be projected.
    /// </summary>
    /// <param name="fields">The key field selectors, in key order.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IReadViewBuilder<TRow> Key(params Expression<Func<TRow, object?>>[] fields);

    /// <summary>
    /// Explicitly declares the view's key fields by name (Decision Log D104/D105). Every named field
    /// must be projected.
    /// </summary>
    /// <param name="fieldNames">The key field names, in key order.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IReadViewBuilder<TRow> Key(params string[] fieldNames);

    /// <summary>
    /// Adds a server-trusted, pre-projection row filter over the EF source entity
    /// <typeparamref name="TSource"/> (row-level security, §5.2, Decision Log D28). The predicate is
    /// produced lazily from the request <see cref="IServiceProvider"/> and AND-ed into the query; it is
    /// not subject to client whitelist validation (Requirement R6.3). For cross-view server-trusted
    /// scope, prefer <c>IViewAuthorizer.ShapeQuery</c> (§5.6) instead.
    /// </summary>
    /// <typeparam name="TSource">The EF source entity type the predicate is expressed over.</typeparam>
    /// <param name="filterFactory">
    /// A factory returning the predicate <c>Expression&lt;Func&lt;TSource, bool&gt;&gt;</c> for the
    /// current request.
    /// </param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IReadViewBuilder<TRow> WithRowFilter<TSource>(
        Func<IServiceProvider, Expression<Func<TSource, bool>>> filterFactory)
        where TSource : class;

    /// <summary>
    /// Attaches a typed Write facet, turning this read-only resource into a read+write view. This is
    /// the only path from the central-template style to write operations and it requires explicit
    /// types: the anonymous read projection is never used for writes (the per-facet typing invariant of
    /// §4.5 / Decision Log D38). Calling this sets <see cref="a2n.Vista.Metadata.ViewMetadata.IsReadOnly"/>
    /// to <see langword="false"/> and populates the metadata's CRUD types.
    /// </summary>
    /// <typeparam name="TCrud">The typed write contract clients post against.</typeparam>
    /// <typeparam name="TEntity">The underlying entity that writes are applied to.</typeparam>
    /// <returns>A builder for configuring the typed Write facet.</returns>
    ICrudFacetBuilder<TCrud, TEntity> WithCrud<TCrud, TEntity>()
        where TCrud : class
        where TEntity : class;
}
