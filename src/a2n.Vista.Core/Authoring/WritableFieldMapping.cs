using System.Linq.Expressions;

namespace a2n.Vista.Authoring;

/// <summary>
/// A single whitelisted write mapping captured by <see cref="ICrudFacetBuilder{TCrud, TEntity}.MapWritable"/>
/// on a Gaya A (central template) view: one member of the typed write contract (<c>TCrud</c>) is bound
/// to one member of the underlying entity (<c>TEntity</c>). Authoritative shape: docs/spec/01-view.md §5.5.
/// </summary>
/// <param name="CrudMember">
/// The name of the source member on the typed write contract (<c>TCrud</c>) the value is read from.
/// </param>
/// <param name="EntityMember">
/// The name of the target member on the entity (<c>TEntity</c>) the value is written to. Only members
/// listed here are writable; everything else is default-deny (Requirement R3.4, Decision Log D25).
/// </param>
/// <param name="From">The original <c>TCrud → TProp</c> selector, kept verbatim for the EF layer.</param>
/// <param name="To">The original <c>TEntity → TProp</c> selector, kept verbatim for the EF layer.</param>
/// <remarks>
/// The expressions are captured as-is so the EF execution layer (Task 9) and the source generator
/// (Pilar 3) can build the strongly-typed assignment without re-parsing. Core never compiles or
/// invokes them — it only carries them as metadata-adjacent state.
/// </remarks>
public sealed record WritableFieldMapping(
    string CrudMember,
    string EntityMember,
    LambdaExpression From,
    LambdaExpression To);
