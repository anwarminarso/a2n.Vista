using System.Diagnostics.CodeAnalysis;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Routing;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Minimal-API endpoint mapping for Vista views (Task 10.3). Lives in the
/// <c>Microsoft.AspNetCore.Builder</c> namespace — by convention, like <c>MapControllers</c> and
/// <c>MapHealthChecks</c> — so <c>app.MapVistaViews()</c> surfaces on <see cref="WebApplication"/> /
/// <see cref="IEndpointRouteBuilder"/> without an extra <c>using</c>.
/// Authoritative behavior: docs/spec/01-view.md §5.6 (routing <c>{root}/{viewName}</c>, D44),
/// §4.6 (facets), §12.3 (endpoint mapping).
/// </summary>
/// <remarks>
/// <para>
/// <b>Routing model.</b> Every view is reached under the global route root
/// (<see cref="VistaEndpointOptions.RouteRoot"/>, default <c>/api/views</c>) plus the view name
/// (R8/D44). Pilar 1 uses a single <b>generic</b> parameterized route per verb that resolves the view
/// by name at request time (<see cref="ViewRequestExecutor"/> → <c>IViewRegistry</c>), rather than
/// emitting per-view endpoints. A generic route works uniformly for anonymous (Gaya A) row types whose
/// CLR type is only known at runtime, and keeps mapping a single call; richer per-view OpenAPI metadata
/// is a source-generator concern (Pilar 3).
/// </para>
/// <para>
/// <b>Verb-to-facet mapping</b> (mirrors <see cref="ViewFacet"/> and §12.3):
/// </para>
/// <list type="bullet">
///   <item><description><c>GET    {root}/{viewName}</c> → List (paging/sort from the query string, <see cref="VistaQueryStringParser"/>).</description></item>
///   <item><description><c>GET    {root}/{viewName}/{key}</c> → Detail (a missing row maps to 404).</description></item>
///   <item><description><c>POST   {root}/{viewName}</c> → Create (write).</description></item>
///   <item><description><c>PUT    {root}/{viewName}/{key}</c> → Update (write).</description></item>
///   <item><description><c>DELETE {root}/{viewName}/{key}</c> → Delete (write).</description></item>
/// </list>
/// <para>
/// This Pilar 1 List mapping reads the neutral <see cref="a2n.Vista.Contracts.ViewQueryRequest"/> from the
/// query string (GET). The §12.3 DataTables mapping (<c>POST {root}/{viewName}/query</c> with a request
/// body and an <c>Accept</c>-driven response shape) is the adapter form introduced in Pilar 2; it layers
/// on top without changing these routes.
/// </para>
/// <para>
/// <b>Read-only views never expose write verbs (R3.3, §4.5).</b> Because the routes are generic, the
/// write handlers resolve the target view and short-circuit with <c>404 Not Found</c> when
/// <see cref="a2n.Vista.Metadata.ViewMetadata.IsReadOnly"/> is <see langword="true"/> — the semantic
/// equivalent of "no write endpoint was generated" for that view.
/// </para>
/// <para>
/// <b>Write execution in Pilar 1.</b> The EF executor's Create/Update/Delete are not implemented in
/// Pilar 1 (they throw, pending the compiled <c>TCrud → entity</c> mapping). For a writable view the
/// write handlers therefore return <c>501 Not Implemented</c> with a clear message rather than invoking
/// an unimplemented path; the routes exist so the surface is stable and so read-only enforcement (R3.3)
/// is observable now. Write wiring lands in a later milestone.
/// </para>
/// <para>
/// <b>Error mapping (Task 10.4).</b> List/Detail forward to <see cref="ViewRequestExecutor"/>, whose
/// typed signals (<see cref="VistaViewNotFoundException"/> → 404, <see cref="VistaForbiddenException"/>
/// → 403) and the Core filter/paging validation errors (400) are intentionally left to propagate here;
/// the Task 10.4 problem-details middleware maps them to RFC 7807 responses.
/// </para>
/// <para>
/// <b>AOT hygiene (R11.4).</b> Mapping drives the reflection bridge in <see cref="ViewRequestExecutor"/>
/// (the view's row type is closed at runtime), so the public map methods carry
/// <see cref="RequiresUnreferencedCodeAttribute"/> consistent with the executor. The AOT-clean route is
/// the source generator (Pilar 3).
/// </para>
/// </remarks>
public static class VistaEndpointRouteBuilderExtensions
{
    private const string AotMessage =
        "Vista endpoint mapping forwards to the reflection bridge in ViewRequestExecutor, which closes "
        + "the generic executor over the view's runtime row type; use the source generator path for AOT.";

