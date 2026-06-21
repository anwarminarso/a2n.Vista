using System.Linq.Expressions;
using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Read-only class-per-view ("Gaya B") builder. This builder intentionally does <b>not</b> expose
/// <c>CrudOn</c>, so a read-only <see cref="View{TQuery}"/> can never opt into a write facet by mistake
/// (Decision Log D26). Authoritative shape: docs/spec/01-view.md §5.2.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type sent to clients.</typeparam>
/// <remarks>
/// Filter, sort, and search are default-allow for every projected field; customize or opt out per field
/// via <see cref="Field{TProp}"/> (§4.4, Decision Log D42). Routing is global and authorization is
/// centralized, so neither is configured here (§5.6).
/// </remarks>
public interface IViewBuilder<TQuery> : IViewBuilderCore
    where TQuery : class
{
    /// <inheritdoc cref="IViewBuilderCore.Named"/>
    new IViewBuilder<TQuery> Named(string viewName);

    /// <summary>
    /// Defines the read projection from the EF source entity <typeparamref name="TSource"/> to
    /// <typeparamref name="TQuery"/>. Exactly one of <see cref="From{TSource}"/> or
    /// <see cref="FromQuery{TSource}"/> must be called. The underlying <c>IQueryable&lt;TSource&gt;</c>
    /// is resolved by the executor at run time (Decision Log D11, deferred to the EF layer).
    /// </summary>
    /// <typeparam name="TSource">The EF source entity the view projects from.</typeparam>
    /// <param name="projection">The projection expression (an object initializer over <typeparamref name="TQuery"/>).</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilder<TQuery> From<TSource>(
        Expression<Func<TSource, TQuery>> projection)
        where TSource : class;

    /// <summary>
    /// Defines the read projection together with an explicit source-query factory, used when the source
    /// needs joins, includes, or other shaping beyond a bare <c>DbSet</c>. The factory receives an
    /// <see cref="IServiceProvider"/> and is invoked by the executor (Decision Log D27/D28).
    /// </summary>
    /// <typeparam name="TSource">The element type produced by the source query.</typeparam>
    /// <param name="source">A factory that produces the base <c>IQueryable&lt;TSource&gt;</c>.</param>
    /// <param name="projection">The projection expression from <typeparamref name="TSource"/> to <typeparamref name="TQuery"/>.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilder<TQuery> FromQuery<TSource>(
        Func<IServiceProvider, IQueryable<TSource>> source,
        Expression<Func<TSource, TQuery>> projection)
        where TSource : class;

    /// <summary>
    /// Customizes a single projected field. Optional: every projected field is filterable and sortable
    /// by default, string fields are searchable by default, and labels are auto-derived. Use this to opt
    /// out, restrict operators, mark the primary key, hide a field, or make it client-scopable (§5.5).
    /// </summary>
    /// <typeparam name="TProp">The CLR type of the projected field.</typeparam>
    /// <param name="field">A selector for the projected field (for example <c>x =&gt; x.ProductId</c>).</param>
    /// <param name="configure">A callback that configures the field via <see cref="IFieldBuilder{TProp}"/>.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilder<TQuery> Field<TProp>(
        Expression<Func<TQuery, TProp>> field,
        Action<IFieldBuilder<TProp>> configure);

    /// <summary>
    /// Explicitly declares the view's key fields, overriding the default derived from the fields marked
    /// <see cref="IFieldBuilder{TProp}.PrimaryKey"/> (Decision Log D104). Use this for views over joins,
    /// unions, or other views where the key cannot be inferred (Decision Log D105). The order given is
    /// the order used for the deterministic paging tiebreaker; every named field must be projected.
    /// </summary>
    /// <param name="fields">The key field selectors, in key order (for example <c>x =&gt; x.OrderId, x =&gt; x.ProductId</c>).</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilder<TQuery> Key(params Expression<Func<TQuery, object?>>[] fields);

    /// <summary>
    /// Explicitly declares the view's key fields by name, overriding the default derived from the
    /// primary-key marks (Decision Log D104/D105). Every named field must be projected.
    /// </summary>
    /// <param name="fieldNames">The key field names, in key order.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilder<TQuery> Key(params string[] fieldNames);

    /// <inheritdoc cref="IViewBuilderCore.MaxPageSize"/>
    new IViewBuilder<TQuery> MaxPageSize(int rows);
    /// <inheritdoc cref="IViewBuilderCore.MaxExportRows"/>
    new IViewBuilder<TQuery> MaxExportRows(int rows);

    /// <summary>
    /// Adds a server-trusted, pre-projection row filter over the source entity
    /// <typeparamref name="TSource"/> (the recommended place for soft-delete and tenant filters, which
    /// live on the entity). The predicate is AND-ed into the query and pushed down to SQL
    /// (Decision Log D28, §5.2).
    /// </summary>
    /// <typeparam name="TSource">The EF source entity the predicate applies to.</typeparam>
    /// <param name="filterFactory">A factory that builds the predicate from an <see cref="IServiceProvider"/>.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilder<TQuery> WithRowFilter<TSource>(
        Func<IServiceProvider, Expression<Func<TSource, bool>>> filterFactory)
        where TSource : class;

    /// <summary>
    /// Adds a server-trusted, post-projection row filter over <typeparamref name="TQuery"/> for the
    /// rare case where the predicate depends on a computed/projected field rather than the source entity
    /// (§5.2).
    /// </summary>
    /// <param name="filterFactory">A factory that builds the predicate from an <see cref="IServiceProvider"/>.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilder<TQuery> WithProjectedRowFilter(
        Func<IServiceProvider, Expression<Func<TQuery, bool>>> filterFactory);

    /// <summary>
    /// Masks a projected field's value in read responses when <paramref name="shouldMask"/> returns
    /// <see langword="true"/>, transforming it with <paramref name="masker"/>. The transformer is
    /// mandatory; there is no implicit masking semantic (Decision Log D29).
    /// </summary>
    /// <typeparam name="TProp">The CLR type of the masked field.</typeparam>
    /// <param name="field">A selector for the field to mask.</param>
    /// <param name="shouldMask">A predicate, evaluated per request, that decides whether to mask.</param>
    /// <param name="masker">A pure transform applied to the field value when masking.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilder<TQuery> MaskField<TProp>(
        Expression<Func<TQuery, TProp>> field,
        Func<IServiceProvider, bool> shouldMask,
        Func<TProp, TProp> masker);
}

