using a2n.Vista.AspNetCore.Authorization;

namespace a2n.Vista.AspNetCore.Configuration;

/// <summary>
/// The fluent configuration surface for Vista's AspNetCore HTTP layer, returned by
/// <c>AddVistaEndpoints</c>. It owns the HTTP-side cross-cutting settings: the single one-door
/// authorizer (Decision Log D43) and the explicit anonymous-access opt-in (Decision Log D94). The
/// route root is no longer configured here — a view's route is composed at registration (the EF
/// layer's <c>RouteGroup</c>/default root) and recorded in <c>ViewMetadata.Route</c> (D101/D103).
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

    /// <summary>
    /// Explicitly opts into anonymous (no-authorizer) access — serving all views publicly. This is the
    /// deliberate, reviewed way to run open in any environment (D94). Without it, a missing authorizer
    /// is allowed in Development (with a warning) but **fails host startup** in non-Development
    /// environments, so a forgotten authorizer cannot silently expose views in production.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Use this only when public access is intended (e.g. an internal back-office or a public read-only
    /// catalog). It does not register an authorizer; it records that open access is a conscious choice.
    /// </remarks>
    IVistaEndpointBuilder AllowAnonymousAccess();
}
