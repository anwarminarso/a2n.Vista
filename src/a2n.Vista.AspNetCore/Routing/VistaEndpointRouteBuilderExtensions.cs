using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using a2n.Vista.Adapters;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
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
/// <b>Action-style surface (D110), where <c>{route}</c> is the view's full <see cref="ViewMetadata.Route"/>:</b>
/// </para>
/// <list type="bullet">
///   <item><description><c>POST {route}/list</c> → List (query in the JSON body).</description></item>
///   <item><description><c>POST {route}/detail</c> → Detail (key in the JSON body; a missing row → 404).</description></item>
///   <item><description><c>GET  {route}/metadata</c> → Metadata (cacheable).</description></item>
///   <item><description><c>POST {route}/export</c> → Export (query in the body, bounded by MaxExportRows).</description></item>
///   <item><description><c>POST {route}/create|update|delete</c> → write (payload in the body).</description></item>
/// </list>
/// <para>
/// The query and key travel in the JSON request body (Decision Log D110), so composite keys and rich
/// filter trees need no URL encoding. Bodies are (de)serialized with
/// <see cref="a2n.Vista.AspNetCore.Serialization.VistaJson"/> (polymorphic <c>FilterNode</c> converter).
/// </para>
/// <para>
/// <b>Read-only views expose only read actions (D38).</b> The write actions are not mapped for a
/// read-only view; if reached anyway, the write handler returns <c>404 Not Found</c>.
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
            MapSingleView(endpoints, view);
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

        MapSingleView(endpoints, view);
        return endpoints;
    }

    /// <summary>
    /// Maps the action-style endpoints for one view at its full <paramref name="view"/> route
    /// (Decision Log D110): <c>POST {route}/list|detail|export</c> + <c>GET {route}/metadata</c> for
    /// reads, and <c>POST {route}/create|update|delete</c> for writes (write actions are not mapped for
    /// a read-only view, D38). The key and query travel in the JSON request body.
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static void MapSingleView(IEndpointRouteBuilder endpoints, ViewMetadata view)
    {
        var route = view.Route;
        var name = view.Name;

        endpoints.MapPost($"{route}/list", (Delegate)((HttpContext http) => HandleListAsync(http, view)));
        endpoints.MapPost($"{route}/detail", (Delegate)((HttpContext http) => HandleDetailAsync(http, name)));
        endpoints.MapGet($"{route}/metadata", (Delegate)((HttpContext http) => HandleMetadataAsync(http, name)));
        endpoints.MapPost($"{route}/export", (Delegate)((HttpContext http) => HandleExportAsync(http, view)));

        // Grid adapters (Decision Log D112): each registered adapter with a route suffix gets a read
        // endpoint at POST {route}/{suffix} (for example DataTables → POST {route}/datatable).
        foreach (var adapter in endpoints.ServiceProvider.GetServices<IViewAdapter>())
        {
            if (string.IsNullOrEmpty(adapter.RouteSuffix))
            {
                continue;
            }

            var captured = adapter;
            endpoints.MapPost(
                $"{route}/{captured.RouteSuffix}",
                (Delegate)((HttpContext http) => HandleAdapterAsync(http, view, captured)));
        }

        if (!view.IsReadOnly)
        {
            endpoints.MapPost($"{route}/create", (Delegate)((HttpContext http) => HandleWrite(http, name, ViewFacet.Create)));
            endpoints.MapPost($"{route}/update", (Delegate)((HttpContext http) => HandleWrite(http, name, ViewFacet.Update)));
            endpoints.MapPost($"{route}/delete", (Delegate)((HttpContext http) => HandleWrite(http, name, ViewFacet.Delete)));
        }
    }

    /// <summary>Handles <c>POST {route}/list</c>: read the query body (filter/search/sort/paging), run the glue.</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleListAsync(HttpContext http, ViewMetadata view)
    {
        var body = await ReadBodyAsync<VistaListRequestBody>(http).ConfigureAwait(false) ?? new VistaListRequestBody();
        var request = VistaSearchMerge.Apply(view, body.ToBaseRequest(), body.Search);

        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        var result = await executor.ListAsync(http, view.Name, request).ConfigureAwait(false);
        return Results.Ok(result);
    }

    /// <summary>Handles <c>POST {route}/detail</c>: read the key from the body; a missing row maps to 404.</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleDetailAsync(HttpContext http, string viewName)
    {
        var body = await ReadBodyAsync<VistaDetailRequestBody>(http).ConfigureAwait(false)
            ?? throw new VistaInvalidRequestException("A detail request requires a JSON body with a 'key'.");
        var key = VistaKeyReader.Read(body.Key);

        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        var row = await executor.DetailAsync(http, viewName, key).ConfigureAwait(false);
        return row is null ? Results.NotFound() : Results.Ok(row);
    }

    /// <summary>Handles <c>GET {route}/metadata</c>: authorize, then return the serializable metadata.</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleMetadataAsync(HttpContext http, string viewName)
    {
        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        var metadata = await executor.MetadataAsync(http, viewName).ConfigureAwait(false);

        var options = http.RequestServices.GetRequiredService<VistaEndpointOptions>();
        if (!options.EnableMetadataCaching)
        {
            return Results.Ok(metadata);
        }

        // Metadata is stable between deploys; an ETag (hash of the serialized payload) lets clients skip
        // re-downloading it. Off by default so edits are visible immediately during development (D110 follow-up).
        var json = JsonSerializer.Serialize(metadata, VistaJson.Options);
        var etag = ComputeETag(json);

        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = $"private, max-age={options.MetadataCacheMaxAgeSeconds}";

        if (string.Equals(http.Request.Headers.IfNoneMatch.ToString(), etag, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Content(json, "application/json");
    }

    /// <summary>Computes a strong, quoted <c>ETag</c> from the serialized metadata payload (SHA-256 hex).</summary>
    private static string ComputeETag(string json)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"\"{Convert.ToHexString(hash)}\"";
    }

    /// <summary>
    /// Handles <c>POST {route}/{adapter.RouteSuffix}</c>: build the neutral <see cref="AdapterRequest"/>
    /// from the request, run the adapter's Bind → ToQuery → (one-door List) → ToResponse pipeline, and
    /// serialize the grid-specific response (Decision Log D112). A bind failure surfaces as
    /// <see cref="AdapterBindException"/> (mapped to 400).
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleAdapterAsync(HttpContext http, ViewMetadata view, IViewAdapter adapter)
    {
        var raw = await AdapterRequestFactory.CreateAsync(http, view.Name).ConfigureAwait(false);

        var request = adapter.BindRequest(raw);
        var query = adapter.ToQuery(request, view);

        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        var result = await executor.ListForAdapterAsync(http, view.Name, query).ConfigureAwait(false);

        var response = adapter.ToResponse(result, request, view);

        // The response's compile-time type is erased to object here; serialize by its runtime type so the
        // grid shape (and the projected rows) are emitted. Anonymous Style A rows ride the documented RUC
        // serialization path (D96).
        var json = JsonSerializer.Serialize(response, response.GetType(), VistaJson.Options);
        return Results.Content(json, "application/json");
    }

    /// <summary>Handles <c>POST {route}/export</c>: read the query body, run the export (bounded by MaxExportRows).</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleExportAsync(HttpContext http, ViewMetadata view)
    {
        var body = await ReadBodyAsync<VistaListRequestBody>(http).ConfigureAwait(false) ?? new VistaListRequestBody();
        var request = VistaSearchMerge.Apply(view, body.ToBaseRequest(), body.Search);

        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        var result = await executor.ExportAsync(http, view.Name, request).ConfigureAwait(false);
        return Results.Ok(result);
    }

    /// <summary>
    /// Reads and deserializes the JSON request body using <see cref="VistaJson.Options"/>. Returns
    /// <see langword="null"/> for an empty body. Malformed JSON surfaces as a
    /// <see cref="VistaInvalidRequestException"/> (mapped to 400).
    /// </summary>
    private static async Task<T?> ReadBodyAsync<T>(HttpContext http)
        where T : class
    {
        if (http.Request.ContentLength is 0)
        {
            return null;
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                http.Request.Body, VistaJson.Options, http.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new VistaInvalidRequestException($"The request body is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the write actions (Create/Update/Delete). Enforces D38 (read-only views expose no write
    /// action — though such views are not even mapped) and returns 501 for writable views because write
    /// execution is not implemented in Pillar 1 (DR7).
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
            return Results.NotFound();
        }

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Write facet not implemented",
            detail: $"The '{facet}' facet of view '{viewName}' is not available in Pillar 1. "
                + "Write execution (compiled TCrud-to-entity mapping, concurrency, SaveChanges) lands in a later milestone.");
    }
}
