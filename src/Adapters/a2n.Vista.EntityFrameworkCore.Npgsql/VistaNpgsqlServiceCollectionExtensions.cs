using a2n.Vista.EntityFrameworkCore.Npgsql;
using a2n.Vista.Filter;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition-root wiring for the Vista PostgreSQL (Npgsql) dialect. Lives in the
/// <c>Microsoft.Extensions.DependencyInjection</c> namespace by .NET convention so
/// <see cref="AddVistaNpgsql"/> surfaces on <see cref="IServiceCollection"/> without an extra
/// <c>using</c>.
/// </summary>
public static class VistaNpgsqlServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the registered <see cref="IQueryDialect"/> with <see cref="NpgsqlQueryDialect"/> so
    /// Vista text search uses PostgreSQL <c>ILIKE</c> (case-insensitive parity). Call after
    /// <c>AddVista(...)</c> (Decision Log D107).
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddVistaNpgsql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IQueryDialect>();
        services.AddSingleton<IQueryDialect, NpgsqlQueryDialect>();
        return services;
    }
}
