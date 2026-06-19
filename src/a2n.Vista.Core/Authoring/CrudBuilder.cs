using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Mutable accumulation state for a class-per-view write facet, shared between the write-capable
/// <see cref="ViewBuilder{TQuery, TCrud}"/> and the <see cref="CrudBuilder{TQuery, TCrud, TEntity}"/>
/// that configures it. The view builder reads this state when validating and building metadata.
/// </summary>
/// <param name="entityType">The entity type the write facet targets.</param>
internal sealed class CrudFacetState(Type entityType)
{
    /// <summary>The entity type write operations target.</summary>
    public Type EntityType { get; } = entityType;

    /// <summary>The number of <c>MapWritable</c> calls; must be at least one to satisfy R3.2.</summary>
    public int MapWritableCount { get; set; }

    /// <summary>Whether an optimistic-concurrency token was declared (Decision Log D30).</summary>
    public bool HasConcurrencyToken { get; set; }

    /// <summary>Whether bulk write operations are permitted (off by default, §7).</summary>
    public bool AllowBulkOperations { get; set; }
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
/// In this Core release the mapping expressions themselves are validated for presence (at least one
/// <see cref="MapWritable{TProp}"/>, R3.2) but are not persisted into <see cref="Metadata.ViewMetadata"/>,
/// whose shape carries only the write contract and entity types. The compiled <c>TCrud → TEntity</c>
/// mapping is produced by the source generator (Pilar 3) / EF layer (Task 9); this builder is the
/// authoring surface and validation point.
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
        _state.MapWritableCount++;
        return this;
    }

    /// <inheritdoc />
    public ICrudBuilder<TQuery, TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField)
    {
        ArgumentNullException.ThrowIfNull(tokenField);
        _state.HasConcurrencyToken = true;
        return this;
    }

    /// <inheritdoc />
    public ICrudBuilder<TQuery, TCrud, TEntity> AllowBulk(bool allow = true)
    {
        _state.AllowBulkOperations = allow;
        return this;
    }
}
