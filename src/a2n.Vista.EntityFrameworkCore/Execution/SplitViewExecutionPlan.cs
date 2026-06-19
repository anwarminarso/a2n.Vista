using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// The fully-correct, §4.1-aligned execution plan: it keeps the <em>source query</em> and the
/// <em>projection</em> separate, so server-trusted scope and authored row filters are AND-ed
/// <b>pre-projection</b> over the source entity <typeparamref name="TSource"/> and pushed down to SQL,
/// then the projection produces <typeparamref name="TRow"/>.
/// </summary>
/// <typeparam name="TSource">The EF source entity type the query is rooted on.</typeparam>
/// <typeparam name="TRow">The projected (read) row type sent to clients.</typeparam>
/// <remarks>
/// <para>
/// This is the plan the executor relies on for correct, secure row-level scope (Requirement R6.3,
/// Decision Log D46). It implements Decision Log <b>D11</b>: when no explicit source factory is given,
/// the base queryable is obtained by the <c>DbContext.Set&lt;TSource&gt;()</c> convention; an explicit
/// factory (the <c>FromQuery&lt;TSource&gt;</c> escape hatch, §5.2) overrides it.
/// </para>
/// <para>
/// It is the natural target for Gaya B (class-per-view) and the source generator (Pilar 3), both of
/// which capture the source type, projection, and row-filter factories as separate, strongly-typed
/// members. See <see cref="ViewExecutionPlan.Split{TSource, TRow}"/> for the public construction entry.
/// </para>
/// </remarks>
public sealed class SplitViewExecutionPlan<TSource, TRow> : IViewExecutionPlan
    where TSource : class
    where TRow : class
{
    private readonly Func<DbContext, IServiceProvider, IQueryable<TSource>> _sourceFactory;
    private readonly IReadOnlyList<Func<IServiceProvider, Expression<Func<TSource, bool>>>> _authoredRowFilters;
    private readonly Expression<Func<TSource, TRow>> _projection;

    /// <summary>
    /// Initializes a new <see cref="SplitViewExecutionPlan{TSource, TRow}"/>.
    /// </summary>
    /// <param name="viewName">The unique view name (matches <c>ViewMetadata.Name</c>).</param>
    /// <param name="projection">The projection <c>Expression&lt;Func&lt;TSource, TRow&gt;&gt;</c>.</param>
    /// <param name="sourceFactory">
    /// The source-query factory. When <see langword="null"/>, the <c>DbContext.Set&lt;TSource&gt;()</c>
    /// convention is used (Decision Log D11).
    /// </param>
    /// <param name="authoredRowFilters">
    /// The authored, server-trusted, deferred pre-projection row filters over <typeparamref name="TSource"/>
    /// (§5.2, Decision Log D28), in declaration order; <see langword="null"/> for none.
    /// </param>
    /// <param name="keyFieldName">
    /// The primary-key field name on <typeparamref name="TRow"/>, when known; otherwise
    /// <see langword="null"/> (the executor then falls back to its name convention).
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="viewName"/> is <see langword="null"/> or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public SplitViewExecutionPlan(
        string viewName,
        Expression<Func<TSource, TRow>> projection,
        Func<IServiceProvider, IQueryable<TSource>>? sourceFactory = null,
        IReadOnlyList<Func<IServiceProvider, Expression<Func<TSource, bool>>>>? authoredRowFilters = null,
        string? keyFieldName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        ArgumentNullException.ThrowIfNull(projection);

        ViewName = viewName;
        _projection = projection;
        _authoredRowFilters = authoredRowFilters ?? [];
        KeyFieldName = keyFieldName;

        // D11: default to the DbContext.Set<TSource>() convention; an explicit factory (FromQuery) wins.
        _sourceFactory = sourceFactory is null
            ? static (db, _) => db.Set<TSource>()
            : (_, services) => sourceFactory(services);
    }

    /// <inheritdoc />
    public string ViewName { get; }

    /// <inheritdoc />
    public Type RowType => typeof(TRow);

    /// <inheritdoc />
    public string? KeyFieldName { get; }

    /// <inheritdoc />
    [RequiresUnreferencedCode("View execution composes source/scope/projection from captured expressions at runtime; use the source generator path for AOT.")]
    public IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scope);

        // 1. Base source query (D11 convention or explicit factory).
        var source = _sourceFactory(dbContext, services);

        // 2. Authored, server-trusted row filters (pre-projection, not whitelist-validated; R6.3).
        for (var i = 0; i < _authoredRowFilters.Count; i++)
        {
            var predicate = _authoredRowFilters[i](services)
                ?? throw new InvalidOperationException(
                    $"An authored row filter for view '{ViewName}' produced a null predicate.");
            source = source.Where(predicate);
        }

        // 3. Per-request server-trusted scope from IViewAuthorizer.ShapeQuery (pre-projection; R6.3).
        var scopeFilters = scope.GetRowFilters<TSource>();
        for (var i = 0; i < scopeFilters.Count; i++)
        {
            source = source.Where(scopeFilters[i]);
        }

        // 4. Projection -> IQueryable<TRow>. Scope/row-filters are already pushed down pre-projection.
        return source.Select(_projection);
    }
}
