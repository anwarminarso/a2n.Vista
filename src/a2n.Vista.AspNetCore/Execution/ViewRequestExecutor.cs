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
///   <see cref="VistaForbiddenException"/> (→ 403, R7.1). An authorizer that throws while deciding is
///   treated as a deny and also maps to 403 — fail-closed (R7.3). When absent, access is allowed
///   (R7.2).</description></item>
///   <item><description>Build a fresh Core <see cref="ViewScope"/>. When an authorizer is present,
///   <see cref="IViewAuthorizer.ShapeQueryAsync"/> populates it with server-trusted row filters (R6.3) —
///   its default implementation forwards to the synchronous <see cref="IViewAuthorizer.ShapeQuery"/>, so
///   an I/O-backed scope is awaited with the request token instead of blocking (Decision Log D151); when
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
/// <b>Generated invoker preferred, reflection confined (Decision Log D123).</b> After the one-door
/// pipeline, each read/write facet resolves a source-generated <see cref="IViewInvoker"/> from
/// <see cref="ViewInvokerStore"/> by the view's runtime <see cref="ViewMetadata.Name"/> and, on a hit,
/// dispatches through it — reflection-free and AOT-clean (R4.1). On a miss it falls back to the
/// deferred-reflection bridge: <see cref="IViewExecutor.ListAsync{TRow}"/> /
/// <see cref="IViewExecutor.DetailAsync{TRow}"/> / <see cref="IViewExecutor.CreateAsync{TCrud}"/> /
/// <see cref="IViewExecutor.UpdateAsync{TCrud}"/> are generic over a type only known at runtime
/// (<see cref="ViewMetadata.QueryType"/>/<c>CrudType</c>), so the bridge closes them via
/// <see cref="MethodInfo.MakeGenericMethod"/>. That reflection lives in the private
/// <c>*ReflectionAsync</c> helpers, which carry <see cref="RequiresUnreferencedCodeAttribute"/>; the
/// public facets no longer do (the RUC is confined to the fallback branch, R4.2). The endpoint does not
/// branch on invoker origin beyond the resolve step, so the observable result is identical (R4.3).
/// <see cref="DeleteAsync"/> is non-generic and stays a direct executor call.
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

    private static readonly MethodInfo CreateAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.CreateAsync))
        ?? throw new InvalidOperationException($"{nameof(IViewExecutor)}.{nameof(IViewExecutor.CreateAsync)} was not found.");

    private static readonly MethodInfo UpdateAsyncMethod =
        typeof(IViewExecutor).GetMethod(nameof(IViewExecutor.UpdateAsync))
        ?? throw new InvalidOperationException($"{nameof(IViewExecutor)}.{nameof(IViewExecutor.UpdateAsync)} was not found.");

    // Prefix for the per-request authorization memo in HttpContext.Items (D145). Namespaced so it cannot
    // collide with host-owned item keys.
    private const string AuthMemoKeyPrefix = "a2n.Vista.authorized:";

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
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification =
            "The reflection fallback is reached only when no source-generated invoker is registered for " +
            "the view. Covered typed Style B views register a generated invoker, so the RUC branch is " +
            "unreachable under trim/AOT and the generated read path stays warning-free (Decision Log D123, R4.2).")]
    public async Task<object> ListAsync(HttpContext http, string viewName, ViewQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.List).ConfigureAwait(false);

        // Prefer the source-generated, reflection-free dispatch invoker; fall back to reflection (D123, R4.1).
        if (ViewInvokerStore.TryGet(view.Name, out var invoker))
        {
            var result = await invoker.ListAsync(executor, view, request, scope, http.RequestAborted).ConfigureAwait(false);
            return result.BoxedResult;
        }

        return await ListReflectionAsync(executor, view, request, scope, http.RequestAborted).ConfigureAwait(false)
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
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification =
            "The reflection fallback is reached only when no source-generated invoker is registered for " +
            "the view. Covered typed Style B views register a generated invoker, so the RUC branch is " +
            "unreachable under trim/AOT and the generated adapter path stays warning-free (Decision Log D123, R4.2).")]
    public async Task<AdapterListResult> ListForAdapterAsync(HttpContext http, string viewName, ViewQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.List).ConfigureAwait(false);

        // Prefer the generated invoker: its ViewInvocationListResult carries rows + both totals without
        // reflecting over ViewListResult<TRow> (replacing ToAdapterResult, D123 R2.2/R4.1).
        if (ViewInvokerStore.TryGet(view.Name, out var invoker))
        {
            var result = await invoker.ListAsync(executor, view, request, scope, http.RequestAborted).ConfigureAwait(false);
            return new AdapterListResult(result.Rows, result.TotalRowsFiltered, result.TotalRowsUnfiltered);
        }

        var boxed = await ListReflectionAsync(executor, view, request, scope, http.RequestAborted).ConfigureAwait(false)
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
    /// Executes the Export facet and returns the materialized rows for a format writer (Decision Log
    /// D115): runs the one-door pipeline (auth <see cref="ViewFacet.Export"/> + scope), bounds the page to
    /// <see cref="a2n.Vista.Metadata.HardLimits.MaxExportRows"/>, and extracts the rows from the boxed
    /// <see cref="ViewListResult{TRow}"/> via the reflection bridge.
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <param name="viewName">The registered view name.</param>
    /// <param name="request">The neutral query (filter/search/sort/scope) to export.</param>
    /// <returns>The view metadata and the materialized rows (bounded by <c>MaxExportRows</c>).</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification =
            "The reflection fallback is reached only when no source-generated invoker is registered for " +
            "the view. Covered typed Style B views register a generated invoker, so the RUC branch is " +
            "unreachable under trim/AOT and the generated export path stays warning-free (Decision Log D123, R4.2).")]
    public async Task<(ViewMetadata View, IReadOnlyList<object?> Rows)> ExportRowsAsync(
        HttpContext http,
        string viewName,
        ViewQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Export).ConfigureAwait(false);

        var exportRequest = request with { Page = 0, PageSize = view.Limits.MaxExportRows };

        // Prefer the generated invoker: consume the materialized rows without ViewListResult<TRow> reflection.
        if (ViewInvokerStore.TryGet(view.Name, out var invoker))
        {
            var result = await invoker.ListAsync(executor, view, exportRequest, scope, http.RequestAborted).ConfigureAwait(false);
            return (view, result.Rows);
        }

        var boxed = await ListReflectionAsync(executor, view, exportRequest, scope, http.RequestAborted).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"View '{viewName}' Export execution returned no result.");

        return (view, ToAdapterResult(boxed).Rows);
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
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification =
            "The reflection fallback is reached only when no source-generated invoker is registered for " +
            "the view. Covered typed Style B views register a generated invoker, so the RUC branch is " +
            "unreachable under trim/AOT and the generated detail path stays warning-free (Decision Log D123, R4.2).")]
    public async Task<object?> DetailAsync(HttpContext http, string viewName, object key)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(key);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Detail).ConfigureAwait(false);

        // Prefer the source-generated, reflection-free dispatch invoker; fall back to reflection (D123, R4.1).
        if (ViewInvokerStore.TryGet(view.Name, out var invoker))
        {
            return await invoker.DetailAsync(executor, view, key, scope, http.RequestAborted).ConfigureAwait(false);
        }

        return await DetailReflectionAsync(executor, view, key, scope, http.RequestAborted).ConfigureAwait(false);
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
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification =
            "The reflection fallback is reached only when no source-generated invoker is registered for " +
            "the view. Covered typed Style B views register a generated invoker, so the RUC branch is " +
            "unreachable under trim/AOT and the generated export path stays warning-free (Decision Log D123, R4.2).")]
    public async Task<object> ExportAsync(HttpContext http, string viewName, ViewQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Export).ConfigureAwait(false);

        var exportRequest = request with { Page = 0, PageSize = view.Limits.MaxExportRows };

        // Prefer the source-generated, reflection-free dispatch invoker; fall back to reflection (D123, R4.1).
        if (ViewInvokerStore.TryGet(view.Name, out var invoker))
        {
            var result = await invoker.ListAsync(executor, view, exportRequest, scope, http.RequestAborted).ConfigureAwait(false);
            return result.BoxedResult;
        }

        return await ListReflectionAsync(executor, view, exportRequest, scope, http.RequestAborted).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"View '{viewName}' Export execution returned no result.");
    }

    /// <summary>
    /// Executes the Create facet (write): authorizes the <see cref="ViewFacet.Create"/> facet
    /// independently and fail-closed, shapes the server-trusted scope, then inserts a new row from the
    /// typed write model through the Core <see cref="IViewExecutor.CreateAsync{TCrud}"/> port
    /// (Requirements R7.1, R7.2, R7.3, R8.1, R14.3, R14.4).
    /// </summary>
    /// <param name="http">The current HTTP context (carries the user, request services, and cancellation token).</param>
    /// <param name="viewName">The registered (writable) view name being created into.</param>
    /// <param name="model">
    /// The already-bound write model, typed as the view's <c>CrudType</c> (bound by the endpoint via
    /// <see cref="VistaWriteBinding.BindModel"/> before authorization).
    /// </param>
    /// <returns>The non-null primary-key value of the newly inserted row (a scalar or a composite-key map).</returns>
    /// <exception cref="VistaViewNotFoundException">No view is registered under <paramref name="viewName"/> (→ 404).</exception>
    /// <exception cref="VistaForbiddenException">The authorizer denied the request (→ 403).</exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification =
            "The reflection fallback is reached only when no source-generated invoker is registered for " +
            "the view. Covered typed Style B views register a generated invoker, so the RUC branch is " +
            "unreachable under trim/AOT and the generated write path stays warning-free (Decision Log D123, R4.2).")]
    public async Task<object> CreateAsync(HttpContext http, string viewName, object model)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(model);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Create).ConfigureAwait(false);
        RequireCrudType(view);

        // Prefer the source-generated, reflection-free dispatch invoker; fall back to reflection (D123, R4.1).
        if (ViewInvokerStore.TryGet(view.Name, out var invoker))
        {
            return await invoker.CreateAsync(executor, view, model, scope, http.RequestAborted).ConfigureAwait(false);
        }

        return await CreateReflectionAsync(executor, view, model, scope, http.RequestAborted).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"View '{viewName}' Create execution returned no primary key.");
    }

    /// <summary>
    /// Executes the Update facet (write): authorizes the <see cref="ViewFacet.Update"/> facet
    /// independently and fail-closed, shapes the server-trusted scope, then updates the row identified by
    /// <paramref name="key"/> through the Core <see cref="IViewExecutor.UpdateAsync{TCrud}"/> port. The
    /// row identity is taken solely from <paramref name="key"/>, never from the model body
    /// (Requirements R7.1, R7.2, R7.3, R8.1, R14.3, R14.4).
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <param name="viewName">The registered (writable) view name being updated.</param>
    /// <param name="key">The row key (scalar or field-name→value map) read from the request.</param>
    /// <param name="model">The already-bound write model, typed as the view's <c>CrudType</c>.</param>
    /// <param name="concurrencyToken">
    /// The optimistic-concurrency token from the HTTP <c>If-Match</c> header, or <see langword="null"/>
    /// when the view declares none.
    /// </param>
    /// <returns><see langword="true"/> when a row was updated; <see langword="false"/> when no row matched within scope.</returns>
    /// <exception cref="VistaViewNotFoundException">No view is registered under <paramref name="viewName"/> (→ 404).</exception>
    /// <exception cref="VistaForbiddenException">The authorizer denied the request (→ 403).</exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification =
            "The reflection fallback is reached only when no source-generated invoker is registered for " +
            "the view. Covered typed Style B views register a generated invoker, so the RUC branch is " +
            "unreachable under trim/AOT and the generated write path stays warning-free (Decision Log D123, R4.2).")]
    public async Task<bool> UpdateAsync(
        HttpContext http,
        string viewName,
        object key,
        object model,
        string? concurrencyToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(model);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Update).ConfigureAwait(false);
        RequireCrudType(view);

        // Prefer the source-generated, reflection-free dispatch invoker; fall back to reflection (D123, R4.1).
        if (ViewInvokerStore.TryGet(view.Name, out var invoker))
        {
            return await invoker.UpdateAsync(executor, view, key, model, scope, concurrencyToken, http.RequestAborted).ConfigureAwait(false);
        }

        var result = await UpdateReflectionAsync(executor, view, key, model, scope, concurrencyToken, http.RequestAborted).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"View '{viewName}' Update execution returned no result.");
        return (bool)result;
    }

    /// <summary>
    /// Executes the Delete facet (write): authorizes the <see cref="ViewFacet.Delete"/> facet
    /// independently and fail-closed, shapes the server-trusted scope, then deletes the row identified by
    /// <paramref name="key"/> through the Core <see cref="IViewExecutor.DeleteAsync"/> port
    /// (Requirements R7.1, R7.2, R7.3, R8.1, R14.3, R14.4). <see cref="IViewExecutor.DeleteAsync"/> is
    /// non-generic, so no runtime type closing is needed here.
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <param name="viewName">The registered (writable) view name being deleted from.</param>
    /// <param name="key">The row key (scalar or field-name→value map) read from the request.</param>
    /// <param name="concurrencyToken">
    /// The optimistic-concurrency token from the HTTP <c>If-Match</c> header, or <see langword="null"/>
    /// when the view declares none.
    /// </param>
    /// <returns><see langword="true"/> when a row was deleted; <see langword="false"/> when no row matched within scope.</returns>
    /// <exception cref="VistaViewNotFoundException">No view is registered under <paramref name="viewName"/> (→ 404).</exception>
    /// <exception cref="VistaForbiddenException">The authorizer denied the request (→ 403).</exception>
    [RequiresUnreferencedCode("Invokes IViewExecutor.DeleteAsync, whose key resolution is metadata-driven at runtime; use the source generator path for AOT.")]
    public async Task<bool> DeleteAsync(HttpContext http, string viewName, object key, string? concurrencyToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(key);

        var (view, scope, executor) = await AuthorizeAndShapeAsync(http, viewName, ViewFacet.Delete).ConfigureAwait(false);

        return await executor.DeleteAsync(view, key, scope, concurrencyToken, http.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the view's <c>CrudType</c> for the write bridge, or throws when it is absent. A missing
    /// <c>CrudType</c> means the view is not writable; the endpoint gates read-only/unregistered views to
    /// an indistinguishable 404 before reaching this glue (Requirement R12), so this is defense in depth.
    /// </summary>
    private static Type RequireCrudType(ViewMetadata view) =>
        view.CrudType
        ?? throw new InvalidOperationException(
            $"View '{view.Name}' has no CrudType; the write facet is only valid for a writable view.");

    /// <summary>
    /// The authorization gate on its own, without building the scope or resolving the executor: resolves
    /// the view (404 when absent) and consults <see cref="IViewAuthorizer"/> for <paramref name="facet"/>
    /// (403 on deny or on a faulty authorizer).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exposed so the endpoint mapper can authorize <b>before</b> it does observable work (Decision Log
    /// D145). Without it, a write request was bound, its key read, and the <c>428</c> precondition gate
    /// applied before authorization ran, so an unauthorized caller received <c>428</c> or a <c>400</c> bind
    /// error instead of <c>403</c> — disclosing that the view exists, that it is writable, and that it
    /// declares a concurrency token, and letting an unauthenticated client force JSON parsing work.
    /// </para>
    /// <para>
    /// The decision is memoized per request in <see cref="HttpContext.Items"/>, so the later
    /// facet call does not consult the authorizer a second time: an authorizer still sees exactly one
    /// <c>IsAllowedAsync</c> call per (view, facet) per request.
    /// </para>
    /// </remarks>
    /// <param name="http">The current request.</param>
    /// <param name="viewName">The view being addressed.</param>
    /// <param name="facet">The facet being authorized.</param>
    /// <returns>The resolved view metadata.</returns>
    /// <exception cref="VistaViewNotFoundException">No view is registered under <paramref name="viewName"/>.</exception>
    /// <exception cref="VistaForbiddenException">The authorizer denied the facet (or failed to decide).</exception>
    public async ValueTask<ViewMetadata> AuthorizeFacetAsync(
        HttpContext http,
        string viewName,
        ViewFacet facet)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        var (view, _) = await GateAsync(http, viewName, facet).ConfigureAwait(false);
        return view;
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

        var (view, context) = await GateAsync(http, viewName, facet).ConfigureAwait(false);

        var services = http.RequestServices;

        // AspNetCore owns the per-request scope (see ViewScope remarks); it is then handed to the executor.
        IViewScope scope = new ViewScope();

        // Shaping goes through the async door (D151). Its default implementation forwards to the
        // synchronous ShapeQuery, so a sync authorizer is unaffected and allocates nothing extra; an
        // authorizer whose scope needs I/O overrides ShapeQueryAsync and receives the request token
        // instead of blocking a thread-pool thread on GetAwaiter().GetResult().
        //
        // Deliberately NOT inside GateAsync's fail-closed catch: an exception here is a scope-loading
        // fault, not an authorization decision, so it propagates as a 500 rather than being reported as
        // a 403. No rows are served either way.
        var authorizer = services.GetService<IViewAuthorizer>();
        if (authorizer is not null)
        {
            await authorizer.ShapeQueryAsync(context, scope, http.RequestAborted).ConfigureAwait(false);
        }

        var executor = services.GetRequiredService<IViewExecutor>();
        return (view, scope, executor);
    }

    /// <summary>
    /// Resolves the view and applies the authorizer gate for <paramref name="facet"/>, memoizing an
    /// allow decision for the duration of the request so a pre-gate followed by the facet call consults
    /// the authorizer once. Returns the view plus the <see cref="ViewAuthContext"/> that
    /// <c>ShapeQuery</c> must share with the decision (R7.4).
    /// </summary>
    private async ValueTask<(ViewMetadata View, ViewAuthContext Context)> GateAsync(
        HttpContext http,
        string viewName,
        ViewFacet facet)
    {
        var view = _registry.Get(viewName) ?? throw new VistaViewNotFoundException(viewName);

        var services = http.RequestServices;
        var authorizer = services.GetService<IViewAuthorizer>();
        var context = new ViewAuthContext(http.User, viewName, facet, http, services);

        // When no authorizer is registered, access defaults to allow and the scope stays empty (R7.2).
        if (authorizer is null)
        {
            return (view, context);
        }

        var memoKey = AuthMemoKeyPrefix + viewName + ":" + facet;
        if (http.Items.ContainsKey(memoKey))
        {
            // Already allowed earlier in this request (a deny threw, so a memo entry means "allowed").
            return (view, context);
        }

        bool allowed;
        try
        {
            // Awaiting the ValueTask keeps the synchronous-decision common case allocation-free.
            allowed = await authorizer.IsAllowedAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cancelled request is not an authorization failure; let it propagate unchanged.
            throw;
        }
        catch (Exception)
        {
            // R7.3: an authorizer that fails to reach a decision (throws or errors) is treated as a
            // deny and mapped to 403 — fail-closed, so a faulty authorizer can never expose a facet.
            throw new VistaForbiddenException(viewName, facet);
        }

        if (!allowed)
        {
            throw new VistaForbiddenException(viewName, facet);
        }

        http.Items[memoKey] = true;
        return (view, context);
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

    /// <summary>
    /// The reflection (RUC) List dispatch, reached only on a <see cref="ViewInvokerStore"/> miss (Style A,
    /// anonymous/<see cref="object"/> row types, or a view without a generated invoker). Closes the generic
    /// <see cref="IViewExecutor.ListAsync{TRow}"/> over the view's runtime row type via
    /// <see cref="MethodInfo.MakeGenericMethod"/> and awaits the boxed result. Kept separate from the public
    /// facets so the <see cref="RequiresUnreferencedCodeAttribute"/> stays confined to the fallback branch
    /// (Decision Log D123, R4.2). Behavior is identical to the former inline dispatch.
    /// </summary>
    [RequiresUnreferencedCode("Invokes the generic IViewExecutor.ListAsync<TRow> closed over the view's runtime row type; use the source generator path for AOT.")]
    private static async Task<object?> ListReflectionAsync(
        IViewExecutor executor,
        ViewMetadata view,
        ViewQueryRequest request,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        var closed = ListAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, [view, request, scope, cancellationToken])!;
        return await AwaitResultAsync(task).ConfigureAwait(false);
    }

    /// <summary>
    /// The reflection (RUC) Detail dispatch, reached only on a <see cref="ViewInvokerStore"/> miss. Closes
    /// the generic <see cref="IViewExecutor.DetailAsync{TRow}"/> over the view's runtime row type and awaits
    /// the boxed row (or <see langword="null"/>). Kept separate so the RUC stays confined (D123, R4.2).
    /// </summary>
    [RequiresUnreferencedCode("Invokes the generic IViewExecutor.DetailAsync<TRow> closed over the view's runtime row type; use the source generator path for AOT.")]
    private static async Task<object?> DetailReflectionAsync(
        IViewExecutor executor,
        ViewMetadata view,
        object key,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        var closed = DetailAsyncMethod.MakeGenericMethod(view.QueryType);
        var task = (Task)closed.Invoke(executor, [view, key, scope, cancellationToken])!;
        return await AwaitResultAsync(task).ConfigureAwait(false);
    }

    /// <summary>
    /// The reflection (RUC) Create dispatch, reached only on a <see cref="ViewInvokerStore"/> miss. Closes
    /// the generic <see cref="IViewExecutor.CreateAsync{TCrud}"/> over the view's runtime <c>CrudType</c> and
    /// awaits the boxed primary key. Kept separate so the RUC stays confined (D123, R4.2).
    /// </summary>
    [RequiresUnreferencedCode("Invokes the generic IViewExecutor.CreateAsync<TCrud> closed over the view's runtime CrudType; use the source generator path for AOT.")]
    private static async Task<object?> CreateReflectionAsync(
        IViewExecutor executor,
        ViewMetadata view,
        object model,
        IViewScope scope,
        CancellationToken cancellationToken)
    {
        var crudType = RequireCrudType(view);
        var closed = CreateAsyncMethod.MakeGenericMethod(crudType);
        var task = (Task)closed.Invoke(executor, [view, model, scope, cancellationToken])!;
        return await AwaitResultAsync(task).ConfigureAwait(false);
    }

    /// <summary>
    /// The reflection (RUC) Update dispatch, reached only on a <see cref="ViewInvokerStore"/> miss. Closes
    /// the generic <see cref="IViewExecutor.UpdateAsync{TCrud}"/> over the view's runtime <c>CrudType</c> and
    /// awaits the boxed boolean outcome. Row identity comes solely from <paramref name="key"/> and the
    /// concurrency token is passed through unchanged. Kept separate so the RUC stays confined (D123, R4.2).
    /// </summary>
    [RequiresUnreferencedCode("Invokes the generic IViewExecutor.UpdateAsync<TCrud> closed over the view's runtime CrudType; use the source generator path for AOT.")]
    private static async Task<object?> UpdateReflectionAsync(
        IViewExecutor executor,
        ViewMetadata view,
        object key,
        object model,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken)
    {
        var crudType = RequireCrudType(view);
        var closed = UpdateAsyncMethod.MakeGenericMethod(crudType);
        var task = (Task)closed.Invoke(executor, [view, key, model, scope, concurrencyToken, cancellationToken])!;
        return await AwaitResultAsync(task).ConfigureAwait(false);
    }
}
