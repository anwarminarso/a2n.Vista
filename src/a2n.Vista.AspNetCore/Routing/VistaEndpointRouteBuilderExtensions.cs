using System.Diagnostics.CodeAnalysis;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Routing;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Minimal-API endpoint mapping for Vista views. Lives in the <c>Microsoft.AspNetCore.Builder</c>
/// namespace — by convention, like <c>MapControllers</c> and <c>MapHealthChecks</c> — so
/// <c>app.MapVistaViews()</c> surfaces on <see cref="WebApplication"/> / <see cref="IEndpointRouteBuilder"/>
/// without an extra <c>using</c>.
/// Authoritative behavior: docs/spec/01-view.md §5.6, §4.6 (facets), §12.3 (endpoint mapping),
/// §13.2 (D101/D103 — registration is the single source of a view's route; the mapper reads it verbatim).
/// </summary>
/// <remarks>
/// <para>
/// <b>Routing model (D101/D103).</b> A view's full route is composed at <em>registration</em> time
/// (the EF layer's <c>RouteGroup</c>/default root) and recorded in <see cref="ViewMetadata.Route"/>.
/// This mapper is a <b>dumb mapper</b>: it reads <see cref="IViewRegistry"/> and maps each view at its
/// own <see cref="ViewMetadata.Route"/>, so internal vs external groups (different prefixes) work without
/// any AspNetCore-side route configuration. Each view is mapped to exactly one set of endpoints (one
/// view = one endpoint, R3.5).
/// </para>
/// <para>
/// <b>Verb-to-facet mapping</b> (mirrors <see cref="ViewFacet"/> and §12.3), where <c>{route}</c> is the
/// view's full <see cref="ViewMetadata.Route"/>:
/// </para>
/// <list type="bullet">
///   <item><description><c>GET    {route}</c> → List (paging/sort from the query string, <see cref="VistaQueryStringParser"/>).</description></item>
///   <item><description><c>GET    {route}/{key}</c> → Detail (a missing row maps to 404).</description></item>
///   <item><description><c>POST   {route}</c> → Create (write).</description></item>
///   <item><description><c>PUT    {route}/{key}</c> → Update (write).</description></item>
///   <item><description><c>DELETE {route}/{key}</c> → Delete (write).</description></item>
/// </list>
/// <para>
/// The Pillar 1 List mapping reads the neutral <see cref="a2n.Vista.Contracts.ViewQueryRequest"/> from the
/// query string (GET). The §12.3 DataTables mapping (<c>POST {route}/query</c> with a request body and an
/// <c>Accept</c>-driven response shape) is the adapter form introduced in Pillar 2; it layers on top
/// without changing these routes.
/// </para>
/// <para>
/// <b>Read-only views never expose write verbs (R3.3, §4.5).</b> The write handlers resolve the target
/// view and short-circuit with <c>404 Not Found</c> when <see cref="ViewMetadata.IsReadOnly"/> is
/// <see langword="true"/> — the semantic equivalent of "no write endpoint was generated".
/// </para>
/// <para>
/// <b>Write execution in Pillar 1.</b> The EF executor's Create/Update/Delete are not implemented in
/// Pillar 1, so for a writable view the write handlers return <c>501 Not Implemented</c>; the routes
/// exist so the surface is stable and read-only enforcement (R3.3) is observable now (DR7).
/// </para>
/// <para>
/// <b>AOT hygiene (R11.4).</b> Mapping drives the reflection bridge in <see cref="ViewRequestExecutor"/>
/// (the view's row type is closed at runtime), so the public map methods carry
/// <see cref="RequiresUnreferencedCodeAttribute"/> consistent with the executor. The AOT-clean route is
/// the source generator (Pillar 3).
/// </para>
/// </remarks>
public static class VistaEndpointRouteBuilderExtensions
{
    private const string AotMessage =
        "Vista endpoint mapping forwards to the reflection bridge in ViewRequestExecutor, which closes "
        + "the generic executor over the view's runtime row type; use the source generator path for AOT.";

    /// <summary>
    /// Maps endpoints for every registered Vista view at its own <see cref="ViewMetadata.Route"/>
    /// (composed at registration, D101/D103). Resolves the registry from DI.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder (typically the <see cref="WebApplication"/>).</param>
    /// <returns>The same <paramref name="endpoints"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(AotMessage)]
    public static IEndpointRouteBuilder MapVistaViews(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var registry = endpoints.ServiceProvider.GetRequiredService<IViewRegistry>();
        foreach (var view in registry.All)
        {
            MapSingleView(endpoints, view.Name, view.Route);
        }

        return endpoints;
    }

