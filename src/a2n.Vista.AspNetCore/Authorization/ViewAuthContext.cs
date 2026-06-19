using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace a2n.Vista.AspNetCore.Authorization;

/// <summary>
/// Immutable context passed to <see cref="IViewAuthorizer"/> for every view request. It carries the
/// caller identity together with the targeted view and facet, plus the HTTP and DI ambient state needed
/// to make server-trusted decisions.
/// Authoritative shape: docs/spec/01-view.md §5.6 (Decision Log D43/D48).
/// </summary>
/// <remarks>
/// <para>
/// Members mirror the authoritative spec exactly. <see cref="User"/>, <see cref="ViewName"/> and
/// <see cref="Facet"/> are the required triple (Requirement R7.4); <see cref="Http"/> and
/// <see cref="Services"/> let an authorizer read route values, headers, tenant claims, and resolve
/// scoped services when shaping the query.
/// </para>
/// <para>
/// This type is <c>HTTP-bound</c> (it carries <see cref="HttpContext"/>), which is precisely why it,
/// <see cref="IViewAuthorizer"/> and <see cref="ViewFacet"/> live in <c>a2n.Vista.AspNetCore</c> while
/// the neutral <c>IViewScope</c> stays in <c>a2n.Vista.Core</c> (Requirement R7.5, Decision Log D48).
/// </para>
/// <para>
/// A request cancellation token is intentionally <b>not</b> a separate parameter: it is reachable via
/// <see cref="HttpContext.RequestAborted"/> on <see cref="Http"/>, matching the authoritative contract.
/// </para>
/// <para>
/// Declared as a <c>sealed record</c> with positional, init-only members so the context is immutable
/// once constructed.
/// </para>
/// </remarks>
/// <param name="User">The authenticated (or anonymous) caller for the request.</param>
/// <param name="ViewName">The registered view name the request targets.</param>
/// <param name="Facet">The facet being accessed (read vs write granularity).</param>
/// <param name="Http">The ambient HTTP context for the request.</param>
/// <param name="Services">The request-scoped service provider, for resolving dependencies during a decision.</param>
public sealed record ViewAuthContext(
    ClaimsPrincipal User,
    string ViewName,
    ViewFacet Facet,
    HttpContext Http,
    IServiceProvider Services);
