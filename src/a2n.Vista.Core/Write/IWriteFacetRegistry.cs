using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Authoring;

namespace a2n.Vista.Write;

/// <summary>
/// Per-view lookup for the captured Write facet (<see cref="CrudFacetDefinition"/>), keyed by view
/// name. This is the Core-side, EF-free channel that delivers the whitelisted <c>MapWritable</c>
/// mappings and the concurrency-token selector to the EF execution layer (the reflection write mapper)
/// without runtime write delegates leaking onto the EF-free <see cref="Metadata.ViewMetadata"/>
/// (Decision Log D119, Requirement R13.1). Both authoring styles — Gaya A (central template) and
/// Gaya B (class-per-view) — populate the same registry at registration time, so the executor consumes
/// one uniform shape regardless of how a view was authored.
/// </summary>
/// <remarks>
/// The registry is populated at registration time alongside the existing view/plan stores and is read
/// once per write when the executor resolves the mapping seam. It stays in Core so neither the EF layer
/// nor the AspNetCore layer needs to reference the other to reach the write facet (Requirement R14.6).
/// </remarks>
public interface IWriteFacetRegistry
{
    /// <summary>
    /// Resolves the captured Write facet for a view by name.
    /// </summary>
    /// <param name="viewName">The view name. Compared ordinally and case-sensitively.</param>
    /// <param name="facet">
    /// When this method returns <see langword="true"/>, the captured <see cref="CrudFacetDefinition"/>
    /// for the view; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the view is a writable view with a captured Write facet;
    /// <see langword="false"/> when no write facet is registered under <paramref name="viewName"/>
    /// (for example a read-only view).
    /// </returns>
    bool TryGet(string viewName, [NotNullWhen(true)] out CrudFacetDefinition? facet);
}
