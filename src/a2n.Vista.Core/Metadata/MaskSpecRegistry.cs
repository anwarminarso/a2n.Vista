// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace a2n.Vista.Metadata;

/// <summary>
/// Process-wide, thread-safe store of captured masking behavior keyed by view name:
/// <c>viewName → ordered IReadOnlyList&lt;MaskSpec&gt;</c> (source-generator Phase 2 / Decision Log
/// D118, Requirement R7). It is the chosen delivery carrier for the runtime masking delegates: the
/// authoring builder captures the <see cref="MaskSpec.ShouldMask"/> predicate and the
/// <see cref="MaskSpec.Masker"/> transform at <c>Configure</c> time, the registration path publishes
/// them here, and the executor reads them back to apply masking at materialization.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors the Phase 1 <see cref="ViewAccessorRegistry"/> rationale and keeps Core EF-free: the
/// runtime mask delegates are <b>not</b> placed on the EF-free <see cref="ViewMetadata"/>, and the EF
/// execution plan carries only the AOT-clean <see cref="MaskAccessor"/>s. The two are matched by field
/// name at apply time. Unlike <see cref="ViewAccessorRegistry"/> (populated by generated
/// <c>[ModuleInitializer]</c>s), this store is populated at DI registration time because the mask
/// delegates are authored runtime closures the generator cannot embed.
/// </para>
/// <para>
/// Registration is idempotent per view name: the first registration wins and a later registration for
/// the same name is ignored (no throw), tolerating a view being registered more than once in a process
/// (for example across multiple <c>AddVista</c> calls or test hosts). Both registration and lookup are
/// safe for concurrent callers.
/// </para>
/// </remarks>
public static class MaskSpecRegistry
{
    // viewName (ordinal, case-sensitive — matches ViewRegistry) → ordered mask specs.
    private static readonly ConcurrentDictionary<string, IReadOnlyList<MaskSpec>> Specs =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers the captured mask specs for <paramref name="viewName"/> in declaration order.
    /// Idempotent: the first registration for a given view name wins; a later registration for the same
    /// name is ignored.
    /// </summary>
    /// <param name="viewName">The unique view name the masks belong to.</param>
    /// <param name="specs">
    /// The ordered mask specs for the view's masked fields. The list is stored by reference and must not
    /// be mutated after registration.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="viewName"/> or <paramref name="specs"/> is <see langword="null"/>.
    /// </exception>
    public static void Register(string viewName, IReadOnlyList<MaskSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(specs);

        // First registration wins; TryAdd silently ignores a repeat for the same view name.
        Specs.TryAdd(viewName, specs);
    }

    /// <summary>
    /// Attempts to resolve the ordered mask specs registered for <paramref name="viewName"/>.
    /// </summary>
    /// <param name="viewName">The view name to look up.</param>
    /// <param name="specs">
    /// When this method returns <see langword="true"/>, the ordered mask specs for the view; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if mask specs exist for the view; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewName"/> is <see langword="null"/>.</exception>
    public static bool TryGet(string viewName, [NotNullWhen(true)] out IReadOnlyList<MaskSpec>? specs)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        return Specs.TryGetValue(viewName, out specs);
    }
}
