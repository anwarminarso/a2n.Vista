using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace a2n.Vista.Metadata;

/// <summary>
/// Process-wide, thread-safe store of generated read accessors keyed by view name and then by field
/// name: <c>viewName → { fieldName → Func&lt;object, object?&gt; }</c>. Each accessor reads one
/// property from a projected row object (a compiled cast + property read), replacing the
/// reflection-based <see cref="System.Reflection.PropertyInfo.GetValue(object)"/> on AOT-sensitive
/// hot paths (Decision Log D117, Pillar 3).
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>static</b> sink by design. The source generator emits a <c>[ModuleInitializer]</c>
/// per consumer assembly that registers the generated accessor map at module load — before any DI
/// container exists — so a static, allocation-free entry point is required. The store holds only
/// compile-time delegates (no per-request state), so its lifetime matching the process is intentional.
/// </para>
/// <para>
/// Registration is idempotent per view name: the first registration wins and any repeat for the same
/// view name is ignored (no throw). This coexists with the duplicate-name fail-fast in
/// <see cref="a2n.Vista.Ports.ViewRegistry"/> — the accessor store is a best-effort acceleration cache,
/// not the authoritative view registry. Both registration and lookup are safe for concurrent callers.
/// </para>
/// </remarks>
public static class ViewAccessorRegistry
{
    // viewName (ordinal, case-sensitive — matches ViewRegistry) → { fieldName → accessor }.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, Func<object, object?>>> Accessors =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers the generated accessor map for <paramref name="viewName"/>. Idempotent: the first
    /// registration for a given view name wins; a later registration for the same name is ignored.
    /// </summary>
    /// <param name="viewName">The unique view name the accessors belong to.</param>
    /// <param name="accessors">
    /// The field-name → accessor map for the view's projected row type. The map is stored by reference
    /// and must not be mutated after registration.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="viewName"/> or <paramref name="accessors"/> is <see langword="null"/>.
    /// </exception>
    public static void Register(string viewName, IReadOnlyDictionary<string, Func<object, object?>> accessors)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(accessors);

        // First registration wins; TryAdd silently ignores a repeat for the same view name.
        Accessors.TryAdd(viewName, accessors);
    }

    /// <summary>
    /// Attempts to resolve the read accessor for the field <paramref name="fieldName"/> of the view
    /// <paramref name="viewName"/>.
    /// </summary>
    /// <param name="viewName">The view name to look up.</param>
    /// <param name="fieldName">The projected field/property name to look up.</param>
    /// <param name="accessor">
    /// When this method returns <see langword="true"/>, the accessor that reads the field's value from a
    /// row object; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a generated accessor exists for the (view, field) pair; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="viewName"/> or <paramref name="fieldName"/> is <see langword="null"/>.
    /// </exception>
    public static bool TryGetAccessor(
        string viewName,
        string fieldName,
        [NotNullWhen(true)] out Func<object, object?>? accessor)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(fieldName);

        if (Accessors.TryGetValue(viewName, out var map) && map.TryGetValue(fieldName, out var found))
        {
            accessor = found;
            return true;
        }

        accessor = null;
        return false;
    }
}
