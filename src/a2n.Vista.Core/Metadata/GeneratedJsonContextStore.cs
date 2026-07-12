using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace a2n.Vista.Metadata;

/// <summary>
/// Process-wide, thread-safe, first-wins idempotent sink of source-generated per-view JSON contexts,
/// keyed by the view's runtime <c>Name</c> (source-generator per-view <c>JsonTypeInfo</c> phase /
/// Decision Log D125, Pillar 3). Each stored value is a <b>serializer-neutral opaque handle</b>
/// (<see cref="object"/>): at runtime it is a <c>System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver</c>
/// emitted into the view's own assembly, but it is held as <see cref="object"/> so that
/// <c>a2n.Vista.Core</c> references <b>no</b> <c>System.Text.Json</c> type and the pluggable-serializer
/// boundary (<c>a2n.Vista.Newtonsoft</c>) is preserved (Decision Log D48).
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>static</b> sink by design, mirroring <c>a2n.Vista.Metadata.ViewAccessorRegistry</c>,
/// <c>a2n.Vista.Ports.ViewInvokerStore</c>, and the EF-side <c>GeneratedExecutionPlanStore</c>: the
/// source generator emits a <c>[ModuleInitializer]</c> per covered view that registers the generated
/// context at module load — <em>before</em> any DI container exists — so a static, allocation-free
/// entry point is required.
/// </para>
/// <para>
/// First-wins (idempotent) registration tolerates a view's module being initialized more than once
/// (for example across multiple <c>AddVista</c> calls or test hosts in one process) without throwing.
/// Both registration and lookup are safe for concurrent callers. Registration and lookup are keyed by
/// the view's runtime <c>Name</c> using ordinal, case-sensitive comparison to match the authoritative
/// view registry.
/// </para>
/// <para>
/// The <c>a2n.Vista.AspNetCore</c> serialization seam drains <see cref="All"/> once at initialization,
/// casts each opaque handle to <c>IJsonTypeInfoResolver</c>, and chains it into the
/// <c>TypeInfoResolverChain</c>. That single unchecked cast is the contract boundary described on
/// <see cref="Register(string, object)"/>.
/// </para>
/// </remarks>
public static class GeneratedJsonContextStore
{
    // viewName (ordinal, case-sensitive — matches ViewRegistry) → opaque IJsonTypeInfoResolver handle.
    private static readonly ConcurrentDictionary<string, object> Contexts =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a generated per-view JSON context for <paramref name="viewName"/>. First registration
    /// wins; later registrations for the same name are ignored (idempotent).
    /// </summary>
    /// <param name="viewName">The view's runtime name (matches <c>ViewMetadata.Name</c>).</param>
    /// <param name="context">
    /// The generated per-view context, held as an opaque handle. <b>Contract:</b> the only value ever
    /// registered here is a <c>System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver</c>. Core
    /// stores it as <see cref="object"/> to stay free of any <c>System.Text.Json</c> dependency; the
    /// <c>a2n.Vista.AspNetCore</c> drain relies on this contract when casting the handle back to
    /// <c>IJsonTypeInfoResolver</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="viewName"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    public static void Register(string viewName, object context)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(context);

        // First registration wins; TryAdd silently ignores a repeat for the same view name.
        Contexts.TryAdd(viewName, context);
    }

    /// <summary>
    /// Looks up a generated per-view JSON context by view name.
    /// </summary>
    /// <param name="viewName">The view's runtime name.</param>
    /// <param name="context">
    /// The registered opaque context handle when present; otherwise <see langword="null"/>. The value is
    /// an <c>IJsonTypeInfoResolver</c> per the <see cref="Register(string, object)"/> contract.
    /// </param>
    /// <returns><see langword="true"/> when a generated context exists for the view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewName"/> is <see langword="null"/>.</exception>
    public static bool TryGet(string viewName, [NotNullWhen(true)] out object? context)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        return Contexts.TryGetValue(viewName, out context);
    }

    /// <summary>
    /// A point-in-time snapshot of every registered context handle, for the <c>a2n.Vista.AspNetCore</c>
    /// drain to chain into the serialization seam without knowing view names. Each element is an
    /// <c>IJsonTypeInfoResolver</c> per the <see cref="Register(string, object)"/> contract.
    /// </summary>
    public static IReadOnlyCollection<object> All => Contexts.Values.ToArray();
}