    /// <summary>
    /// Maps the generic Vista view endpoints (List/Detail/Create/Update/Delete) under the configured
    /// route root. A single parameterized route per verb resolves the view by name at request time, so
    /// every registered view is served without per-view wiring (R8/D44).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder (typically the <see cref="WebApplication"/>).</param>
    /// <returns>The same <paramref name="endpoints"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(AotMessage)]
    public static IEndpointRouteBuilder MapVistaViews(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = ResolveOptions(endpoints);
        var group = endpoints.MapGroup(options.RouteRoot);
        MapViewRoutes(group, fixedViewName: null);
        return endpoints;
    }

    /// <summary>
    /// Maps the Vista view endpoints for a single, explicitly named view under the configured route
    /// root. Useful when an application wants to expose only specific views (or to control ordering);
    /// the verb-to-facet mapping and read-only enforcement are identical to <see cref="MapVistaViews"/>.
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
    /// form needs a compile-time view-type → name resolution that only the source generator (Pilar 3)
    /// can provide AOT-cleanly; the runtime reflection authoring path is not implemented yet
    /// (<c>IViewRegistry.Register&lt;TView&gt;</c> throws). This name-based overload is the Pilar 1
    /// equivalent and is what the generator-emitted code will call under the covers.
    /// </remarks>
    [RequiresUnreferencedCode(AotMessage)]
    public static IEndpointRouteBuilder MapView(this IEndpointRouteBuilder endpoints, string viewName)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        // Fail fast at startup if the name is not registered, rather than 404-ing every request.
        var registry = endpoints.ServiceProvider.GetService<IViewRegistry>();
        if (registry is not null && registry.Get(viewName) is null)
        {
            throw new InvalidOperationException(
                $"Cannot map endpoints for view '{viewName}' because no view is registered under that name. "
                + "Register it via the EF layer's AddVista(...) before mapping.");
        }

