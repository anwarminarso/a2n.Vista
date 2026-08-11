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
    /// <para>
    /// This is the synchronous door and stays the default: most shaping composes predicates over the
    /// source entity type from claims already on <see cref="ViewAuthContext.User"/> and performs no I/O.
    /// Resolve any needed services from <see cref="ViewAuthContext.Services"/>.
    /// </para>
    /// <para>
    /// When the scope itself must be <b>loaded</b> (a grants table, an effective-permission query),
    /// override <see cref="ShapeQueryAsync"/> instead of blocking here — see its remarks.
    /// </para>
    /// </remarks>
    void ShapeQuery(ViewAuthContext context, IViewScope scope);

    /// <summary>
    /// Asynchronous counterpart to <see cref="ShapeQuery"/>, for a server-trusted scope that must be
    /// resolved with I/O (for example a per-caller set of accessible ids read from a grants table).
    /// This is the method the pipeline calls; the default implementation forwards to
    /// <see cref="ShapeQuery"/>, so an authorizer that needs no I/O implements only the synchronous one
    /// (Decision Log D151).
    /// </summary>
    /// <param name="context">The request context (caller, view, facet, HTTP, services).</param>
    /// <param name="scope">
    /// The Core <see cref="IViewScope"/> to populate. Predicates added here are AND-ed into the query and
    /// are <b>not</b> validated against the client filter/scope whitelist — they are server-trusted by
    /// design and cannot be bypassed by the client (Requirement R6.3, Decision Log D46).
    /// </param>
    /// <param name="cancellationToken">
    /// The request cancellation token (<c>HttpContext.RequestAborted</c>), so a client abort actually
    /// cancels the scope query.
    /// </param>
    /// <remarks>
    /// <para>
    /// Overriding this member removes the sync-over-async an I/O-backed scope otherwise forces (a parked
    /// thread-pool thread per request, and no token to cancel with). Do <b>not</b> load scope data from
    /// <see cref="IsAllowedAsync"/>: a failure there is treated as a deny and becomes HTTP 403, which
    /// would report a transient data-loading fault as an authorization failure.
    /// </para>
    /// <para>
    /// An exception thrown here is <b>not</b> converted to a deny. It propagates, so a scope that cannot
    /// be loaded surfaces as a server error (HTTP 500) and no rows are served — fail-closed, but honest
    /// about the cause. Cancellation propagates unchanged.
    /// </para>
    /// <para>
    /// Row filters added here are expressed over the view's <c>TSource</c> and applied pre-projection, so
    /// the view must keep its source and projection separate: a class-per-view (Style B) view always
    /// does, and a central-template (Style A) view does when registered through the
    /// <c>AddView&lt;TSource, TRow&gt;(name, source, projection)</c> overload (Decision Log D152). A
    /// Style A view registered through the combined single-delegate overload fails closed instead.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes when the scope has been populated.</returns>
    ValueTask ShapeQueryAsync(ViewAuthContext context, IViewScope scope, CancellationToken cancellationToken)
    {
        ShapeQuery(context, scope);
        return ValueTask.CompletedTask;
    }
}
