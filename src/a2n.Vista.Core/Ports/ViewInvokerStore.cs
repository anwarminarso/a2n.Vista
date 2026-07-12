using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace a2n.Vista.Ports;

/// <summary>
/// Process-wide, thread-safe, first-wins idempotent sink of source-generated
/// <see cref="IViewInvoker"/>s, keyed by view name (source-generator HTTP-surface phase /
/// Decision Log D123). It is Core-resident by design: the generated dispatch invoker uses only Core
/// ports, so its store must not force an ASP.NET Core dependency into a domain assembly (Decision
/// Log D48). It mirrors <c>a2n.Vista.Metadata.ViewAccessorRegistry</c> and the EF-side
/// <c>GeneratedExecutionPlanStore</c> / <c>GeneratedWriteMapperStore</c>: generated
/// <c>[ModuleInitializer]</c>s populate it at assembly load — <em>before</em> DI exists — and the
/// ASP.NET Core <c>ViewRequestExecutor</c> prefers a stored invoker over the reflection fallback.
/// </summary>
/// <remarks>
/// First-wins (idempotent) registration tolerates a view's module being initialized more than once
/// (for example across multiple <c>AddVista</c> calls or test hosts in one process) without throwing.
/// Both registration and lookup are safe for concurrent callers. Registration and lookup are keyed by
/// the view's runtime <c>Name</c> using ordinal, case-sensitive comparison to match the authoritative
/// view registry.
/// </remarks>
public static class ViewInvokerStore
{
    // viewName (ordinal, case-sensitive — matches ViewRegistry) → generated invoker.
    private static readonly ConcurrentDictionary<string, IViewInvoker> Invokers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a generated invoker for <paramref name="viewName"/>. First registration wins; later
    /// registrations for the same name are ignored (idempotent).
    /// </summary>
    /// <param name="viewName">The view's runtime name (matches <c>ViewMetadata.Name</c>).</param>
    /// <param name="invoker">The generated dispatch invoker.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="viewName"/> or <paramref name="invoker"/> is <see langword="null"/>.
    /// </exception>
    public static void Register(string viewName, IViewInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(invoker);

        // First registration wins; TryAdd silently ignores a repeat for the same view name.
        Invokers.TryAdd(viewName, invoker);
    }

    /// <summary>
    /// Looks up a generated invoker by view name.
    /// </summary>
    /// <param name="viewName">The view's runtime name.</param>
    /// <param name="invoker">The registered invoker when present; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a generated invoker exists for the view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewName"/> is <see langword="null"/>.</exception>
    public static bool TryGet(string viewName, [NotNullWhen(true)] out IViewInvoker? invoker)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        return Invokers.TryGetValue(viewName, out invoker);
    }
}