        var options = ResolveOptions(endpoints);
        var group = endpoints.MapGroup(options.RouteRoot);
        MapViewRoutes(group, fixedViewName: viewName);
        return endpoints;
    }

    /// <summary>
    /// Resolves the shared <see cref="VistaEndpointOptions"/> (route root) from DI, falling back to the
    /// defaults when <c>AddVistaEndpoints</c> was not called (the route root still defaults to
    /// <see cref="VistaEndpointOptions.DefaultRouteRoot"/>).
    /// </summary>
    private static VistaEndpointOptions ResolveOptions(IEndpointRouteBuilder endpoints) =>
        endpoints.ServiceProvider.GetService<VistaEndpointOptions>() ?? new VistaEndpointOptions();

    /// <summary>
    /// Adds the five verb routes to <paramref name="group"/>. When <paramref name="fixedViewName"/> is
    /// <see langword="null"/> the routes are parameterized (<c>{viewName}</c>); otherwise the view-name
    /// segment is a literal and the handler uses the captured name.
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static void MapViewRoutes(RouteGroupBuilder group, string? fixedViewName)
    {
        var segment = fixedViewName is null ? "{viewName}" : Uri.EscapeDataString(fixedViewName);
        var listPattern = $"/{segment}";
        var detailPattern = $"/{segment}/{{key}}";

        // Cast each handler to Delegate so it is bound as a route handler (whose returned IResult /
        // Task<IResult> is written to the response) rather than as a RequestDelegate (which would discard
        // the result — see analyzer ASP0016). The lone-HttpContext + Task<IResult> shape otherwise
        // matches RequestDelegate.
        group.MapGet(listPattern, (Delegate)((HttpContext http) => HandleListAsync(http, fixedViewName)));
        group.MapGet(detailPattern, (Delegate)((HttpContext http) => HandleDetailAsync(http, fixedViewName)));
        group.MapPost(listPattern, (Delegate)((HttpContext http) => HandleWrite(http, fixedViewName, ViewFacet.Create)));
        group.MapPut(detailPattern, (Delegate)((HttpContext http) => HandleWrite(http, fixedViewName, ViewFacet.Update)));
        group.MapDelete(detailPattern, (Delegate)((HttpContext http) => HandleWrite(http, fixedViewName, ViewFacet.Delete)));
    }

    /// <summary>Handles the List facet: parse the query string, run the glue, serialize the paged result.</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleListAsync(HttpContext http, string? fixedViewName)
    {
        var viewName = ResolveViewName(http, fixedViewName);
        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        var request = VistaQueryStringParser.Parse(http.Request);

        var result = await executor.ListAsync(http, viewName, request).ConfigureAwait(false);
        return Results.Ok(result);
    }

    /// <summary>Handles the Detail facet: resolve a single row by key; a missing row maps to 404.</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleDetailAsync(HttpContext http, string? fixedViewName)
    {
        var viewName = ResolveViewName(http, fixedViewName);
        var key = ResolveKey(http);
        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();

        var row = await executor.DetailAsync(http, viewName, key).ConfigureAwait(false);
        return row is null ? Results.NotFound() : Results.Ok(row);
    }

    /// <summary>
    /// Handles the write verbs (Create/Update/Delete). Enforces R3.3 (read-only views expose no write
    /// endpoint → 404) and returns 501 for writable views because write execution is not implemented in
    /// Pilar 1.
    /// </summary>
    private static IResult HandleWrite(HttpContext http, string? fixedViewName, ViewFacet facet)
    {
        var viewName = ResolveViewName(http, fixedViewName);
        var registry = http.RequestServices.GetRequiredService<IViewRegistry>();

        var view = registry.Get(viewName);
        if (view is null)
        {
            // Unknown view: same 404 contract as List/Detail (R1.1), surfaced for the Task 10.4 mapper.
            throw new VistaViewNotFoundException(viewName);
        }

        if (view.IsReadOnly)
        {
            // R3.3: a read-only view never exposes a write endpoint. The route exists generically, so we
            // emit the "no such endpoint" result here rather than at map time.
            return Results.NotFound();
        }

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Write facet not implemented",
            detail: $"The '{facet}' facet of view '{viewName}' is not available in Pilar 1. "
                + "Write execution (compiled TCrud-to-entity mapping, concurrency, SaveChanges) lands in a later milestone.");
    }

    /// <summary>
    /// Returns the captured literal view name (single-view mapping) or reads <c>{viewName}</c> from the
    /// route values (generic mapping).
    /// </summary>
    private static string ResolveViewName(HttpContext http, string? fixedViewName)
    {
        if (fixedViewName is not null)
        {
            return fixedViewName;
        }

        return http.Request.RouteValues["viewName"] as string
            ?? throw new InvalidOperationException("The 'viewName' route value was not present on a Vista view endpoint.");
    }

    /// <summary>
    /// Reads the <c>{key}</c> route value as a string. The executor converts it to the primary-key field's
    /// CLR type, so the endpoint stays type-agnostic.
    /// </summary>
    private static string ResolveKey(HttpContext http) =>
        http.Request.RouteValues["key"] as string
            ?? throw new InvalidOperationException("The 'key' route value was not present on a Vista detail/write endpoint.");
}
