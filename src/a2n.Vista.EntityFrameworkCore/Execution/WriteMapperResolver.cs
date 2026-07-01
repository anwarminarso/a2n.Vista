// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Metadata;
using a2n.Vista.Write;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// The single seam that turns a <see cref="ViewMetadata"/> into one resolved <see cref="WriteMapper"/>
/// for a write, hiding the generated-vs-reflection choice behind a fixed-signature delegate (write-path
/// seam / Decision Log D119). The executor calls <see cref="Resolve"/> exactly once per write and applies
/// the returned delegate, so it never branches on which implementation produced the mapper
/// (Requirements R13.1, R13.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deterministic preference (Requirements R13.3, R13.4).</b> Resolution always checks
/// <see cref="GeneratedWriteMapperStore"/> first: when a source-generated mapper exists for the view it
/// is returned unconditionally on every write; only when none is registered does resolution fall back to
/// the reflection mapper. In this milestone (M12) the generated store is empty, so the reflection
/// fallback is authoritative — and when the future M9 write-DSL phase populates the store, the executor
/// silently prefers the generated mapper with zero source changes.
/// </para>
/// <para>
/// <b>AOT hygiene (Requirement R13.5).</b> <see cref="Resolve"/> is deliberately <em>not</em>
/// <see cref="RequiresUnreferencedCodeAttribute"/>: the generated-store branch is trim/AOT-clean, so an
/// AOT-clean caller that resolves a generated mapper stays warning-free. The reflection fallback — the
/// only trim-unsafe part — is isolated in <see cref="ResolveReflectionFallback"/>, which owns and lazily
/// builds the RUC <see cref="ReflectionWriteMapper"/>. The single call from <see cref="Resolve"/> into
/// that branch is suppressed with the justification that it is unreachable under trim/AOT once a
/// generated mapper is registered (the same generated-vs-reflection tradeoff the read path makes).
/// </para>
/// <para>
/// The reflection mapper is created at most once and cached, so as long as this resolver is registered as
/// a singleton its per-view compiled-delegate cache is shared across every write in the process.
/// </para>
/// </remarks>
public sealed class WriteMapperResolver
{
    private readonly IWriteFacetRegistry _facetRegistry;
    private readonly object _fallbackGate = new();
    private ReflectionWriteMapper? _reflectionFallback;

    /// <summary>
    /// Initializes a new <see cref="WriteMapperResolver"/> over the Core write-facet registry the
    /// reflection fallback needs to build a mapper from a view's captured <c>MapWritable</c> selectors.
    /// The registry reference is stored only; the (RUC) reflection mapper is built lazily on first
    /// fallback so constructing the resolver stays trim/AOT-clean.
    /// </summary>
    /// <param name="facetRegistry">The per-view write-facet lookup populated at registration time.</param>
    /// <exception cref="ArgumentNullException"><paramref name="facetRegistry"/> is <see langword="null"/>.</exception>
    public WriteMapperResolver(IWriteFacetRegistry facetRegistry)
    {
        ArgumentNullException.ThrowIfNull(facetRegistry);
        _facetRegistry = facetRegistry;
    }

    /// <summary>
    /// Resolves the single <see cref="WriteMapper"/> for <paramref name="view"/>: the source-generated
    /// mapper when one is registered (preferred deterministically), otherwise the reflection fallback.
    /// Callers apply the returned delegate directly and never branch on its origin.
    /// </summary>
    /// <param name="view">The writable view whose write mapper is requested.</param>
    /// <returns>The resolved <see cref="WriteMapper"/> for the view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No generated mapper is registered and no write facet is registered for the view (for example a
    /// read-only view). Callers that need the indistinguishable not-found / no-plan behavior should verify
    /// the view is writable before resolving a mapper.
    /// </exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification =
            "The reflection fallback is reached only when no source-generated write mapper is registered " +
            "for the view. The AOT-clean write path registers a generated mapper, so the RUC branch is " +
            "unreachable under trim/AOT and the generated write path stays warning-free (Requirement R13.5).")]
    public WriteMapper Resolve(ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(view);

        // Prefer the source-generated mapper deterministically on every write (Requirements R13.3, R13.2).
        if (GeneratedWriteMapperStore.TryGet(view.Name, out var generated))
        {
            return generated;
        }

        // No generated mapper: fall back to the reflection implementation (Requirement R13.4).
        return ResolveReflectionFallback(view);
    }

    /// <summary>
    /// The RUC-confined fallback branch: lazily builds (once) and consults the reflection write mapper.
    /// Kept separate from <see cref="Resolve"/> so the <see cref="RequiresUnreferencedCodeAttribute"/>
    /// stays confined to the reflection path (Requirement R13.5).
    /// </summary>
    [RequiresUnreferencedCode(
        "The reflection write mapper compiles the captured MapWritable selectors at runtime; use the " +
        "source-generated write mapper (GeneratedWriteMapperStore) for the AOT-clean path.")]
    private WriteMapper ResolveReflectionFallback(ViewMetadata view)
    {
        var fallback = _reflectionFallback;
        if (fallback is null)
        {
            lock (_fallbackGate)
            {
                fallback = _reflectionFallback ??= new ReflectionWriteMapper(_facetRegistry);
            }
        }

        return fallback.GetOrCreate(view);
    }
}
