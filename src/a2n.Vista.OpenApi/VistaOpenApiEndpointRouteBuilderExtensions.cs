using System.Diagnostics.CodeAnalysis;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.OpenApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Minimal-API mapping for the opt-in Vista OpenAPI <c>Serve_Endpoint</c> (Decision Log D128; spec
/// openapi-emitter, task 7.1). Lives in the <c>Microsoft.AspNetCore.Builder</c> namespace — by convention,
/// like <c>MapControllers</c>, <c>MapHealthChecks</c>, and <c>MapVistaViews</c> — so
/// <c>app.MapVistaOpenApi()</c> surfaces on <see cref="WebApplication"/> / <see cref="IEndpointRouteBuilder"/>
/// without an extra <c>using</c>.
/// </summary>
/// <remarks>
/// <para>
/// Maps <c>GET {EndpointPath}</c> (default <c>/openapi/v1.json</c>, from
/// <see cref="a2n.Vista.OpenApi.VistaOpenApiOptions.EndpointPath"/>) returning the once-built, cached
/// document JSON as <c>application/json</c> (Requirement 11.1). The document is materialized at most once,
/// on the first request, by <see cref="a2n.Vista.OpenApi.VistaOpenApiDocumentCache"/>.
/// </para>
/// <para>
/// <b>Security (Requirement 11.3).</b> The endpoint sits <em>inside</em> the host's normal middleware
/// pipeline and, by default, carries <c>RequireAuthorization()</c>. That default matters: an endpoint with
/// no authorization metadata is <b>anonymous</b> even when <c>UseAuthentication</c>/<c>UseAuthorization</c>
/// are configured, and the document publishes every mapped view's route, operation set, writability, and
/// row/write schemas. The requirement is skipped only when the host opted into anonymous access through the
/// D94 switch (<c>AllowAnonymousAccess()</c>) or explicitly set
/// <see cref="a2n.Vista.OpenApi.VistaOpenApiOptions.RequireAuthorization"/> to <see langword="false"/>. A
/// host that needs a specific policy attaches it to the returned <see cref="IEndpointConventionBuilder"/>.
/// </para>
/// <para>
/// <b>AOT posture (Requirement 13.3).</b> Serving the document reaches the RUC document build, so this
/// method carries <see cref="RequiresUnreferencedCodeAttribute"/> (the build itself is deferred to the
/// first request by the cache).
/// </para>
/// </remarks>
public static class VistaOpenApiEndpointRouteBuilderExtensions
{
    private const string AotMessage =
        "Serving the Vista OpenAPI document builds it once via the RUC document builder, which reflects "
        + "over per-view DTO row/write types; use the envelopes-only document for AOT.";

    /// <summary>
    /// Maps the Vista OpenAPI <c>Serve_Endpoint</c> at the configured
    /// <see cref="a2n.Vista.OpenApi.VistaOpenApiOptions.EndpointPath"/> (Requirement 11.1). Requires
    /// <c>AddVistaOpenApi(...)</c> to have registered the options and the document cache first.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder (typically the <see cref="WebApplication"/>).</param>
    /// <returns>
    /// The <see cref="IEndpointConventionBuilder"/> for the mapped endpoint, so the host can attach its own
    /// conventions (for example <c>.RequireAuthorization(...)</c> or <c>.WithName(...)</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddVistaOpenApi(...)</c> was not called, so the emitter services are not registered (fail-fast).
    /// </exception>
    [RequiresUnreferencedCode(AotMessage)]
    public static IEndpointConventionBuilder MapVistaOpenApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<VistaOpenApiOptions>();
        var endpointOptions = endpoints.ServiceProvider.GetRequiredService<VistaEndpointOptions>();
        var cache = endpoints.ServiceProvider.GetRequiredService<VistaOpenApiDocumentCache>();

        // A normal GET endpoint: it participates in the host auth pipeline and bypasses nothing (R11.3).
        // The cached JSON is built once, on first request, then reused (design "Runtime path").
        var endpoint = endpoints.MapGet(
            options.EndpointPath,
            (Delegate)(() => Results.Text(cache.GetJson(), "application/json")));

        // Secure by default: without authorization metadata this endpoint would be anonymous regardless of
        // the host's authentication/authorization middleware. Skipped for the reviewed D94 anonymous
        // posture (where no authentication scheme need exist) and for the explicit opt-out.
        if (options.RequireAuthorization && !endpointOptions.AllowAnonymous)
        {
            endpoint.RequireAuthorization();
        }

        return endpoint;
    }
}
