using a2n.Vista.AspNetCore.Authorization;

namespace a2n.Vista.AspNetCore.Configuration;

/// <summary>
/// The AspNetCore-side configuration snapshot for Vista's HTTP surface: the global route root and
/// whether a one-door <see cref="IViewAuthorizer"/> was registered. Built once at the composition root
/// by <c>AddVistaEndpoints</c> (through <see cref="IVistaEndpointBuilder"/>) and registered as a
/// singleton so the request glue (Task 10.2), the endpoint mapper (Task 10.3), and the startup
/// fail-open warning (Task 10.4) all read the same values.
/// Authoritative behavior: docs/spec/01-view.md §5.6 (Decision Log D43/D44).
/// </summary>
/// <remarks>
/// <para>
/// This type is deliberately independent of the Entity Framework layer's <c>IVistaBuilder</c>: the
/// AspNetCore package must not reference <c>a2n.Vista.EntityFrameworkCore</c> (Requirement R11.3). The
/// two share only <c>a2n.Vista.Core</c>. Both expose a <c>RouteRoot(...)</c>; keep them in sync when an
/// application configures both — the EF-side root is captured into
/// <see cref="a2n.Vista.Metadata.ViewMetadata.Route"/> while this one drives the live endpoints.
/// </para>
/// <para>
/// <b>Fail-open seam (Task 10.4).</b> <see cref="HasAuthorizer"/> / <see cref="AuthorizerType"/> let the
/// startup warning ("no IViewAuthorizer registered — all views are publicly accessible", R7.3) be added
/// without changing this surface: 10.4 only needs to read these flags.
/// </para>
/// </remarks>
public sealed class VistaEndpointOptions
{
    /// <summary>The default global route root applied when a caller does not supply one (§5.6, D44).</summary>
    public const string DefaultRouteRoot = "/api/views";

    /// <summary>
    /// The global route root prefixed to each view name (<c>{root}/{viewName}</c>, §5.6). Defaults to
    /// <see cref="DefaultRouteRoot"/> (<c>/api/views</c>). Set via <see cref="IVistaEndpointBuilder.RouteRoot"/>.
    /// </summary>
    public string RouteRoot { get; internal set; } = DefaultRouteRoot;

    /// <summary>
    /// The registered authorizer implementation type, or <see langword="null"/> when none was registered
    /// (default-allow, R7.2). Recorded by <see cref="IVistaEndpointBuilder.UseAuthorizer{T}"/> so the
    /// startup fail-open warning (R7.3, Task 10.4) can detect the missing-authorizer case.
    /// </summary>
    public Type? AuthorizerType { get; internal set; }

    /// <summary>
    /// <see langword="true"/> when a one-door <see cref="IViewAuthorizer"/> was registered. When
    /// <see langword="false"/>, access defaults to allow (R7.2) and Task 10.4 emits a startup warning (R7.3).
    /// </summary>
    public bool HasAuthorizer => AuthorizerType is not null;
}
