using a2n.Vista.Adapters;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition-root wiring for Vista grid adapters (Decision Log D112). Lives in the
/// <c>Microsoft.Extensions.DependencyInjection</c> namespace by convention so
/// <see cref="AddVistaAdapter{TAdapter}"/> surfaces on <see cref="IServiceCollection"/> without an extra
/// <c>using</c>.
/// </summary>
public static class VistaAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TAdapter"/> as an <see cref="IViewAdapter"/>. Adapters are
    /// format-level (applied to every view): the endpoint mapper exposes each adapter with a non-null
    /// <see cref="IViewAdapter.RouteSuffix"/> at <c>POST {route}/{suffix}</c> on every mapped view. Added
    /// at most once per concrete adapter type across repeat calls.
    /// </summary>
    /// <typeparam name="TAdapter">The adapter implementation (for example <c>DataTablesAdapter</c>).</typeparam>
    /// <param name="services">The service collection to add the adapter to.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddVistaAdapter<TAdapter>(this IServiceCollection services)
        where TAdapter : class, IViewAdapter
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IViewAdapter, TAdapter>());
        return services;
    }
}
