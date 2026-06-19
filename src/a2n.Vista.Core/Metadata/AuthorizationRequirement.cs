namespace a2n.Vista.Metadata;

/// <summary>
/// Optional per-view authorization override carried on <see cref="ViewMetadata.Authorization"/>.
/// A <see langword="null"/> value means the view defers to the central authorizer (§5.6); a
/// non-null value is the rare per-view override.
/// </summary>
/// <param name="Policy">
/// An opaque authorization policy name the central authorizer can interpret for this view.
/// </param>
/// <remarks>
/// The full authorization model lives in <c>a2n.Vista.AspNetCore</c> (HTTP-bound, §5.6 / Decision
/// Log D48). docs/spec/01-view.md §5.4 references this type without defining its members yet, so
/// this is a minimal Core-side placeholder: it keeps <see cref="ViewMetadata"/> EF/HTTP-free while
/// reserving a stable shape. Members may grow as §5.6 is implemented.
/// </remarks>
public sealed record AuthorizationRequirement(string? Policy = null);
