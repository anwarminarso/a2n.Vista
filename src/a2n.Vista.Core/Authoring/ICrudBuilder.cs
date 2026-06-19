using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Configures the typed write facet of a class-per-view ("Gaya B") view: it whitelists the fields that
/// may be written by mapping <typeparamref name="TCrud"/> members to <typeparamref name="TEntity"/>
/// members. Authoritative shape: docs/spec/01-view.md §5.2.
/// </summary>
/// <typeparam name="TQuery">The projected (read) row type of the owning view.</typeparam>
/// <typeparam name="TCrud">The typed write contract received from clients.</typeparam>
/// <typeparam name="TEntity">The entity type write operations target.</typeparam>
/// <remarks>
/// Write is default-deny: no field is mapped automatically, so <see cref="MapWritable{TProp}"/> must be
/// called at least once (Requirement R3.2, Decision Log D25). Any <typeparamref name="TEntity"/> member
/// not mapped here cannot be set by a client, which is what closes mass-assignment (R3.4, §7).
/// </remarks>
public interface ICrudBuilder<TQuery, TCrud, TEntity>
    where TQuery : class
    where TCrud : class
    where TEntity : class
{
    /// <summary>
    /// Whitelists a single writable field by mapping a <typeparamref name="TCrud"/> member to a
    /// <typeparamref name="TEntity"/> member. Must be called at least once for the write facet to be
    /// valid (R3.2).
    /// </summary>
    /// <typeparam name="TProp">The CLR type shared by both mapped members.</typeparam>
    /// <param name="from">A selector for the source member on the write DTO.</param>
    /// <param name="to">A selector for the target member on the entity.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    ICrudBuilder<TQuery, TCrud, TEntity> MapWritable<TProp>(
        Expression<Func<TCrud, TProp>> from,
        Expression<Func<TEntity, TProp>> to);

    /// <summary>
    /// Declares the optimistic-concurrency token field on the entity. The write endpoint honours the
    /// <c>If-Match</c> header and maps a conflict to HTTP 409/412 (Decision Log D30, §14.2).
    /// </summary>
    /// <typeparam name="TToken">The token type (for example <c>byte[]</c> rowversion or a timestamp).</typeparam>
    /// <param name="tokenField">A selector for the concurrency-token member on the entity.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    ICrudBuilder<TQuery, TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField);

    /// <summary>
    /// Opts the write facet into bulk operations. Off by default (§7).
    /// </summary>
    /// <param name="allow">Whether bulk write operations are permitted.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    ICrudBuilder<TQuery, TCrud, TEntity> AllowBulk(bool allow = true);
}
