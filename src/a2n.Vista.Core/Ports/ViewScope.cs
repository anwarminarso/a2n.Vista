using System.Linq.Expressions;

namespace a2n.Vista.Ports;

/// <summary>
/// Minimal default <see cref="IViewScope"/> implementation. Accumulates server-trusted row
/// predicates keyed by source entity type so the executor can read them back per
/// <typeparamref name="TSource"/>. Authoritative shape: docs/spec/01-view.md §5.6.
/// </summary>
/// <remarks>
/// <para>
/// A scope instance is request-scoped: the <c>a2n.Vista.AspNetCore</c> layer creates one per request,
/// fills it from <c>IViewAuthorizer.ShapeQuery</c>, and hands it to the executor. It is therefore not
/// designed for concurrent use.
/// </para>
/// <para>
/// Predicates are stored as <see cref="object"/> in a per-type list to avoid reflection; each entry
/// is the exact <see cref="Expression{TDelegate}"/> that was added, recovered by a typed cast in
/// <see cref="GetRowFilters{TSource}"/>.
/// </para>
/// </remarks>
public sealed class ViewScope : IViewScope
{
    private readonly Dictionary<Type, List<object>> _filtersBySource = [];

    /// <inheritdoc />
    public int RowFilterCount { get; private set; }

    /// <inheritdoc />
    public void AddRowFilter<TSource>(Expression<Func<TSource, bool>> filter) where TSource : class
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!_filtersBySource.TryGetValue(typeof(TSource), out var list))
        {
            list = [];
            _filtersBySource[typeof(TSource)] = list;
        }

        list.Add(filter);
        RowFilterCount++;
    }

    /// <inheritdoc />
    public IReadOnlyList<Expression<Func<TSource, bool>>> GetRowFilters<TSource>() where TSource : class
    {
        if (!_filtersBySource.TryGetValue(typeof(TSource), out var list))
        {
            return [];
        }

        var result = new Expression<Func<TSource, bool>>[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            result[i] = (Expression<Func<TSource, bool>>)list[i];
        }

        return result;
    }
}
