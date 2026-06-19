using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Fluent builder for the typed Write facet of a Gaya A (central template) view, obtained from
/// <see cref="IReadViewBuilder{TRow}.WithCrud{TCrud, TEntity}"/>. This is the only door from the
/// central-template style to write operations and it never accepts an anonymous type: writes always
/// flow through the typed contract <typeparamref name="TCrud"/> mapped onto the entity
/// <typeparamref name="TEntity"/> (the per-facet typing invariant of §4.5 / Decision Log D38).
/// Authoritative shape: docs/spec/01-view.md §5.5.
/// </summary>
/// <typeparam name="TCrud">The typed write contract clients post against.</typeparam>
/// <typeparam name="TEntity">The underlying entity that writes are applied to.</typeparam>
/// <remarks>
/// Same semantics as the Gaya B <c>ICrudBuilder&lt;TQuery, TCrud, TEntity&gt;</c> (§5.2) without the
/// read <c>TQuery</c> parameter, because the read facet in Gaya A is served by the anonymous
/// <c>TRow</c> projection. At least one <see cref="MapWritable"/> mapping is required; a facet with
/// none is rejected at build time (Decision Log D38/D1), closing mass-assignment at design time.
/// <para>
/// The §5.5 <c>WithValidator&lt;TValidator&gt;</c> member is intentionally deferred: it depends on the
/// shared <c>IViewCrudValidator&lt;TCrud&gt;</c> contract, which is owned jointly with the Gaya B
/// <c>ICrudBuilder</c> (§5.2). It will be added alongside that shared validator/interceptor surface to
/// avoid declaring the same public type twice. The members present here are sufficient for the typing
/// invariant this surface guarantees (Requirements R2.1, R3.1, R3.3).
/// </para>
/// </remarks>
public interface ICrudFacetBuilder<TCrud, TEntity>
    where TCrud : class
    where TEntity : class
{
    /// <summary>
    /// Whitelists a single writable field by binding a member of the write contract
    /// (<typeparamref name="TCrud"/>) to a member of the entity (<typeparamref name="TEntity"/>).
    /// Fields not mapped here are not writable (default-deny, Requirement R3.4, Decision Log D25).
    /// </summary>
    /// <typeparam name="TProp">The shared CLR type of the mapped member on both sides.</typeparam>
    /// <param name="from">A simple member selector on the write contract, e.g. <c>w =&gt; w.UnitPrice</c>.</param>
    /// <param name="to">A simple member selector on the entity, e.g. <c>e =&gt; e.UnitPrice</c>.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    ICrudFacetBuilder<TCrud, TEntity> MapWritable<TProp>(
        Expression<Func<TCrud, TProp>> from,
        Expression<Func<TEntity, TProp>> to);

    /// <summary>
    /// Declares an optimistic-concurrency token field on the entity. The write endpoint honours the
    /// HTTP <c>If-Match</c> header against this field; a mismatch surfaces as a concurrency conflict
    /// (Decision Log D30).
    /// </summary>
    /// <typeparam name="TToken">The CLR type of the concurrency token.</typeparam>
    /// <param name="tokenField">A simple member selector for the token on the entity.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    ICrudFacetBuilder<TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField);

    /// <summary>
    /// Opts the facet in or out of bulk write operations. Defaults to <see langword="false"/>.
    /// </summary>
    /// <param name="allow">Whether bulk writes are permitted.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    ICrudFacetBuilder<TCrud, TEntity> AllowBulk(bool allow = true);
}
