using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Authoring;

namespace a2n.Vista.Write;

/// <summary>
/// Default in-memory <see cref="IWriteFacetRegistry"/>. Stores the captured
/// <see cref="CrudFacetDefinition"/> per view, keyed by view name (ordinal, case-sensitive), and is
/// populated at registration time alongside the view/plan stores (Decision Log D119, Requirement R13.1).
/// Both authoring styles — Gaya A (central template) and Gaya B (class-per-view) — register their
/// captured write facet here, so the EF execution layer (the reflection write mapper) consumes one
/// uniform shape regardless of how a view was authored.
/// </summary>
/// <remarks>
/// <para>
/// This is a process-scoped singleton in the composition root: built once at startup and read
/// concurrently while serving requests. It stays in Core so the write facet is reachable from the EF
/// layer without either adapter referencing the other (Requirement R14.6), and it keeps the runtime
/// write mappings off the EF-free <see cref="Metadata.ViewMetadata"/>.
/// </para>
/// <para>
/// Registration is idempotent per view name: the first registration wins and a later registration for
/// the same name is ignored (no throw), tolerating a view being registered more than once in a process
/// (for example across multiple <c>AddVista</c> calls or test hosts), mirroring
/// <see cref="Metadata.MaskSpecRegistry"/>. Both registration and lookup are safe for concurrent callers.
/// </para>
/// </remarks>
public sealed class WriteFacetRegistry : IWriteFacetRegistry
{
    // viewName (ordinal, case-sensitive — matches ViewRegistry) → captured write facet.
    private readonly ConcurrentDictionary<string, CrudFacetDefinition> _facets =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers the captured write facet for <paramref name="viewName"/>. Idempotent: the first
    /// registration for a given view name wins; a later registration for the same name is ignored.
    /// </summary>
    /// <param name="viewName">The unique view name the write facet belongs to.</param>
    /// <param name="facet">The captured <see cref="CrudFacetDefinition"/> for the view.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="viewName"/> or <paramref name="facet"/> is <see langword="null"/>.
    /// </exception>
    public void Register(string viewName, CrudFacetDefinition facet)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(facet);

        // First registration wins; TryAdd silently ignores a repeat for the same view name.
        _facets.TryAdd(viewName, facet);
    }

    /// <inheritdoc />
    public bool TryGet(string viewName, [NotNullWhen(true)] out CrudFacetDefinition? facet)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        return _facets.TryGetValue(viewName, out facet);
    }
}
