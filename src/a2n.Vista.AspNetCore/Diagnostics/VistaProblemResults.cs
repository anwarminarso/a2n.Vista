using System.Collections.Generic;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Filter;
using Microsoft.AspNetCore.Http;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace a2n.Vista.AspNetCore.Diagnostics;

/// <summary>
/// The single source of truth that maps Vista's typed request failures to RFC 7807 problem responses
/// (Task 10.4). Both the opt-in <see cref="VistaExceptionHandler"/> (the
/// <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> path) and the self-contained
/// <c>UseVistaExceptionHandling</c> middleware delegate here so the status-code/title/code mapping is
/// defined exactly once.
/// Authoritative behavior: docs/spec/01-view.md §14 (RFC 7807 error model), §5.6/§8.3 (the failures);
/// design.md "Error Handling"; Requirements R5.5, R5.6, R6.2, R7.1, R1.1, R10.3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status-code map.</b>
/// </para>
/// <list type="bullet">
///   <item><description><see cref="VistaViewNotFoundException"/> → <c>404 Not Found</c> (R1.1).</description></item>
///   <item><description><see cref="VistaForbiddenException"/> → <c>403 Forbidden</c> (R7.1).</description></item>
///   <item><description><see cref="FilterValidationException"/> → <c>400 Bad Request</c> (R5.5/R5.6/R6.2/R9.2).</description></item>
///   <item><description><see cref="System.ArgumentOutOfRangeException"/> → <c>400 Bad Request</c> (page-size clamping, R10.3).</description></item>
/// </list>
/// <para>
/// <b>Page-size caveat.</b> The EF executor rejects an out-of-range page size (notably the
/// DataTables <c>length=-1</c> "all rows" request) by throwing <see cref="System.ArgumentOutOfRangeException"/>.
/// Pilar 1 maps <em>every</em> <see cref="System.ArgumentOutOfRangeException"/> that reaches the HTTP
/// boundary to a 400 "Invalid page size"; this is deliberate and documented — Pilar 1 has no other path
/// that surfaces this exception type to the client. A finer-grained signal (a dedicated exception) is a
/// later-milestone concern and would slot in here without changing the wire contract.
/// </para>
/// <para>
/// a stable <c>type</c> URN, and a machine-readable <c>code</c> extension member. Filter failures also
/// surface the offending <c>field</c>/<c>operator</c> as extension members for clients.
/// </para>
/// </remarks>
internal static class VistaProblemResults
{
    /// <summary>The stable problem-detail <c>type</c> URN prefix for Vista errors.</summary>
    private const string TypePrefix = "urn:a2n.vista:error:";

    /// <summary>
    /// Attempts to map <paramref name="exception"/> to an RFC 7807 <see cref="IResult"/>. Returns
    /// <see langword="true"/> and sets <paramref name="result"/> for the recognized Vista failures;
    /// returns <see langword="false"/> (and a <see langword="null"/> result) for anything else, so the
    /// caller can defer to the default pipeline (typically a 500).
    /// </summary>
    /// <param name="exception">The exception that escaped a Vista endpoint.</param>
    /// <param name="result">The mapped problem result when the return value is <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="exception"/> is a recognized Vista failure.</returns>
    public static bool TryCreate(Exception exception, out IResult? result)
    {
        switch (exception)
        {
            case VistaViewNotFoundException notFound:
                result = HttpResults.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "View not found",
                    type: TypePrefix + "view-not-found",
                    detail: $"No view is registered under the name '{notFound.ViewName}'.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "view-not-found",
                        ["view"] = notFound.ViewName,
                    });
                return true;

            case VistaForbiddenException forbidden:
                result = HttpResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    type: TypePrefix + "forbidden",
                    detail: $"Access to facet '{forbidden.Facet}' of view '{forbidden.ViewName}' was denied.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "forbidden",
                        ["view"] = forbidden.ViewName,
                        ["facet"] = forbidden.Facet.ToString(),
                    });
                return true;

            case FilterValidationException filter:
                result = HttpResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid filter",
                    type: TypePrefix + filter.ErrorCode,
                    detail: filter.Message,
                    extensions: BuildFilterExtensions(filter));
                return true;

            case ArgumentOutOfRangeException pageSize:
                result = HttpResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid page size",
                    type: TypePrefix + "invalid-page-size",
                    detail: pageSize.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "invalid-page-size",
                    });
                return true;

            default:
                result = null;
                return false;
        }
    }

    /// <summary>
    /// Builds the extension members for a filter validation failure: the wire <c>code</c> and the
    /// offending <c>field</c>/<c>operator</c> when present.
    /// </summary>
    private static Dictionary<string, object?> BuildFilterExtensions(FilterValidationException filter)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = filter.ErrorCode,
        };

        if (filter.Field is not null)
        {
            extensions["field"] = filter.Field;
        }

        if (filter.Operator is not null)
        {
            extensions["operator"] = filter.Operator.Value.ToString();
        }

        return extensions;
    }
}