/// <summary>
/// Class-per-view ("Gaya B") builder for a view that also has a typed write facet. It inherits every
/// read-side knob from <see cref="IViewBuilder{TQuery}"/> and adds the write entry point
/// <see cref="CrudOn{TEntity}"/>. Authoritative shape: docs/spec/01-view.md §5.2.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type sent to clients.</typeparam>
/// <typeparam name="TCrud">The typed write contract received from clients.</typeparam>
/// <remarks>
/// The write path is always typed: a write facet never uses an anonymous projection, which closes
/// mass-assignment by design (Decision Log D38, §4.5). <see cref="CrudOn{TEntity}"/> must be called at
/// least once on a <see cref="View{TQuery, TCrud}"/>, and that facet must whitelist at least one field
/// via <see cref="ICrudBuilder{TQuery, TCrud, TEntity}.MapWritable{TProp}"/> (Requirement R3.2).
/// </remarks>
public interface IViewBuilder<TQuery, TCrud> : IViewBuilder<TQuery>
    where TQuery : class
    where TCrud : class
{
    /// <summary>
    /// Declares the typed write facet that targets the entity <typeparamref name="TEntity"/>. Must be
    /// called at least once on a <see cref="View{TQuery, TCrud}"/>, and the returned builder must
    /// whitelist at least one writable field (R3.2).
    /// </summary>
    /// <typeparam name="TEntity">The entity type write operations target.</typeparam>
    /// <param name="projectionForRead">
    /// An optional read-back projection from <typeparamref name="TEntity"/> to <typeparamref name="TQuery"/>
    /// used after a write; when <see langword="null"/> the List projection is reused.
    /// </param>
    /// <returns>A <see cref="ICrudBuilder{TQuery, TCrud, TEntity}"/> for configuring the write facet.</returns>
    ICrudBuilder<TQuery, TCrud, TEntity> CrudOn<TEntity>(
        Expression<Func<TEntity, TQuery>>? projectionForRead = null)
        where TEntity : class;
}
