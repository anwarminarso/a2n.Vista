using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Internal contract that lets a read-view builder collect a finished <see cref="CrudFacetDefinition"/>
/// from a configured CRUD facet builder without knowing its <c>TCrud</c>/<c>TEntity</c> type arguments.
/// </summary>
internal interface ICrudFacetDefinitionSource
{
    /// <summary>
    /// Materializes the accumulated state into a <see cref="CrudFacetDefinition"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No <c>MapWritable</c> mapping was declared.</exception>
    CrudFacetDefinition Build();
}

/// <summary>
/// Default <see cref="ICrudFacetBuilder{TCrud, TEntity}"/> implementation for the Gaya A (central
/// template) style. Accumulates the whitelisted write mappings and produces a
/// <see cref="CrudFacetDefinition"/>. Authoritative shape: docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TCrud">The typed write contract clients post against.</typeparam>
/// <typeparam name="TEntity">The underlying entity that writes are applied to.</typeparam>
internal sealed class CrudFacetBuilder<TCrud, TEntity> : ICrudFacetBuilder<TCrud, TEntity>, ICrudFacetDefinitionSource
    where TCrud : class
    where TEntity : class
{
    private readonly List<WritableFieldMapping> _writableFields = [];
    private LambdaExpression? _concurrencyToken;
    private bool _allowBulk;

    /// <inheritdoc />
    public ICrudFacetBuilder<TCrud, TEntity> MapWritable<TProp>(
        Expression<Func<TCrud, TProp>> from,
        Expression<Func<TEntity, TProp>> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        _writableFields.Add(new WritableFieldMapping(
            CrudMember: CentralTemplateExpressions.GetMemberName(from),
            EntityMember: CentralTemplateExpressions.GetMemberName(to),
            From: from,
            To: to));

        return this;
    }

    /// <inheritdoc />
    public ICrudFacetBuilder<TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField)
    {
        ArgumentNullException.ThrowIfNull(tokenField);
        _concurrencyToken = tokenField;
        return this;
    }

    /// <inheritdoc />
    public ICrudFacetBuilder<TCrud, TEntity> AllowBulk(bool allow = true)
    {
        _allowBulk = allow;
        return this;
    }

    /// <inheritdoc />
    public CrudFacetDefinition Build()
    {
        if (_writableFields.Count == 0)
        {
            // Write must be an explicit whitelist; an empty CRUD facet would re-open mass assignment
            // (Decision Log D38/D1, Requirement R3.2/R3.4).
            throw new InvalidOperationException(
                $"The CRUD facet for write contract '{typeof(TCrud).Name}' must declare at least one " +
                "MapWritable mapping. Write is default-deny: an anonymous read projection never serves writes.");
        }

        return new CrudFacetDefinition(
            CrudType: typeof(TCrud),
            EntityType: typeof(TEntity),
            WritableFields: _writableFields,
            ConcurrencyToken: _concurrencyToken,
            AllowsBulk: _allowBulk);
    }
}
