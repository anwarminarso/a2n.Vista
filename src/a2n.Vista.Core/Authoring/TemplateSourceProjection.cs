// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Double-dispatch visitor over a <see cref="TemplateSourceProjection{TDbContext}"/>, the way a
/// consumer recovers the strongly-typed <c>TSource</c>/<c>TRow</c> pair a Style A view captured through
/// <c>AddView&lt;TSource, TRow&gt;</c> (Decision Log D152).
/// </summary>
/// <typeparam name="TDbContext">The template's data-source type.</typeparam>
/// <typeparam name="TResult">What the visitor produces (for the EF layer, an execution plan).</typeparam>
/// <remarks>
/// The visitor exists so the type arguments stay <b>closed at compile time</b>: the alternative would be
/// <c>MakeGenericType</c> over the captured source/row types, which is reflection the trimmer cannot see
/// through. A row type may be anonymous, which is precisely the case reflection handles worst.
/// </remarks>
public interface ITemplateSourceProjectionVisitor<TDbContext, out TResult>
    where TDbContext : class
{
    /// <summary>
    /// Receives the captured source query, the projection, and the authored server-trusted row filters,
    /// all typed to the view's source entity.
    /// </summary>
    /// <typeparam name="TSource">The EF source entity type the view is rooted on.</typeparam>
    /// <typeparam name="TRow">The projected (read) row type, possibly anonymous.</typeparam>
    /// <param name="source">The source-query factory over the data source and request services.</param>
    /// <param name="projection">The projection applied after all server-trusted predicates.</param>
    /// <param name="authoredRowFilters">
    /// The deferred, server-trusted pre-projection predicates declared with
    /// <c>WithRowFilter&lt;TSource&gt;</c>, in declaration order; empty when none were declared.
    /// </param>
    TResult Visit<TSource, TRow>(
        Func<TDbContext, IServiceProvider, IQueryable<TSource>> source,
        Expression<Func<TSource, TRow>> projection,
        IReadOnlyList<Func<IServiceProvider, Expression<Func<TSource, bool>>>> authoredRowFilters)
        where TSource : class
        where TRow : class;
}

/// <summary>
/// The §4.1-aligned capture of a Style A (central template) view: the source query and the projection
/// held <b>separately</b>, so server-trusted predicates over the source entity can be AND-ed
/// pre-projection and pushed down to SQL (Decision Log D152).
/// </summary>
/// <typeparam name="TDbContext">The template's data-source type.</typeparam>
/// <remarks>
/// <para>
/// Present on a <see cref="TemplateViewDefinition{TDbContext}"/> only when the view was registered
/// through <c>IViewTemplateBuilder.AddView&lt;TSource, TRow&gt;(name, source, projection)</c>. The
/// original single-delegate <c>AddView&lt;TRow&gt;</c> overload erases the source type behind the
/// projection and leaves this <see langword="null"/>, which is why such a view must fail closed as soon
/// as a server-trusted row filter exists (Decision Log D141).
/// </para>
/// <para>
/// The hierarchy is closed (the constructor is internal): the only implementation is the generic
/// carrier the authoring layer creates, and the only way out is <see cref="Accept{TResult}"/>.
/// </para>
/// </remarks>
public abstract class TemplateSourceProjection<TDbContext>
    where TDbContext : class
{
    /// <summary>Restricts subclassing to the Core authoring layer.</summary>
    internal TemplateSourceProjection()
    {
    }

    /// <summary>The EF source entity type (<c>TSource</c>) the view is rooted on.</summary>
    public abstract Type SourceType { get; }

    /// <summary>The projected (read) row type (<c>TRow</c>), possibly anonymous.</summary>
    public abstract Type RowType { get; }

    /// <summary>
    /// Hands the captured, strongly-typed source/projection/row-filter triple to
    /// <paramref name="visitor"/>.
    /// </summary>
    /// <typeparam name="TResult">What the visitor produces.</typeparam>
    /// <param name="visitor">The visitor to dispatch to.</param>
    /// <returns>The visitor's result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract TResult Accept<TResult>(ITemplateSourceProjectionVisitor<TDbContext, TResult> visitor);
}

/// <summary>
/// The single <see cref="TemplateSourceProjection{TDbContext}"/> implementation: holds the captured
/// source factory, projection, and typed authored row filters for one Style A split view.
/// </summary>
/// <typeparam name="TDbContext">The template's data-source type.</typeparam>
/// <typeparam name="TSource">The EF source entity type.</typeparam>
/// <typeparam name="TRow">The projected (read) row type, possibly anonymous.</typeparam>
internal sealed class TemplateSourceProjection<TDbContext, TSource, TRow> : TemplateSourceProjection<TDbContext>
    where TDbContext : class
    where TSource : class
    where TRow : class
{
    private readonly Func<TDbContext, IServiceProvider, IQueryable<TSource>> _source;
    private readonly Expression<Func<TSource, TRow>> _projection;
    private readonly IReadOnlyList<Func<IServiceProvider, Expression<Func<TSource, bool>>>> _authoredRowFilters;

    /// <summary>
    /// Initializes the carrier, converting the authoring layer's type-tagged
    /// <see cref="TemplateRowFilter"/>s into typed factories. A filter declared over a different entity
    /// than the view's source cannot be applied pre-projection, so it fails fast here (at registration,
    /// not at request time) rather than turning into an <see cref="InvalidCastException"/> per request.
    /// </summary>
    internal TemplateSourceProjection(
        string viewName,
        Func<TDbContext, IServiceProvider, IQueryable<TSource>> source,
        Expression<Func<TSource, TRow>> projection,
        IReadOnlyList<TemplateRowFilter> authoredRowFilters)
    {
        _source = source;
        _projection = projection;

        var typed = new List<Func<IServiceProvider, Expression<Func<TSource, bool>>>>(authoredRowFilters.Count);
        foreach (var filter in authoredRowFilters)
        {
            if (filter.SourceType != typeof(TSource))
            {
                throw new InvalidOperationException(
                    $"View '{viewName}' declares a server-trusted row filter over '{filter.SourceType}', " +
                    $"but the view's source entity is '{typeof(TSource)}'. A pre-projection row filter must " +
                    "be expressed over the same entity the view's source query is rooted on, so it can be " +
                    "AND-ed before the projection and pushed down to SQL. Change the WithRowFilter<TSource> " +
                    "type argument to match the source query, or express the predicate over the projected " +
                    "row instead.");
            }

            var captured = filter;
            typed.Add(services => (Expression<Func<TSource, bool>>)captured.Create(services));
        }

        _authoredRowFilters = typed;
    }

    /// <inheritdoc />
    public override Type SourceType => typeof(TSource);

    /// <inheritdoc />
    public override Type RowType => typeof(TRow);

    /// <inheritdoc />
    public override TResult Accept<TResult>(ITemplateSourceProjectionVisitor<TDbContext, TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(_source, _projection, _authoredRowFilters);
    }
}
