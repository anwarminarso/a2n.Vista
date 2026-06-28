using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using a2n.Vista.Adapters;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// The request glue for Vista's HTTP layer (Task 10.2). For each request it performs the one-door
/// pipeline from §5.6: resolve view metadata, run the central authorizer, build the server-trusted
/// <see cref="IViewScope"/>, then forward to the Core <see cref="IViewExecutor"/>. The endpoint mapper
/// (Task 10.3) calls into this component and serializes the returned result; the error-mapping
/// middleware (Task 10.4) translates the typed failures it raises.
/// Authoritative behavior: docs/spec/01-view.md §5.6 (one-door auth + ShapeQuery → IViewScope → executor).
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-request pipeline.</b> <see cref="AuthorizeAndShape"/> centralizes the shared steps so every
/// facet behaves identically:
/// </para>
/// <list type="number">
///   <item><description>Resolve <see cref="ViewMetadata"/> from <see cref="IViewRegistry"/>; a miss throws
///   <see cref="VistaViewNotFoundException"/> (→ 404, R1.1).</description></item>
///   <item><description>Resolve <see cref="IViewAuthorizer"/> from the request services. When present,
///   <see cref="IViewAuthorizer.IsAllowedAsync"/> gates the request; <see langword="false"/> throws
///   <see cref="VistaForbiddenException"/> (→ 403, R7.1). When absent, access is allowed (R7.2).</description></item>
///   <item><description>Build a fresh Core <see cref="ViewScope"/>. When an authorizer is present,
///   <see cref="IViewAuthorizer.ShapeQuery"/> populates it with server-trusted row filters (R6.3); when
///   absent the scope stays empty.</description></item>
///   <item><description>Resolve <see cref="IViewExecutor"/> from the request services and invoke the
///   matching facet, passing metadata + request + scope + <see cref="HttpContext.RequestAborted"/>.</description></item>
/// </list>
/// <para>
/// <b>Lifetime.</b> Registered as a singleton: it is stateless and resolves the request-scoped
/// authorizer, executor, and user from <see cref="HttpContext.RequestServices"/> / <see cref="HttpContext.User"/>
/// at call time. The <see cref="IViewScope"/> is constructed per request here (matching <see cref="ViewScope"/>'s
/// "AspNetCore creates one per request" contract), so there is no DI-lifetime coupling on the scope.
/// </para>
/// <para>
/// <b>Reflection bridge (R11.4).</b> <see cref="IViewExecutor.ListAsync{TRow}"/> and
/// <see cref="IViewExecutor.DetailAsync{TRow}"/> are generic over the projected row type, but a view's
/// row type is only known at runtime (<see cref="ViewMetadata.QueryType"/>, often an anonymous type from
/// Gaya A). The bridge closes the generic method with that runtime type via
/// <see cref="MethodInfo.MakeGenericMethod"/> and returns the result as <see cref="object"/> for the
/// endpoint to serialize. This is the same deferred-reflection path used across Pilar 1; it is marked
/// <see cref="RequiresUnreferencedCodeAttribute"/> and is replaced by the source generator (Pilar 3).
/// </para>
/// </remarks>
public sealed class ViewRequestExecutor
{
    private static readonly MethodInfo ListAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.ListAsync))
        ?? throw new InvalidOperationException($"{nameof(IViewExecutor)}.{nameof(IViewExecutor.ListAsync)} was not found.");

    private static readonly MethodInfo DetailAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.DetailAsync))
        ?? throw new InvalidOperationException($"{nameof(IViewExecutor)}.{nameof(IViewExecutor.DetailAsync)} was not found.");

    private readonly IViewRegistry _registry;

    /// <summary>
    /// Initializes a new <see cref="ViewRequestExecutor"/>.
    /// </summary>
    /// <param name="registry">The (singleton) view registry used to resolve metadata by name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    public ViewRequestExecutor(IViewRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// Executes the List facet: authorizes, shapes the server-trusted scope, then runs the executor.
    /// </summary>
    /// <param name="http">The current HTTP context (carries the user, request services, and cancellation token).</param>
    /// <param name="viewName">The registered view name being queried.</param>
    /// <param name="request">The neutral query request (filter/sort/page) parsed by the endpoint (Task 10.3).</param>
    /// <returns>
    /// The boxed <see cref="ViewListResult{TRow}"/> for the view's runtime row type, ready for the endpoint
    /// to serialize.
    /// </returns>
    /// <exception cref="VistaViewNotFoundException">No view is registered under <paramref name="viewName"/> (→ 404).</exception>
    /// <exception cref="VistaForbiddenException">The authorizer denied the request (→ 403).</exception>
    [RequiresUnreferencedCode("Invokes the generic IViewExecutor.ListAsync<TRow> closed over the view's runtime row type; use the source generator path for AOT.")]
    public async Task<object> ListAsync(HttpContext http, string viewName, ViewQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.List).ConfigureAwait(false);

        var closed = ListAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, [view, request, scope, http.RequestAborted])!;
        return await AwaitResultAsync(task).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"View '{viewName}' List execution returned no result.");
    }

    /// <summary>
    /// Executes the List facet for a grid adapter: runs the same one-door pipeline as
    /// <see cref="ListAsync"/>, then converts the boxed <see cref="ViewListResult{TRow}"/> into the
    /// neutral, type-erased <see cref="AdapterListResult"/> the adapter formats (Decision Log D111). The
    /// conversion uses the same deferred-reflection style as the rest of the bridge.
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <param name="viewName">The registered view name being queried.</param>
    /// <param name="request">The neutral query request produced by the adapter's <c>ToQuery</c>.</param>
    /// <returns>The type-erased list result (rows + recordsFiltered + recordsTotal).</returns>
    /// <exception cref="VistaViewNotFoundException">No view is registered under <paramref name="viewName"/> (→ 404).</exception>
    /// <exception cref="VistaForbiddenException">The authorizer denied the request (→ 403).</exception>
    [RequiresUnreferencedCode("Invokes the generic IViewExecutor.ListAsync<TRow> and reflects over ViewListResult<TRow>; use the source generator path for AOT.")]
    public async Task<AdapterListResult> ListForAdapterAsync(HttpContext http, string viewName, ViewQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.List).ConfigureAwait(false);

        var closed = ListAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, [view, request, scope, http.RequestAborted])!;
        var boxed = await AwaitResultAsync(task).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"View '{viewName}' List execution returned no result.");

        return ToAdapterResult(boxed);
    }

    /// <summary>
    /// Converts a boxed <see cref="ViewListResult{TRow}"/> (runtime row type) into an
    /// <see cref="AdapterListResult"/> by reflecting over its <c>Page</c> (<see cref="ViewListResult{TRow}.Page"/>)
    /// and <c>TotalRowsUnfiltered</c>.
    /// </summary>
    [RequiresUnreferencedCode("Reflects over the runtime-closed ViewListResult<TRow>/PagedResult<TRow>; use the source generator path for AOT.")]
    private static AdapterListResult ToAdapterResult(object boxed)
    {
        var resultType = boxed.GetType();
        var page = resultType.GetProperty("Page")!.GetValue(boxed)!;
        var unfiltered = (long)resultType.GetProperty("TotalRowsUnfiltered")!.GetValue(boxed)!;

        var pageType = page.GetType();
        var filtered = (long)pageType.GetProperty("TotalRows")!.GetValue(page)!;
        var items = (IEnumerable)pageType.GetProperty("Items")!.GetValue(page)!;

        var rows = new List<object?>();
        foreach (var item in items)
        {
            rows.Add(item);
        }

        return new AdapterListResult(rows, filtered, unfiltered);
    }

    /// <summary>
    /// Executes the Detail facet: authorizes, shapes the server-trusted scope, then resolves a single row by key.
    /// </summary>
    /// <param name="http">The current HTTP context (carries the user, request services, and cancellation token).</param>
    /// <param name="viewName">The registered view name being read.</param>
    /// <param name="key">The primary-key value identifying the row (converted to the key type by the executor).</param>
    /// <returns>
    /// The boxed projected row for the view's runtime row type, or <see langword="null"/> when no row matches
    /// within the authorized scope (the endpoint maps a null result to HTTP 404).
    /// </returns>
    /// <exception cref="VistaViewNotFoundException">No view is registered under <paramref name="viewName"/> (→ 404).</exception>
    /// <exception cref="VistaForbiddenException">The authorizer denied the request (→ 403).</exception>
    [RequiresUnreferencedCode("Invokes the generic IViewExecutor.DetailAsync<TRow> closed over the view's runtime row type; use the source generator path for AOT.")]
    public async Task<object?> DetailAsync(HttpContext http, string viewName, object key)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(key);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Detail).ConfigureAwait(false);

        var closed = DetailAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, [view, key, scope, http.RequestAborted])!;
        return await AwaitResultAsync(task).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the Metadata facet: authorizes, then returns the view's serializable metadata
    /// (Decision Log D110). Metadata is authorized like any other facet (no implicit anonymous).
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <param name="viewName">The registered view name.</param>
    /// <returns>The serializable <see cref="VistaMetadataResponse"/>.</returns>
    public async Task<VistaMetadataResponse> MetadataAsync(HttpContext http, string viewName)
    {
        ArgumentNullException.ThrowIfNull(http);

        var (view, _, _) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Metadata).ConfigureAwait(false);
        return VistaMetadataResponse.From(view);
    }

    /// <summary>
    /// Executes the Export facet: authorizes (as the higher-risk <see cref="ViewFacet.Export"/>), shapes
    /// the server-trusted scope, then runs the List pipeline bounded by the view's
    /// <see cref="a2n.Vista.Metadata.HardLimits.MaxExportRows"/> (Decision Log D110).
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <param name="viewName">The registered view name.</param>
    /// <param name="request">The neutral query (filter/sort) to export.</param>
    /// <returns>The boxed <see cref="a2n.Vista.Ports.ViewListResult{TRow}"/> for the view's row type.</returns>
    [RequiresUnreferencedCode("Invokes the generic IViewExecutor.ListAsync<TRow> closed over the view's runtime row type; use the source generator path for AOT.")]
    public async Task<object> ExportAsync(HttpContext http, string viewName, ViewQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Export).ConfigureAwait(false);

        var exportRequest = request with { Page = 0, PageSize = view.Limits.MaxExportRows };
        var closed = ListAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, [view, exportRequest, scope, http.RequestAborted])!;
        return await AwaitResultAsync(task).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"View '{viewName}' Export execution returned no result.");
    }

    /// <summary>
    /// Runs the shared one-door pipeline: resolve metadata, gate via the authorizer, and build the
    /// server-trusted scope. Returns the pieces the facet methods need to invoke the executor.
    /// </summary>
    private async ValueTask<(ViewMetadata View, IViewScope Scope, IViewExecutor Executor)> AuthorizeAndShapeAsync(
        HttpContext http,
        string viewName,
        ViewFacet facet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        var view = _registry.Get(viewName) ?? throw new VistaViewNotFoundException(viewName);

        var services = http.RequestServices;
        var authorizer = services.GetService<IViewAuthorizer>();
        var context = new ViewAuthContext(http.User, viewName, facet, http, services);

        // The authorizer call and ShapeQuery share one context instance (R7.4). When no authorizer is
        // registered, access defaults to allow and the scope stays empty (R7.2). Awaiting the ValueTask
        // keeps the synchronous-decision common case allocation-free.
        if (authorizer is not null && !await authorizer.IsAllowedAsync(context).ConfigureAwait(false))
        {
            throw new VistaForbiddenException(viewName, facet);
        }

        // AspNetCore owns the per-request scope (see ViewScope remarks); it is then handed to the executor.
        IViewScope scope = new ViewScope();
        authorizer?.ShapeQuery(context, scope);

        var executor = services.GetRequiredService<IViewExecutor>();
        return (view, scope, executor);
    }

    /// <summary>
    /// Awaits a non-generic <see cref="Task"/> that is actually a <c>Task&lt;TResult&gt;</c> and returns
    /// its boxed result. Used by the reflection bridge because the closed generic executor method's
    /// return type is only known at runtime.
    /// </summary>
    [RequiresUnreferencedCode("Reads Task<TResult>.Result via reflection over a runtime-closed generic Task; use the source generator path for AOT.")]
    private static async Task<object?> AwaitResultAsync(Task task)
    {
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result))
            ?? throw new InvalidOperationException("Expected a Task<TResult> but found no Result property.");
        return resultProperty.GetValue(task);
    }
}
