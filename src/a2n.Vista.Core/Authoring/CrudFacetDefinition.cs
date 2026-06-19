using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// Immutable snapshot of the typed Write facet attached to a Gaya A (central template) view via
/// <see cref="IReadViewBuilder{TRow}.WithCrud{TCrud, TEntity}"/>. Its mere presence on a view flips
/// the view from read-only to read+write (Decision Log D38). Authoritative shape:
/// docs/spec/01-view.md §5.5.
/// </summary>
/// <param name="CrudType">The typed write contract (<c>TCrud</c>) clients post against.</param>
/// <param name="EntityType">The underlying entity (<c>TEntity</c>) writes are applied to.</param>
/// <param name="WritableFields">
/// The whitelisted write mappings, in declaration order. There is always at least one (a CRUD facet
/// with no <see cref="ICrudFacetBuilder{TCrud, TEntity}.MapWritable"/> is rejected at build time,
/// Decision Log D38/D1).
/// </param>
/// <param name="ConcurrencyToken">
/// The optional optimistic-concurrency token selector over <c>TEntity</c>, or <see langword="null"/>
/// when the facet declares none (Decision Log D30).
/// </param>
/// <param name="AllowsBulk">Whether bulk write operations are permitted for this facet.</param>
/// <remarks>
/// This type intentionally mirrors the Write half of the Gaya B <c>ICrudBuilder</c> shape (§5.2) but
/// drops the read <c>TQuery</c> parameter, because in Gaya A the read facet is served by the anonymous
/// <c>TRow</c> projection while writes always flow through the typed <see cref="CrudType"/> (the
/// per-facet typing invariant of §4.5 / D38). The write surface is never derived from the anonymous
/// projection.
/// </remarks>
public sealed record CrudFacetDefinition(
    Type CrudType,
    Type EntityType,
    IReadOnlyList<WritableFieldMapping> WritableFields,
    LambdaExpression? ConcurrencyToken,
    bool AllowsBulk);
