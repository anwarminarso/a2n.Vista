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
/// A write-capable view must declare the write facet it needs: <see cref="CrudOn{TEntity}"/> is mandatory
/// and the view requires a primary key so writes can resolve the target row (Requirement R4.4); both are
/// enforced when metadata is built. Mass-assignment safety (a non-empty whitelist, scalar-only targets, no
/// key/token targets) is enforced at build time by the M9 write-DSL analyzer (VISTA0030/0031/0032, D122),
/// not by a startup guard. In this release a single write facet per view is supported; a later
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
        var state = new CrudFacetState(typeof(TCrud), typeof(TEntity));
        _crudState = state;
        return new CrudBuilder<TQuery, TCrud, TEntity>(state);
    }

    private protected override Type? GetCrudType() => typeof(TCrud);

    private protected override Type? GetCrudEntityType() => _crudState?.EntityType;

    private protected override bool IsReadOnlyView() => false;

    /// <summary>
    /// Exposes the captured Gaya B write facet as a <see cref="CrudFacetDefinition"/>, so registration
    /// can feed it into the same write-facet registry the Gaya A path uses. Returns <see langword="null"/>
    /// when no write facet was declared. Only valid to call after <see cref="ValidateWriteFacet"/>
    /// has passed (which guarantees the facet exists and the view has a resolvable primary key).
    /// </summary>
    private protected override CrudFacetDefinition? GetCrudFacetDefinition() => _crudState?.Build();

    private protected override void ValidateWriteFacet(
        string viewName,
        bool hasPrimaryKey,
        IReadOnlyList<string> keyFields)
    {
        // Write-executability preconditions only. The interim mass-assignment fail-fast guards that
        // mirrored the source-generator diagnostics (VISTA0030 zero-mapping, VISTA0031 non-scalar/
        // navigation target, VISTA0032 key/token target) have been retired (D122, Requirement 9.6): the
        // M9 write-DSL analyzer now reports those at build time, so an unsafe mapping is caught exactly
        // once and only during compilation. The runtime defense-in-depth in ReflectionWriteMapper still
        // skips keys, the concurrency token, and non-scalar targets as belt-and-suspenders.
        if (_crudState is null)
        {
            throw new InvalidOperationException(
                $"View '{viewName}' is a write-capable view (View<{typeof(TQuery).Name}, " +
                $"{typeof(TCrud).Name}>) but never declared a write facet; call CrudOn<TEntity>(...) " +
                "in Configure.");
        }

        // Retained: a writable view requires a resolvable primary key so a write can locate the target
        // row. This is a write-executability requirement (R4.4), not a mass-assignment guard, and it
        // depends on runtime key resolution (including D105 auto-derivation), so it stays at startup.
        if (!hasPrimaryKey)
        {
            throw new InvalidOperationException(
                $"View '{viewName}' has a write facet and therefore requires a primary key; mark one " +
                "projected field with .PrimaryKey() (R4.4).");
        }
    }
}
