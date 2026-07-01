using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Mutable accumulation state for a class-per-view write facet, shared between the write-capable
/// <see cref="ViewBuilder{TQuery, TCrud}"/> and the <see cref="CrudBuilder{TQuery, TCrud, TEntity}"/>
/// that configures it. The view builder reads this state when validating and building metadata, and
/// materializes it into a <see cref="CrudFacetDefinition"/> for the write-facet registry.
/// </summary>
/// <param name="crudType">The typed write contract (<c>TCrud</c>) clients post against.</param>
/// <param name="entityType">The entity type write operations target.</param>
/// <remarks>
/// This state now captures the <c>MapWritable</c> expressions verbatim (rather than merely counting
/// them) so the Gaya B write facet produces the same <see cref="CrudFacetDefinition"/> shape as the
/// Gaya A <see cref="CrudFacetBuilder{TCrud, TEntity}"/>. Both styles therefore feed the same
/// <c>IWriteFacetRegistry</c> and the reflection write mapper consumes them uniformly.
/// </remarks>
internal sealed class CrudFacetState(Type crudType, Type entityType)
{
    /// <summary>The typed write contract (<c>TCrud</c>) clients post against.</summary>
    public Type CrudType { get; } = crudType;

    /// <summary>The entity type write operations target.</summary>
    public Type EntityType { get; } = entityType;

    /// <summary>
    /// The whitelisted write mappings captured from <c>MapWritable</c>, in declaration order. Must
    /// contain at least one entry to satisfy R3.2 (write is default-deny).
    /// </summary>
    public List<WritableFieldMapping> WritableFields { get; } = [];

    /// <summary>
    /// The optional optimistic-concurrency token selector over <c>TEntity</c>, or <see langword="null"/>
    /// when the facet declares none (Decision Log D30).
    /// </summary>
    public LambdaExpression? ConcurrencyToken { get; set; }

    /// <summary>Whether bulk write operations are permitted (off by default, §7).</summary>
    public bool AllowBulkOperations { get; set; }

    /// <summary>
    /// Materializes the accumulated state into a <see cref="CrudFacetDefinition"/> matching the Gaya A
    /// shape. Presence of at least one <c>MapWritable</c> mapping is validated by the owning view
    /// builder (<see cref="ViewBuilder{TQuery, TCrud}"/>) before this is called.
    /// </summary>
    public CrudFacetDefinition Build() => new(
        CrudType: CrudType,
        EntityType: EntityType,
        WritableFields: WritableFields,
        ConcurrencyToken: ConcurrencyToken,
        AllowsBulk: AllowBulkOperations);
}

/// <summary>
/// Default <see cref="ICrudBuilder{TQuery, TCrud, TEntity}"/> implementation. It records the write
/// whitelist and facet options onto a shared <see cref="CrudFacetState"/> the owning view builder reads.
/// Authoritative shape: docs/spec/01-view.md §5.2.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type of the owning view.</typeparam>
/// <typeparam name="TCrud">The typed write contract received from clients.</typeparam>
/// <typeparam name="TEntity">The entity type write operations target.</typeparam>
/// <remarks>
/// The <c>MapWritable</c> expressions are captured verbatim into <see cref="CrudFacetState.WritableFields"/>
/// so the EF execution layer (the reflection write mapper) can build the strongly-typed
/// <c>TCrud → TEntity</c> assignment without re-parsing. Core never compiles or invokes them here; it
/// only carries them as metadata-adjacent state, exactly as the Gaya A
/// <see cref="CrudFacetBuilder{TCrud, TEntity}"/> does.
/// </remarks>
internal sealed class CrudBuilder<TQuery, TCrud, TEntity> : ICrudBuilder<TQuery, TCrud, TEntity>
    where TQuery : class
    where TCrud : class
    where TEntity : class
{
    private readonly CrudFacetState _state;

    internal CrudBuilder(CrudFacetState state) => _state = state;

    /// <inheritdoc />
    public ICrudBuilder<TQuery, TCrud, TEntity> MapWritable<TProp>(
        Expression<Func<TCrud, TProp>> from,
        Expression<Func<TEntity, TProp>> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        _state.WritableFields.Add(new WritableFieldMapping(
            CrudMember: CentralTemplateExpressions.GetMemberName(from),
            EntityMember: CentralTemplateExpressions.GetMemberName(to),
            From: from,
            To: to));

        return this;
    }

    /// <inheritdoc />
    public ICrudBuilder<TQuery, TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField)
    {
        ArgumentNullException.ThrowIfNull(tokenField);
        _state.ConcurrencyToken = tokenField;
        return this;
    }

    /// <inheritdoc />
    public ICrudBuilder<TQuery, TCrud, TEntity> AllowBulk(bool allow = true)
    {
        _state.AllowBulkOperations = allow;
        return this;
    }
}
