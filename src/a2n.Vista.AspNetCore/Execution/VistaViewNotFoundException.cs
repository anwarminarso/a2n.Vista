namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// Signals that a request targeted a view name that is not registered. The request glue
/// (<see cref="ViewRequestExecutor"/>) throws this instead of returning a sentinel so the endpoint
/// layer (Task 10.4) can map it to an RFC 7807 HTTP 404 without inspecting nulls in the hot path.
/// Authoritative behavior: docs/spec/01-view.md §5.3 (no auto-expose), Requirement R1.1 (Decision Log D2).
/// </summary>
/// <remarks>
/// <para>
/// This is the AspNetCore-side counterpart to <c>IViewRegistry.Get</c> returning <see langword="null"/>:
/// resolution stays a simple null check in Core, while the glue converts a miss into this typed signal
/// so HTTP mapping is uniform with the other request failures (forbidden, invalid filter).
/// </para>
/// <para>
/// <b>Error-mapping contract for Task 10.4.</b> This type maps to <c>404 Not Found</c>. See
/// <see cref="VistaForbiddenException"/> (403), <c>a2n.Vista.Filter.FilterValidationException</c> (400),
/// and <see cref="System.ArgumentOutOfRangeException"/> from page-size clamping (400) for the rest of
/// the contract the error-mapping middleware consumes.
/// </para>
/// </remarks>
public sealed class VistaViewNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="VistaViewNotFoundException"/>.
    /// </summary>
    /// <param name="viewName">The unregistered view name the request targeted.</param>
    public VistaViewNotFoundException(string viewName)
        : base($"No view is registered under the name '{viewName}'.")
    {
        ViewName = viewName;
    }

    /// <summary>The unregistered view name the request targeted.</summary>
    public string ViewName { get; }
}
