using a2n.Vista.AspNetCore.Authorization;

namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// Signals that the registered <see cref="IViewAuthorizer"/> denied access to a (view, facet, user)
/// tuple. The request glue (<see cref="ViewRequestExecutor"/>) throws this when
/// <see cref="IViewAuthorizer.IsAllowedAsync"/> returns <see langword="false"/>, so the endpoint layer
/// (Task 10.4) can map it to an RFC 7807 HTTP 403.
/// Authoritative behavior: docs/spec/01-view.md §5.6 (one-door auth), Requirement R7.1 (Decision Log D43).
/// </summary>
/// <remarks>
/// <b>Error-mapping contract for Task 10.4.</b> This type maps to <c>403 Forbidden</c>. See
/// <see cref="VistaViewNotFoundException"/> (404), <c>a2n.Vista.Filter.FilterValidationException</c>
/// (400), and <see cref="System.ArgumentOutOfRangeException"/> from page-size clamping (400) for the
/// rest of the contract the error-mapping middleware consumes.
/// </remarks>
public sealed class VistaForbiddenException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="VistaForbiddenException"/>.
    /// </summary>
    /// <param name="viewName">The view name whose access was denied.</param>
    /// <param name="facet">The facet whose access was denied.</param>
    public VistaForbiddenException(string viewName, ViewFacet facet)
        : base($"Access to facet '{facet}' of view '{viewName}' was denied by the authorizer.")
    {
        ViewName = viewName;
        Facet = facet;
    }

    /// <summary>The view name whose access was denied.</summary>
    public string ViewName { get; }

    /// <summary>The facet whose access was denied.</summary>
    public ViewFacet Facet { get; }
}
