using a2n.Vista.EntityFrameworkCore;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.EntityFrameworkCore.Hosting;
using a2n.Vista.Filter;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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

        // Process-scoped write-facet registry (Decision Log D119, R13.1): populated at registration time
        // alongside the view/plan stores with each writable view's captured CrudFacetDefinition, and read
        // by the EF execution layer (the reflection write mapper). It lives in Core so neither adapter
        // references the other to reach the write facet (R14.6). Registered under the concrete type so the
        // builder can populate it, and exposed via the IWriteFacetRegistry port for DI consumers.
        var writeFacetRegistry = GetOrAddSingletonInstance(services, static () => new WriteFacetRegistry());
        services.TryAddSingleton<IWriteFacetRegistry>(static sp => sp.GetRequiredService<WriteFacetRegistry>());

        // Write-mapper resolver (Decision Log D119, R13.1–R13.4): the single seam that resolves one
        // WriteMapper per write, preferring a source-generated mapper (GeneratedWriteMapperStore) over the
        // RUC reflection fallback so the executor never branches on the implementation. Registered as a
        // singleton so its reflection mapper's per-view compiled-delegate cache is shared process-wide;
        // the scoped executor obtains it from the request IServiceProvider.
        services.TryAddSingleton<WriteMapperResolver>(static sp =>
            new WriteMapperResolver(sp.GetRequiredService<IWriteFacetRegistry>()));

        // Default provider dialect (Decision Log D107): SQL-standard LIKE with wildcard escaping. A
        // provider package (for example a2n.Vista.EntityFrameworkCore.Npgsql via AddVistaNpgsql())
        // replaces this with a provider-specific dialect (ILIKE) before the executor resolves it.
        services.TryAddSingleton<IQueryDialect, DefaultQueryDialect>();

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

        // Startup provider guard (Decision Log D107, R4.6): verify the registered dialect matches the
        // active EF Core provider. Added at most once across repeat AddVista calls.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, VistaDialectStartupValidator>());

        // Startup model hook (Decision Log D105 / M11, R6): derive ViewMetadata.KeyFields from
        // DbContext.Model for single-source executable views that declared no key. Added at most once
        // across repeat AddVista calls (R6.7) and never runs on the request hot path (R6.8).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, VistaModelKeyDerivationService>());

        // Startup concurrency guard (Decision Log D146): a view that declares WithConcurrencyToken(...) must
        // select a property the EF model treats as a concurrency token, otherwise the database emits no
        // atomic UPDATE ... WHERE predicate and the Vista-level pre-check alone allows a lost update.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, VistaConcurrencyTokenStartupValidator>());

        // Per-request sink for the post-write concurrency token (Decision Log D146): the executor publishes
        // the token it read back after a successful update; the AspNetCore mapper echoes it as the ETag
        // instead of round-tripping the client's stale If-Match value.
        services.TryAddScoped<IWriteTokenSink, WriteTokenSink>();

        configure?.Invoke(new VistaBuilder(registry, planRegistry, contextAccessor, writeFacetRegistry));

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
