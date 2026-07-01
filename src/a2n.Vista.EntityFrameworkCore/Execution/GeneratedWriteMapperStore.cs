// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Collections.Concurrent;
using a2n.Vista.Write;

namespace a2n.Vista.EntityFrameworkCore.Execution;

/// <summary>
/// Process-wide, thread-safe, first-wins idempotent sink of source-generated <see cref="WriteMapper"/>s,
/// keyed by view name (write-path seam / Decision Log D119). It mirrors
/// <see cref="GeneratedExecutionPlanStore"/>: a future M9 write-DSL phase emits generated
/// <c>[ModuleInitializer]</c>s that populate it at assembly load — <em>before</em> DI exists — and the
/// write-mapper resolver prefers a stored mapper over the reflection fallback. It is empty in this
/// milestone (M12); the RUC reflection mapper is authoritative until the generator fills it, with zero
/// source changes to the executor at that point (Requirements R13.1–R13.4).
/// </summary>
/// <remarks>
/// First-wins (idempotent) registration tolerates a view's module being initialized more than once
/// (for example across multiple <c>AddVista</c> calls or test hosts in one process) without throwing.
/// </remarks>
public static class GeneratedWriteMapperStore
{
    private static readonly ConcurrentDictionary<string, WriteMapper> Mappers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a generated write mapper for <paramref name="viewName"/>. First registration wins; later
    /// registrations for the same name are ignored (idempotent).
    /// </summary>
    /// <param name="viewName">The view's runtime name (matches <c>ViewMetadata.Name</c>).</param>
    /// <param name="mapper">The generated <see cref="WriteMapper"/> delegate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewName"/> or <paramref name="mapper"/> is null.</exception>
    public static void Add(string viewName, WriteMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(mapper);
        Mappers.TryAdd(viewName, mapper);
    }

    /// <summary>
    /// Looks up a generated write mapper by view name.
    /// </summary>
    /// <param name="viewName">The view's runtime name.</param>
    /// <param name="mapper">The registered mapper when present.</param>
    /// <returns><see langword="true"/> when a generated mapper exists for the view.</returns>
    public static bool TryGet(string viewName, out WriteMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        return Mappers.TryGetValue(viewName, out mapper!);
    }
}
