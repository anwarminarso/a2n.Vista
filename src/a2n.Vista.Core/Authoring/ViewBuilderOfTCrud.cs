using System.Linq.Expressions;
using a2n.Vista.Metadata;

namespace a2n.Vista.Authoring;

/// <summary>
/// Default <see cref="IViewBuilder{TQuery, TCrud}"/> implementation for the class-per-view ("Gaya B")
/// write-capable authoring path. It reuses all read-side behaviour from <see cref="ViewBuilder{TQuery}"/>
/// and adds the typed write facet via <see cref="CrudOn{TEntity}"/>.
/// Authoritative shape: docs/spec/01-view.md §5.2.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type sent to clients.</typeparam>
/// <typeparam name="TCrud">The typed write contract received from clients.</typeparam>
/// <remarks>
/// A write-capable view must declare exactly the write facet it needs: <see cref="CrudOn{TEntity}"/> is
/// mandatory and the resulting facet must whitelist at least one field (Requirement R3.2). The view also
/// requires a primary key so writes can resolve the target row (Requirement R4.4). Both invariants are
/// enforced when metadata is built. In this release a single write facet per view is supported; a later
/// <see cref="CrudOn{TEntity}"/> call replaces the previous one.
/// </remarks>
internal sealed class ViewBuilder<TQuery, TCrud> : ViewBuilder<TQuery>, IViewBuilder<TQuery, TCrud>
    where TQuery : class
    where TCrud : class
{
    private CrudFacetState? _crudState;

    /// <inheritdoc />
    public ICrudBuilder<TQuery, TCrud, TEntity> CrudOn<TEntity>(
        Expression<Func<TEntity, TQuery>>? projectionForRead = null)
        where TEntity : class
    {
        var state = new CrudFacetState(typeof(TEntity));
        _crudState = state;
        return new CrudBuilder<TQuery, TCrud, TEntity>(state);
    }

    private protected override Type? GetCrudType() => typeof(TCrud);

    private protected override Type? GetCrudEntityType() => _crudState?.EntityType;

    private protected override bool IsReadOnlyView() => false;

    private protected override void ValidateWriteFacet(string viewName, bool hasPrimaryKey)
    {
        if (_crudState is null)
        {
            throw new InvalidOperationException(
                $"View '{viewName}' is a write-capable view (View<{typeof(TQuery).Name}, " +
                $"{typeof(TCrud).Name}>) but never declared a write facet; call CrudOn<TEntity>(...) " +
                "in Configure.");
        }

        if (_crudState.MapWritableCount == 0)
        {
            throw new InvalidOperationException(
                $"The write facet of view '{viewName}' must whitelist at least one field; call " +
                "MapWritable(...) at least once (R3.2). Write is default-deny.");
        }

        if (!hasPrimaryKey)
        {
            throw new InvalidOperationException(
                $"View '{viewName}' has a write facet and therefore requires a primary key; mark one " +
                "projected field with .PrimaryKey() (R4.4).");
        }
    }
}
