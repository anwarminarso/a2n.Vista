using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace a2n.Vista.AspNetCore.Diagnostics;

/// <summary>
/// An <see cref="IExceptionHandler"/> that translates Vista's typed request failures into RFC 7807
/// problem responses (Task 10.4). Registered by <c>AddVistaEndpoints</c> so applications that already
/// use the framework's exception-handling pipeline (<c>app.UseExceptionHandler()</c>) get Vista's error
/// mapping for free, without adding the <c>UseVistaExceptionHandling</c> middleware.
/// Authoritative behavior: docs/spec/01-view.md §14; design.md "Error Handling".
/// </summary>
/// <remarks>
/// <para>
/// The actual exception → problem mapping lives in <see cref="VistaProblemResults"/>, shared with the
/// self-contained <c>UseVistaExceptionHandling</c> middleware so both paths produce identical responses.
/// </para>
/// <para>
/// <b>Activation.</b> An <see cref="IExceptionHandler"/> is only invoked when the application has added
/// the exception-handling middleware (<c>app.UseExceptionHandler()</c>). Applications that prefer a
/// single, self-contained call can instead use <c>app.UseVistaExceptionHandling()</c>, which does not
/// depend on the framework pipeline. Registering this handler is harmless when neither is wired up — it
/// simply stays dormant.
/// </para>
/// <para>
/// <b>Cooperative chaining.</b> When the escaped exception is not a recognized Vista failure,
/// <see cref="TryHandleAsync"/> returns <see langword="false"/> so any other registered
/// <see cref="IExceptionHandler"/> — and ultimately the default 500 path — can handle it.
/// </para>
/// </remarks>
public sealed class VistaExceptionHandler : IExceptionHandler
{
    /// <summary>
    /// Maps <paramref name="exception"/> to an RFC 7807 problem response when it is a recognized Vista
    /// failure (404/403/400); otherwise defers to the rest of the pipeline.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="exception">The exception that escaped the endpoint.</param>
    /// <param name="cancellationToken">A token tied to the request lifetime.</param>
    /// <returns>
    /// <see langword="true"/> when the exception was mapped and the response written; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (!VistaProblemResults.TryCreate(exception, out var result) || result is null)
        {
            return false;
        }

        await result.ExecuteAsync(httpContext).ConfigureAwait(false);
        return true;
    }
}
