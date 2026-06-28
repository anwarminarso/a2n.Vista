using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.AspNetCore.Diagnostics;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Hosting;
using a2n.Vista.Export;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition-root wiring for Vista's ASP.NET Core HTTP layer (Task 10.2). Lives in the
/// <c>Microsoft.Extensions.DependencyInjection</c> namespace by .NET convention so
/// <c>AddVistaEndpoints</c> surfaces on <see cref="IServiceCollection"/> without an extra <c>using</c>.
/// </summary>
/// <remarks>
/// This wiring is intentionally independent of the Entity Framework layer's <c>AddVista</c>: the
/// AspNetCore package must not reference <c>a2n.Vista.EntityFrameworkCore</c> (Requirement R11.3). A
/// typical application calls both — <c>AddVista(...)</c> registers the views, the
/// <c>IViewExecutor</c>, and the <c>IViewRegistry</c> (resolved here at request time), while
/// <c>AddVistaEndpoints(...)</c> registers the route root, the one-door authorizer, and the request glue.
/// </remarks>
public static class VistaEndpointServiceCollectionExtensions
{
    /// <summary>
    /// Registers Vista's AspNetCore HTTP services and lets the caller configure the global route root and
    /// the one-door authorizer through the returned <see cref="IVistaEndpointBuilder"/> (§5.6, D43/D44).
    /// </summary>
    /// <param name="services">The service collection to add Vista's HTTP layer to.</param>
    /// <param name="configure">
    /// An optional callback to set <c>RouteRoot(...)</c> and register an authorizer via
    /// <c>UseAuthorizer&lt;T&gt;()</c>. Runs synchronously during this call.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Lifetimes.</b> <see cref="VistaEndpointOptions"/> is a singleton (built once, read by the glue,
    /// the endpoint mapper, and the Task 10.4 startup warning). <see cref="ViewRequestExecutor"/> is a
    /// singleton too — it is stateless and resolves the request-scoped authorizer and executor from the
    /// HTTP context at call time. An authorizer registered via <c>UseAuthorizer&lt;T&gt;()</c> is scoped.
    /// </para>
    /// <para>
    /// <b>Idempotency.</b> The options singleton is reused across repeat calls so the builder always
    /// mutates the instance the glue resolves.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVistaEndpoints(
        this IServiceCollection services,
        Action<IVistaEndpointBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = GetOrAddSingletonInstance(services, static () => new VistaEndpointOptions());
        services.TryAddSingleton<ViewRequestExecutor>();

        // Task 10.4 — fail-open startup warning (R7.3). Added once via TryAddEnumerable so repeat
        // AddVistaEndpoints calls do not register duplicate hosted services; it reads the options
        // singleton above and warns only when no authorizer was registered.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, VistaStartupValidator>());

        // Task 10.4 — RFC 7807 error mapping. Registering the IExceptionHandler lets applications that
        // use the framework's app.UseExceptionHandler() pipeline get Vista's mapping for free. Apps that
        // prefer a single self-contained call use app.UseVistaExceptionHandling() instead; the handler is
        // dormant when neither is wired up. TryAddEnumerable keeps repeat calls idempotent.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExceptionHandler, VistaExceptionHandler>());

        // D115 — built-in export writers (CSV + XLSX). Registered via TryAddEnumerable so repeat calls
        // do not duplicate them; a custom AddVistaExportWriter<T>() registered afterwards overrides a
        // built-in by sharing its Format (the endpoint resolves the last writer per format).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IViewExportWriter, CsvViewExportWriter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IViewExportWriter, XlsxViewExportWriter>());

        configure?.Invoke(new VistaEndpointBuilder(services, options));

        return services;
    }

    /// <summary>
    /// Returns the already-registered singleton instance of <typeparamref name="TService"/> (matched by
    /// service type and an <see cref="ServiceDescriptor.ImplementationInstance"/>), or creates one with
    /// <paramref name="factory"/>, registers it, and returns it. Keeps <c>AddVistaEndpoints</c> additive:
    /// a repeat call reuses the same options instance the builder and glue read.
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
