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
using a2n.Vista.Export;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;
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
/// <b>Write execution (M12).</b> For a writable view the write handlers bind the request, authorize the
/// write facet fail-closed, forward to the Core <c>IViewExecutor</c> write facet, and map the outcome to
/// HTTP: <c>200</c> with the created row's primary key (create) or an <c>ETag</c> header carrying the
/// current concurrency token (update/delete on a token view), <c>404</c> for a no-match / read-only /
/// unregistered / no-plan target (indistinguishable), <c>428</c> when a token view omits <c>If-Match</c>,
/// and <c>409</c> / <c>4xx</c> / <c>5xx</c> for the typed write failures (Requirements R1.2, R2.2, R3.2,
/// R6.x, R12.x, R16.6).
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

        // Metadata schema adapters (Decision Log D116): each registered schema adapter with a route suffix
        // gets a GET endpoint at GET {route}/{suffix} (for example QueryBuilder → GET {route}/querybuilder).
        foreach (var schemaAdapter in endpoints.ServiceProvider.GetServices<IViewMetadataAdapter>())
        {
            if (string.IsNullOrEmpty(schemaAdapter.RouteSuffix))
            {
                continue;
            }

            var capturedSchema = schemaAdapter;
            endpoints.MapGet(
                $"{route}/{capturedSchema.RouteSuffix}",
                (Delegate)((HttpContext http) => HandleSchemaAsync(http, view, capturedSchema)));
        }

        if (!view.IsReadOnly)
        {
            endpoints.MapPost($"{route}/create", (Delegate)((HttpContext http) => HandleWriteAsync(http, name, ViewFacet.Create)));
            endpoints.MapPost($"{route}/update", (Delegate)((HttpContext http) => HandleWriteAsync(http, name, ViewFacet.Update)));
            endpoints.MapPost($"{route}/delete", (Delegate)((HttpContext http) => HandleWriteAsync(http, name, ViewFacet.Delete)));
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

        // Serialize the boxed ViewListResult<TRow> through the seam by its runtime type (D124): resolve
        // the JsonTypeInfo via VistaJson.Options and write with the AOT-safe overload. This replaces the
        // framework Results.Ok(obj) pipeline while preserving the 200 status and byte-for-byte body (R5.2).
        return VistaJsonWriter.Json(result, result.GetType());
    }

    /// <summary>Handles <c>POST {route}/detail</c>: read the key from the body; a missing row maps to 404.</summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleDetailAsync(HttpContext http, string viewName)
    {
        var body = await ReadBodyAsync<VistaDetailRequestBody>(http).ConfigureAwait(false)
            ?? throw new VistaInvalidRequestException("A detail request requires a JSON body with a 'key'.");

        // A present-but-keyless envelope (for example `{}`) leaves Key at its default Undefined kind; an
        // explicit `null` key is equally unusable. Both are malformed per R2.5 and must surface as the
        // 400 contract rather than letting VistaKeyReader's raw JsonException escape as an unhandled 500.
        if (body.Key.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new VistaInvalidRequestException("A detail request requires a 'key'.");
        }

        object key;
        try
        {
            key = VistaKeyReader.Read(body.Key);
        }
        catch (JsonException ex)
        {
            // Keep VistaKeyReader serializer-neutral: it signals unreadable keys with JsonException; the
            // handler translates that into the read-path 400 contract so no System.Text.Json type leaks.
            throw new VistaInvalidRequestException($"The request 'key' is invalid: {ex.Message}");
        }

        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        var row = await executor.DetailAsync(http, viewName, key).ConfigureAwait(false);

        // A missing row keeps the 404 contract; a present row is serialized through the seam by its
        // runtime row type (D124), preserving the 200 status and byte-for-byte body (R5.2).
        return row is null ? Results.NotFound() : VistaJsonWriter.Json(row, row.GetType());
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
    /// Handles <c>GET {route}/{schemaAdapter.RouteSuffix}</c>: authorize (Metadata facet), then emit the
    /// grid-specific metadata schema verbatim (Decision Log D116).
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleSchemaAsync(HttpContext http, ViewMetadata view, IViewMetadataAdapter adapter)
    {
        // Authorize like the metadata facet before disclosing the schema (no implicit anonymous).
        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();
        _ = await executor.MetadataAsync(http, view.Name).ConfigureAwait(false);

        var schema = adapter.BuildSchema(view);
        // Dictionary-based schema → keys serialize verbatim (DynData-compatible casing).
        var json = JsonSerializer.Serialize(schema, schema.GetType(), VistaJson.Options);
        return Results.Content(json, "application/json");
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

    /// <summary>
    /// Handles <c>POST {route}/export</c>: read the query body, then either format a file (when a
    /// <c>format</c> is supplied and a writer is registered, D115) or return the JSON
    /// <see cref="ViewListResult{TRow}"/> (backward compatible when no format).
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleExportAsync(HttpContext http, ViewMetadata view)
    {
        var body = await ReadBodyAsync<VistaListRequestBody>(http).ConfigureAwait(false) ?? new VistaListRequestBody();
        var request = VistaSearchMerge.Apply(view, body.ToBaseRequest(), body.Search);
        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();

        if (string.IsNullOrWhiteSpace(body.Format))
        {
            // No format → preserve the JSON ViewListResult behavior, now serialized through the seam by
            // its runtime type (D124): 200 status and byte-for-byte body unchanged (R5.2).
            var jsonResult = await executor.ExportAsync(http, view.Name, request).ConfigureAwait(false);
            return VistaJsonWriter.Json(jsonResult, jsonResult.GetType());
        }

        var writer = ResolveExportWriter(http, body.Format)
            ?? throw new VistaInvalidRequestException(
                $"Export format '{body.Format}' is not supported. Register a writer via AddVistaExportWriter<T>() "
                + "or use a built-in format (csv, xlsx).");

        var (resolvedView, rows) = await executor.ExportRowsAsync(http, view.Name, request).ConfigureAwait(false);

        var buffer = new MemoryStream();
        await writer.WriteAsync(buffer, resolvedView, rows, http.RequestAborted).ConfigureAwait(false);
        buffer.Position = 0;

        return Results.File(
            buffer.ToArray(),
            contentType: writer.ContentType,
            fileDownloadName: $"{resolvedView.Name}.{writer.FileExtension}");
    }

    /// <summary>Resolves the registered <see cref="IViewExportWriter"/> for <paramref name="format"/> (case-insensitive; last wins).</summary>
    private static IViewExportWriter? ResolveExportWriter(HttpContext http, string format)
    {
        IViewExportWriter? match = null;
        foreach (var writer in http.RequestServices.GetServices<IViewExportWriter>())
        {
            if (string.Equals(writer.Format, format, StringComparison.OrdinalIgnoreCase))
            {
                match = writer; // last registration wins → custom overrides built-in
            }
        }

        return match;
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
    /// Handles the write actions (Create/Update/Delete) as a dumb mapper (Decision Log D110, D120):
    /// resolve the view, gate to an indistinguishable <c>404</c>, bind the request, enforce the <c>428</c>
    /// precondition gate, forward to <see cref="ViewRequestExecutor"/>, and map the outcome to HTTP. All
    /// typed write failures ride the shared RFC 7807 envelope via
    /// <see cref="a2n.Vista.AspNetCore.Diagnostics.VistaProblemResults"/> (Requirements R1.2, R1.4, R1.5,
    /// R2.2, R2.6, R3.2, R3.5, R6.1, R6.4, R6.6, R12.1–R12.4, R16.6).
    /// </summary>
    /// <remarks>
    /// The route is only mapped for <c>!view.IsReadOnly</c>, so a read-only or missing view can only be
    /// reached by a hand-crafted request; both — together with a writable view that carries no
    /// executable write plan (the executor raises <see cref="WriteErrorCode.NoWritePlan"/>) — collapse to
    /// the same bodyless <c>404</c> so the write surface of a read-only view is undiscoverable
    /// (Requirements R12.2, R12.3, R12.4).
    /// </remarks>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleWriteAsync(HttpContext http, string viewName, ViewFacet facet)
    {
        var registry = http.RequestServices.GetRequiredService<IViewRegistry>();
        var view = registry.Get(viewName);

        // Indistinguishable 404 for an unregistered, read-only, or non-writable (no CrudType) target
        // (R1.4, R1.5, R12.2, R12.3). The read-only / null branches are defense in depth: write routes
        // are only mapped for a writable view, so these are unreachable through normal mapping.
        if (view is null || view.IsReadOnly || view.CrudType is null)
        {
            return Results.NotFound();
        }

        var executor = http.RequestServices.GetRequiredService<ViewRequestExecutor>();

        try
        {
            var body = await VistaWriteBinding.ReadBodyAsync(http).ConfigureAwait(false);

            return facet switch
            {
                ViewFacet.Create => await HandleCreateAsync(http, view, body, executor).ConfigureAwait(false),
                ViewFacet.Update => await HandleUpdateAsync(http, view, body, executor).ConfigureAwait(false),
                ViewFacet.Delete => await HandleDeleteAsync(http, view, body, executor).ConfigureAwait(false),
                _ => Results.NotFound(),
            };
        }
        catch (VistaWriteException ex) when (ex.Code == WriteErrorCode.NoWritePlan)
        {
            // R12.3/R12.4: a writable view registered as metadata-only (no executable write plan) is
            // rendered as a plain 404 — indistinguishable from a genuinely nonexistent view, never a
            // coded body that would disclose the view exists.
            return Results.NotFound();
        }
    }

    /// <summary>
    /// Handles <c>POST {route}/create</c>: bind the write model, insert through the executor, and return
    /// <c>200</c> with a minimal <see cref="VistaWriteResponse"/> carrying only the new primary key
    /// (Requirements R1.1, R1.2, R10.1).
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleCreateAsync(
        HttpContext http,
        ViewMetadata view,
        VistaWriteRequestBody body,
        ViewRequestExecutor executor)
    {
        var model = VistaWriteBinding.BindModel(body, view.CrudType!);
        var key = await executor.CreateAsync(http, view.Name, model).ConfigureAwait(false);
        return Results.Ok(new VistaWriteResponse(key));
    }

    /// <summary>
    /// Handles <c>POST {route}/update</c>: bind the model and the request key, enforce the <c>428</c>
    /// precondition gate for a token view, update through the executor, and map the result — <c>404</c>
    /// when no row matched within scope, otherwise <c>200</c> (with the round-tripped <c>ETag</c> when the
    /// view declares a token). The row identity is taken solely from the request key, never the body
    /// (Requirements R2.1, R2.2, R2.3, R2.5, R2.8, R6.1, R6.2, R6.4, R6.6).
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleUpdateAsync(
        HttpContext http,
        ViewMetadata view,
        VistaWriteRequestBody body,
        ViewRequestExecutor executor)
    {
        var model = VistaWriteBinding.BindModel(body, view.CrudType!);
        var key = VistaWriteBinding.ReadKey(body);
        var hasToken = ViewDeclaresConcurrencyToken(http, view.Name);
        var ifMatch = ResolvePrecondition(http, hasToken);

        var updated = await executor
            .UpdateAsync(http, view.Name, key, model, ifMatch)
            .ConfigureAwait(false);

        return updated ? WriteOk(http, hasToken, ifMatch) : Results.NotFound();
    }

    /// <summary>
    /// Handles <c>POST {route}/delete</c>: read the request key, enforce the <c>428</c> precondition gate
    /// for a token view, delete through the executor, and map the result — <c>404</c> when no row matched
    /// within scope, otherwise <c>200</c> (with the round-tripped <c>ETag</c> when the view declares a
    /// token) (Requirements R3.1, R3.2, R3.3, R6.1, R6.2, R6.4, R6.6).
    /// </summary>
    [RequiresUnreferencedCode(AotMessage)]
    private static async Task<IResult> HandleDeleteAsync(
        HttpContext http,
        ViewMetadata view,
        VistaWriteRequestBody body,
        ViewRequestExecutor executor)
    {
        var key = VistaWriteBinding.ReadKey(body);
        var hasToken = ViewDeclaresConcurrencyToken(http, view.Name);
        var ifMatch = ResolvePrecondition(http, hasToken);

        var deleted = await executor
            .DeleteAsync(http, view.Name, key, ifMatch)
            .ConfigureAwait(false);

        return deleted ? WriteOk(http, hasToken, ifMatch) : Results.NotFound();
    }

    /// <summary>
    /// Reads the <c>If-Match</c> precondition and applies the token gate: for a view that declares a
    /// concurrency token a missing/blank header is <c>428</c> (Requirement R6.2); for a tokenless view
    /// any header is ignored and <see langword="null"/> flows to the executor (Requirement R6.6).
    /// </summary>
    private static string? ResolvePrecondition(HttpContext http, bool hasToken)
    {
        var ifMatch = VistaWriteBinding.ReadIfMatch(http);

        if (!hasToken)
        {
            // R6.6: a tokenless view performs the write without any precondition and ignores If-Match.
            return null;
        }

        // R6.2: a token view requires a non-blank If-Match before the executor is touched.
        return ifMatch ?? throw new VistaPreconditionRequiredException();
    }

    /// <summary>
    /// Produces the <c>200</c> success result for an update/delete, round-tripping the concurrency token
    /// into the <c>ETag</c> response header when the view declares one (Requirement R6.4).
    /// </summary>
    /// <remarks>
    /// The Core <see cref="IViewExecutor"/> update/delete facet reports success as a <see cref="bool"/>,
    /// so the token echoed here is the client-supplied <c>If-Match</c> value (guaranteed non-null for a
    /// token view by <see cref="ResolvePrecondition"/>). A post-write token that differs from the
    /// precondition would require the port to surface it; that is a later refinement and does not change
    /// this endpoint's wiring.
    /// </remarks>
    private static IResult WriteOk(HttpContext http, bool hasToken, string? token)
    {
        if (hasToken && token is not null)
        {
            http.Response.Headers.ETag = token;
        }

        return Results.Ok();
    }

    /// <summary>
    /// Returns <see langword="true"/> when the view declares an optimistic-concurrency token, consulting
    /// the Core <see cref="IWriteFacetRegistry"/> (EF-free; no adapter cross-reference, Requirement
    /// R14.5/R14.6). Drives both the <c>428</c> gate and the <c>ETag</c> round-trip.
    /// </summary>
    private static bool ViewDeclaresConcurrencyToken(HttpContext http, string viewName)
    {
        var facets = http.RequestServices.GetRequiredService<IWriteFacetRegistry>();
        return facets.TryGet(viewName, out var facet) && facet.ConcurrencyToken is not null;
    }
}
