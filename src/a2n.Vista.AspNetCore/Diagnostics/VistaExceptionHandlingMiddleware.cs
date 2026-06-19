using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace a2n.Vista.AspNetCore.Diagnostics;

/// <summary>
/// A self-contained middleware that catches Vista's typed request failures and writes RFC 7807 problem
/// responses (Task 10.4). Unlike <see cref="VistaExceptionHandler"/>, this does not depend on the
/// framework's <c>UseExceptionHandler</c> pipeline — adding <c>app.UseVistaExceptionHandling()</c> is
/// enough to map Vista errors end-to-end, which keeps the Northwind example (Task 11) to a single call.
/// Authoritative behavior: docs/spec/01-view.md §14; design.md "Error Handling".
/// </summary>
/// <remarks>
/// <para>
/// The exception → problem mapping is shared with <see cref="VistaExceptionHandler"/> via
/// <see cref="VistaProblemResults"/>, so both paths emit identical responses.
/// </para>
/// <para>
/// <b>Response-started guard.</b> If the response has already begun streaming when the exception is
/// thrown, the middleware cannot rewrite headers/status, so it rethrows to let the host abort the
/// connection rather than corrupting a partial response.
/// </para>
/// <para>
/// <b>Pass-through.</b> Non-Vista exceptions are rethrown unchanged so the application's own error
/// handling (or the default 500) applies.
/// </para>
/// </remarks>
public sealed class VistaExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<VistaExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Initializes a new <see cref="VistaExceptionHandlingMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger used to record unmapped failures before rethrowing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public VistaExceptionHandlingMiddleware(RequestDelegate next, ILogger<VistaExceptionHandlingMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the next middleware and maps any escaping Vista failure to an RFC 7807 problem response.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception exception) when (VistaProblemResults.TryCreate(exception, out _))
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(
                    exception,
                    "A Vista request failed after the response had already started; cannot write a problem response.");
                throw;
            }

            // Re-map to obtain the result (the filter above only confirmed it is mappable).
            VistaProblemResults.TryCreate(exception, out var result);
            await result!.ExecuteAsync(context).ConfigureAwait(false);
        }
    }
}
