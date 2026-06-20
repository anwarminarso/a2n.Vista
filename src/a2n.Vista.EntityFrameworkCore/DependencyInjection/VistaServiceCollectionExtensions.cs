using a2n.Vista.EntityFrameworkCore;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition-root wiring for Vista on Entity Framework Core (Task 9.4). Lives in the
/// <c>Microsoft.Extensions.DependencyInjection</c> namespace by .NET convention so <c>AddVista</c>
/// surfaces on <see cref="IServiceCollection"/> without an extra <c>using</c>.
/// </summary>
public static class VistaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Vista core services and lets the caller register views through the returned
    /// <see cref="IVistaBuilder"/> (Requirement R11.2 — execution behind ports, wired via DI).
    /// </summary>
    /// <param name="services">The service collection to add Vista to.</param>
    /// <param name="configure">
    /// An optional callback to register views (<c>RegisterTemplate&lt;TTemplate, TDbContext&gt;</c> /
    /// <c>Register&lt;TView&gt;</c>) and configure the route root. Runs synchronously during this call.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Lifetimes.</b> The <see cref="IViewRegistry"/> (metadata) and <see cref="IViewExecutionPlanRegistry"/>
    /// (execution plans) are <b>singletons</b> — built once at startup and read concurrently while
    /// serving requests (R1.2/R1.3). The <see cref="IViewExecutor"/> is <b>scoped</b> because it depends
    /// on the request-scoped <see cref="DbContext"/>.
    /// </para>
    /// <para>
    /// <b>DbContext resolution.</b> The scoped executor is built with the application's
    /// <see cref="DbContext"/>. Since <c>AddDbContext&lt;TContext&gt;</c> registers only <c>TContext</c>
    /// (never the <see cref="DbContext"/> base), the executor factory resolves the <em>captured</em>
    /// context type recorded by <c>RegisterTemplate&lt;TTemplate, TDbContext&gt;</c>
    /// (see <see cref="VistaDbContextAccessor"/>). When no template captured a context type, it falls back
    /// to resolving <see cref="DbContext"/> directly — so callers that register their context as
    /// <see cref="DbContext"/> (or use only Gaya B + a custom plan over a directly-registered context)
    /// still work.
    /// </para>
    /// <para>
    /// <b>Idempotency.</b> Core services are added with <c>TryAdd</c>; the shared registries are reused
    /// across calls so the returned builder always writes into the instances the executor resolves.
    /// Views should be registered in a single <c>AddVista</c> call — registering the same view name
    /// twice fails fast (R1.3).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVista(this IServiceCollection services, Action<IVistaBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Reuse the shared instances across repeat calls so the builder writes into exactly the
        // singletons the executor will resolve.
        var registry = GetOrAddSingletonInstance<IViewRegistry>(services, static () => new ViewRegistry());
        var planRegistry = GetOrAddSingletonInstance<IViewExecutionPlanRegistry>(services, static () => new ViewExecutionPlanRegistry());
        var contextAccessor = GetOrAddSingletonInstance(services, static () => new VistaDbContextAccessor());

        // Scoped executor: resolves the request-scoped DbContext (captured concrete type, or the base).
        services.TryAddScoped<IViewExecutor>(static sp =>
        {
            var accessor = sp.GetRequiredService<VistaDbContextAccessor>();
            var dbContext = accessor.ContextType is null
                ? sp.GetRequiredService<DbContext>()
                : (DbContext)sp.GetRequiredService(accessor.ContextType);

            return new EfViewExecutor(
                dbContext,
                sp,
                sp.GetRequiredService<IViewExecutionPlanRegistry>());
        });

        // A request-scoped scope so the executor can run without an authorizer (AspNetCore, Task 10,
        // replaces/fills this from IViewAuthorizer.ShapeQuery).
        services.TryAddScoped<IViewScope, ViewScope>();

        configure?.Invoke(new VistaBuilder(registry, planRegistry, contextAccessor));

        return services;
    }

    /// <summary>
    /// Returns the already-registered singleton instance of <typeparamref name="TService"/> (matched by
    /// service type and an <see cref="ServiceDescriptor.ImplementationInstance"/>), or creates one with
    /// <paramref name="factory"/>, registers it, and returns it. This keeps <c>AddVista</c> additive: a
    /// repeat call reuses the same registries the executor resolves.
    /// </summary>
    private static TService GetOrAddSingletonInstance<TService>(IServiceCollection services, Func<TService> factory)
        where TService : class
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(TService) && descriptor.ImplementationInstance is TService existing)
            {
                return existing;
            }
        }

        var instance = factory();
        services.AddSingleton(typeof(TService), instance);
        return instance;
    }
}
