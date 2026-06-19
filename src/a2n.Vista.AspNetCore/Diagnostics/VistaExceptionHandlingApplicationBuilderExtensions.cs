using a2n.Vista.AspNetCore.Diagnostics;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Application-pipeline wiring for Vista's RFC 7807 error mapping (Task 10.4). Lives in the
/// <c>Microsoft.AspNetCore.Builder</c> namespace by convention (like <c>UseExceptionHandler</c> /
/// <c>UseRouting</c>) so <c>app.UseVistaExceptionHandling()</c> surfaces on
/// <see cref="IApplicationBuilder"/> / <see cref="WebApplication"/> without an extra <c>using</c>.
/// </summary>
public static class VistaExceptionHandlingApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the self-contained <see cref="VistaExceptionHandlingMiddleware"/> to the pipeline so Vista's
    /// typed failures are translated to RFC 7807 problem responses (404/403/400). This is the
    /// single-call way to enable Vista error mapping; it does not require
    /// <see cref="ExceptionHandlerExtensions.UseExceptionHandler(IApplicationBuilder)"/>.
    /// </summary>
    /// <param name="app">The application builder (typically the <see cref="WebApplication"/>).</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Place this <b>before</b> <c>app.MapVistaViews()</c> (and as early in the pipeline as practical) so
    /// it wraps the Vista endpoints. A typical bootstrap reads:
    /// <code>
    /// app.UseVistaExceptionHandling();
    /// app.MapVistaViews();
    /// </code>
    /// Applications that prefer the framework's pipeline can instead call
    /// <c>app.UseExceptionHandler()</c>; <c>AddVistaEndpoints</c> registers a
    /// <see cref="a2n.Vista.AspNetCore.Diagnostics.VistaExceptionHandler"/> that the framework pipeline
    /// will invoke. Use one mechanism or the other.
    /// </remarks>
    public static IApplicationBuilder UseVistaExceptionHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<VistaExceptionHandlingMiddleware>();
    }
}