    /// <summary>
    /// Maps endpoints for a single, explicitly named registered view at its
    /// <see cref="ViewMetadata.Route"/>. Useful when an application wants to expose only specific views.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder (typically the <see cref="WebApplication"/>).</param>
    /// <param name="viewName">The registered view name to expose.</param>
    /// <returns>The same <paramref name="endpoints"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="viewName"/> is <see langword="null"/> or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// No view is registered under <paramref name="viewName"/> at mapping time (fail-fast).
    /// </exception>
    /// <remarks>
    /// The authoritative spec sketches a type-inferred <c>MapView&lt;TView&gt;()</c> (§5.6 example). That
    /// form needs a compile-time view-type → name resolution that only the source generator (Pillar 3)
    /// can provide AOT-cleanly; the runtime reflection authoring path is not implemented yet. This
    /// name-based overload is the Pillar 1 equivalent and is what generator-emitted code will call.
    /// </remarks>
    [RequiresUnreferencedCode(AotMessage)]
    public static IEndpointRouteBuilder MapView(this IEndpointRouteBuilder endpoints, string viewName)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        var registry = endpoints.ServiceProvider.GetRequiredService<IViewRegistry>();
        var view = registry.Get(viewName)
            ?? throw new InvalidOperationException(
                $"Cannot map endpoints for view '{viewName}' because no view is registered under that name. "
                + "Register it via the EF layer's AddVista(...) before mapping.");

        MapSingleView(endpoints, view.Name, view.Route);
        return endpoints;
    }

    /// <summary>
    /// Maps the five verb routes for one view at its full <paramref name="route"/>. The view-name
    /// segment is captured (literal), so handlers use the known name without reading a route value.
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static void MapSingleView(IEndpointRouteBuilder endpoints, string viewName, string route)
    {
        var detailPattern = $"{route}/{{key}}";

        // Cast each handler to Delegate so it is bound as a route handler (whose returned IResult /
        // Task<IResult> is written to the response) rather than as a RequestDelegate (which would discard
        // the result — see analyzer ASP0016).
        endpoints.MapGet(route, (Delegate)((HttpContext http) => HandleListAsync(http, viewName)));
        endpoints.MapGet(detailPattern, (Delegate)((HttpContext http) => HandleDetailAsync(http, viewName)));
        endpoints.MapPost(route, (Delegate)((HttpContext http) => HandleWrite(http, viewName, ViewFacet.Create)));
        endpoints.MapPut(detailPattern, (Delegate)((HttpContext http) => HandleWrite(http, viewName, ViewFacet.Update)));
        endpoints.MapDelete(detailPattern, (Delegate)((HttpContext http) => HandleWrite(http, viewName, ViewFacet.Delete)));
    }

    /// <summary>Handles the List facet: parse the query string, run the glue, serialize the paged result.</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleListAsync(HttpContext http, string viewName)
    {
        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        var request = VistaQueryStringParser.Parse(http.Request);

        var result = await executor.ListAsync(http, viewName, request).ConfigureAwait(false);
        return Results.Ok(result);
    }

    /// <summary>Handles the Detail facet: resolve a single row by key; a missing row maps to 404.</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleDetailAsync(HttpContext http, string viewName)
    {
        var key = ResolveKey(http);
        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();

        var row = await executor.DetailAsync(http, viewName, key).ConfigureAwait(false);
        return row is null ? Results.NotFound() : Results.Ok(row);
    }

    /// <summary>
    /// Handles the write verbs (Create/Update/Delete). Enforces R3.3 (read-only views expose no write
    /// endpoint → 404) and returns 501 for writable views because write execution is not implemented in
    /// Pillar 1 (DR7).
    /// </summary>
    private static IResult HandleWrite(HttpContext http, string viewName, ViewFacet facet)
    {
        var registry = http.RequestServices.GetRequiredService<IViewRegistry>();

        var view = registry.Get(viewName);
        if (view is null)
        {
            throw new VistaViewNotFoundException(viewName);
        }

        if (view.IsReadOnly)
        {
            // R3.3: a read-only view never exposes a write endpoint.
            return Results.NotFound();
        }

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Write facet not implemented",
            detail: $"The '{facet}' facet of view '{viewName}' is not available in Pillar 1. "
                + "Write execution (compiled TCrud-to-entity mapping, concurrency, SaveChanges) lands in a later milestone.");
    }

    /// <summary>
    /// Reads the <c>{key}</c> route value as a string. The executor converts it to the primary-key field's
    /// CLR type, so the endpoint stays type-agnostic.
    /// </summary>
    private static string ResolveKey(HttpContext http) =>
        http.Request.RouteValues["key"] as string
            ?? throw new InvalidOperationException("The 'key' route value was not present on a Vista detail/write endpoint.");
}
