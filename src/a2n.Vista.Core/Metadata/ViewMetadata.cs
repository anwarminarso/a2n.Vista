namespace a2n.Vista.Metadata;

/// <summary>
/// Declarative snapshot of a View after its authoring builder has run. Both authoring styles
/// (central template and class-per-view) emit an equivalent <see cref="ViewMetadata"/>, which is
/// the primary input for the executor, UI adapters, the source generator, and the TypeScript client.
/// Authoritative shape: docs/spec/01-view.md §5.4.
/// </summary>
/// <param name="Name">The unique view name used for registration and routing.</param>
/// <param name="Route">
/// The full route at which the view is served, composed at registration from the route group prefix
/// (or the default root <c>/api/views</c>) plus the view name — e.g. <c>/api/views/customers</c> or
/// <c>/internal/orders</c> (Decision Log D101/D103). The AspNetCore mapper maps the view at this route
/// verbatim. Core authoring builders emit the bare name here; the registration layer composes the
/// final full route.
/// </param>
/// <param name="QueryType">The CLR type of the projected (read) row.</param>
/// <param name="CrudType">
/// The typed CRUD/write contract, or <see langword="null"/> for a read-only view.
/// </param>
/// <param name="CrudEntityType">
/// The underlying entity type targeted by write operations, or <see langword="null"/> when the
/// view is read-only.
/// </param>
/// <param name="Fields">The projected fields and their per-field metadata, in projection order.</param>
/// <param name="Authorization">
/// Optional per-view authorization override; <see langword="null"/> means the view defers to the
/// central authorizer (§5.6).
/// </param>
/// <param name="Limits">The hard limits (page size, export rows) enforced for this view.</param>
/// <param name="IsReadOnly">
/// <see langword="true"/> when the view exposes only read facets (anonymous projection); write
/// endpoints are not generated for read-only views (Decision Log D38, §4.5).
/// </param>
public sealed record ViewMetadata(
    string Name,
    string Route,
    Type QueryType,
    Type? CrudType,
    Type? CrudEntityType,
    IReadOnlyList<FieldMetadata> Fields,
    AuthorizationRequirement? Authorization,
    HardLimits Limits,
    bool IsReadOnly);
