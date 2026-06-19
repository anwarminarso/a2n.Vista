using a2n.Vista.Ports;

namespace a2n.Vista.AspNetCore.Authorization;

/// <summary>
/// The single, centralized authorization gate for every view and facet — the "one door" replacing
/// per-view auth attributes.
/// Authoritative shape: docs/spec/01-view.md §5.6 (Decision Log D43/D48).
/// </summary>
/// <remarks>
/// <para>
/// One implementation is registered once (via <c>UseAuthorizer&lt;T&gt;</c>) and becomes the gate for
/// all views and facets, mirroring DynData's <c>IDynDataAPIAuth</c> (Requirement R7).
/// </para>
/// <para>
/// When an authorizer is registered, <see cref="IsAllowedAsync"/> is invoked on every request and a
/// <c>false</c> result maps to HTTP 403 (mapping handled by the endpoint layer, Tasks 10.2/10.4).
/// When no authorizer is registered, access defaults to allow and a startup warning is emitted
/// (Requirements R7.1–R7.3).
/// </para>
/// <para>
/// This interface is HTTP-bound (its context carries <see cref="ViewAuthContext.Http"/>) and therefore
/// lives in <c>a2n.Vista.AspNetCore</c>, whereas <see cref="IViewScope"/> stays in <c>a2n.Vista.Core</c>
/// (Requirement R7.5, Decision Log D48).
/// </para>
/// </remarks>
public interface IViewAuthorizer
{
    /// <summary>
    /// Allow/deny gate for a (view, facet, user) tuple. Called once per request.
    /// </summary>
    /// <param name="context">The request context (caller, view, facet, HTTP, services).</param>
    /// <returns>
    /// <c>true</c> to allow the request; <c>false</c> to deny it (mapped to HTTP 403 by the endpoint layer).
    /// </returns>
    /// <remarks>
    /// Returns <see cref="ValueTask{TResult}"/> for the hot path: synchronous decisions (the common case)
    /// complete without allocating. Cancellation is available via <see cref="ViewAuthContext.Http"/>'s
    /// <c>RequestAborted</c> token.
    /// </remarks>
    ValueTask<bool> IsAllowedAsync(ViewAuthContext context);

    /// <summary>
    /// Injects server-trusted row filters / scope (tenant, ownership) into the view query for this
    /// request. This is the trusted, centralized counterpart to client-supplied contextual filters.
    /// </summary>
    /// <param name="context">The request context (caller, view, facet, HTTP, services).</param>
    /// <param name="scope">
    /// The Core <see cref="IViewScope"/> to populate. Predicates added here are AND-ed into the query and
    /// are <b>not</b> validated against the client filter/scope whitelist — they are server-trusted by
    /// design and cannot be bypassed by the client (Requirement R6.3, Decision Log D46).
    /// </param>
    /// <remarks>
    /// Synchronous by design, matching the authoritative spec: shaping composes predicates over the
    /// source entity type and does not perform I/O. Resolve any needed services from
    /// <see cref="ViewAuthContext.Services"/>.
    /// </remarks>
    void ShapeQuery(ViewAuthContext context, IViewScope scope);
}
