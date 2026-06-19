using a2n.Vista.AspNetCore.Authorization;

namespace a2n.Vista.AspNetCore.Configuration;

/// <summary>
/// The fluent configuration surface for Vista's AspNetCore HTTP layer, returned by
/// <c>AddVistaEndpoints</c>. It owns the two cross-cutting, lint-of-style settings from §5.6: the
/// global route root (Decision Log D44) and the single one-door authorizer (Decision Log D43).
/// </summary>
/// <remarks>
/// <para>
/// This builder is intentionally separate from the Entity Framework layer's <c>IVistaBuilder</c>
/// (which registers views and execution plans). The AspNetCore package must not reference the EF
/// package (Requirement R11.3); the two layers meet only through <c>a2n.Vista.Core</c> ports
/// (<c>IViewRegistry</c>, <c>IViewExecutor</c>, <c>IViewScope</c>) resolved from DI at request time.
/// </para>
/// <para>
/// A typical composition root calls both: <c>services.AddVista(...)</c> (EF — registers views, the
/// executor, and the registry) and <c>services.AddVistaEndpoints(...)</c> (this builder — route root +
/// authorizer).
/// </para>
/// </remarks>
public interface IVistaEndpointBuilder
{
    /// <summary>
    /// Sets the global route root that prefixes every view's live endpoints (<c>{root}/{viewName}</c>,
    /// §5.6/D44). Defaults to <see cref="VistaEndpointOptions.DefaultRouteRoot"/> (<c>/api/views</c>).
    /// </summary>
    /// <param name="root">The route root, for example <c>/api/views</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is <see langword="null"/> or whitespace.</exception>
    IVistaEndpointBuilder RouteRoot(string root);

    /// <summary>
    /// Registers <typeparamref name="T"/> as the single one-door authorizer (§5.6/D43). When configured,
    /// <see cref="IViewAuthorizer.IsAllowedAsync"/> gates every request and a <see langword="false"/>
    /// result maps to HTTP 403 (R7.1). When this is never called, access defaults to allow (R7.2) and the
    /// startup fail-open warning is emitted (R7.3, Task 10.4).
    /// </summary>
    /// <typeparam name="T">The authorizer implementation type.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The authorizer is registered with a <b>scoped</b> lifetime so it can take request-scoped
    /// dependencies (current user/tenant accessors, a scoped <c>DbContext</c>) even though the
    /// authorizer itself is stateless across requests. It is resolved per request by the glue
    /// (<c>ViewRequestExecutor</c>).
    /// </remarks>
    IVistaEndpointBuilder UseAuthorizer<T>() where T : class, IViewAuthorizer;
}
