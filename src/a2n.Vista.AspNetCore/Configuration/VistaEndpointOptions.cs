using a2n.Vista.AspNetCore.Authorization;

namespace a2n.Vista.AspNetCore.Configuration;

/// <summary>
/// The AspNetCore-side configuration snapshot for Vista's HTTP surface: whether a one-door
/// <see cref="IViewAuthorizer"/> was registered and whether anonymous access was explicitly opted into.
/// Built once at the composition root by <c>AddVistaEndpoints</c> (through
/// <see cref="IVistaEndpointBuilder"/>) and registered as a singleton so the request glue, the endpoint
/// mapper, and the startup posture check all read the same values.
/// Authoritative behavior: docs/spec/01-view.md §5.6, §13.2 (Decision Log D43/D94).
/// </summary>
/// <remarks>
/// <para>
/// This type is deliberately independent of the Entity Framework layer's <c>IVistaBuilder</c>: the
/// AspNetCore package must not reference <c>a2n.Vista.EntityFrameworkCore</c> (Requirement R11.3). The
/// two share only <c>a2n.Vista.Core</c>. The global route root is <b>not</b> owned here: a view's route
/// is composed at registration (the EF layer's <c>RouteGroup</c>/default root) and recorded in
/// <see cref="a2n.Vista.Metadata.ViewMetadata.Route"/>, which the mapper reads verbatim (D101/D103).
/// </para>
/// <para>
/// <b>Fail-safe posture (D94).</b> <see cref="HasAuthorizer"/> / <see cref="AuthorizerType"/> and
/// <see cref="AllowAnonymous"/> let the startup validator decide warn vs fail-closed without changing
/// this surface.
/// </para>
/// </remarks>
public sealed class VistaEndpointOptions
{
    /// <summary>
    /// The current wire-contract version (Decision Log D99). A reserved seam for future URL versioning
    /// (e.g. <c>/api/v{n}/views</c>); <b>no routing behavior depends on it in this release</b> —
    /// unversioned requests serve the latest (and only) version. Route groups (<c>RouteGroup</c>) are the
    /// intended versioning vehicle when versioning is implemented (backlog, Spec 11).
    /// </summary>
    public const string CurrentWireVersion = "v1";

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

    /// <summary>
    /// Whether anonymous (no-authorizer) access is an explicit, reviewed opt-in, set via
    /// <see cref="IVistaEndpointBuilder.AllowAnonymousAccess"/>. When no authorizer is registered, this
    /// flag distinguishes a deliberate open posture (<see langword="true"/>) from a forgotten
    /// configuration (<see langword="false"/>). In non-Development environments, serving views with no
    /// authorizer requires this to be <see langword="true"/>; otherwise startup fails fast (D94).
    /// </summary>
    public bool AllowAnonymous { get; internal set; }
}
