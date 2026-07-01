using System.Collections.Generic;
using a2n.Vista.Adapters;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Write;
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
/// <b>Write-path map.</b> Every typed write failure derives from <see cref="VistaWriteException"/> and
/// carries a <see cref="WriteErrorCode"/>; the status is derived from that code and the wire
/// <c>code</c> from <see cref="VistaWriteException.ErrorCode"/> — the same envelope/vocabulary the read
/// path uses. <c>write-validation-failed</c> additionally surfaces the offending <c>fields</c>. Write
/// exception messages are constructed by Vista and are leak-free by contract (no stack traces, exception
/// type names, SQL text, schema/object names, connection strings, masked field values, or non-projected
/// entity fields; Requirements R9.4, R9.5, R9.6, R10.5, R6.2, R6.3, R6.5, R15.1).
/// </para>
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

            case VistaInvalidRequestException { WriteErrorCode: { } writeErrorCode } invalidWrite:
                // A write-path invalid request carries a precise WriteErrorCode; surface it with the
                // same status/title/type/code the executor's write failures use (Decision Log D120).
                result = HttpResults.Problem(
                    statusCode: MapWriteStatus(writeErrorCode),
                    title: WriteTitle(writeErrorCode),
                    type: TypePrefix + writeErrorCode.ToWireCode(),
                    detail: invalidWrite.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = writeErrorCode.ToWireCode(),
                    });
                return true;

            case VistaInvalidRequestException invalid:
                result = HttpResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid request",
                    type: TypePrefix + "invalid-request",
                    detail: invalid.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "invalid-request",
                    });
                return true;

            case VistaWriteException write:
                result = CreateWriteProblem(write);
                return true;

            case AdapterBindException bind:
                result = HttpResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Adapter bind failed",
                    type: TypePrefix + "adapter-bind-failed",
                    detail: bind.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "adapter-bind-failed",
                    });
                return true;

            default:
                result = null;
                return false;
        }
    }

    /// <summary>
    /// Builds the RFC 7807 problem result for a typed write failure. The HTTP status and title are
    /// derived from the exception's <see cref="WriteErrorCode"/>; the wire <c>code</c> comes from
    /// <see cref="VistaWriteException.ErrorCode"/>. The <c>detail</c> is the exception's Vista-authored,
    /// leak-free message. <see cref="VistaValidationException"/> additionally reports the offending
    /// <c>fields</c>. No provider text, SQL, schema/object names, connection strings, stack traces, or
    /// exception type names are ever included (Requirements R9.5, R9.6, R10.5).
    /// </summary>
    private static IResult CreateWriteProblem(VistaWriteException write)
    {
        return HttpResults.Problem(
            statusCode: MapWriteStatus(write.Code),
            title: WriteTitle(write.Code),
            type: TypePrefix + write.ErrorCode,
            detail: write.Message,
            extensions: BuildWriteExtensions(write));
    }

    /// <summary>Maps a <see cref="WriteErrorCode"/> to its HTTP status code (design "Error Handling").</summary>
    private static int MapWriteStatus(WriteErrorCode code) => code switch
    {
        WriteErrorCode.MalformedBody => StatusCodes.Status400BadRequest,
        WriteErrorCode.MissingKey => StatusCodes.Status400BadRequest,
        WriteErrorCode.KeyTypeCoercion => StatusCodes.Status400BadRequest,
        WriteErrorCode.IncompleteKey => StatusCodes.Status400BadRequest,
        WriteErrorCode.ValidationFailed => StatusCodes.Status400BadRequest,
        WriteErrorCode.BulkNotEnabled => StatusCodes.Status400BadRequest,
        WriteErrorCode.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
        WriteErrorCode.ConcurrencyConflict => StatusCodes.Status409Conflict,
        WriteErrorCode.WriteConflict => StatusCodes.Status409Conflict,
        // NoWritePlan is rendered as an indistinguishable 404 by the endpoint (never via a coded body);
        // if one ever reaches here, fall back to 404 so no existence signal leaks.
        WriteErrorCode.NoWritePlan => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>Human-readable problem title for a <see cref="WriteErrorCode"/>.</summary>
    private static string WriteTitle(WriteErrorCode code) => code switch
    {
        WriteErrorCode.MalformedBody => "Malformed request body",
        WriteErrorCode.MissingKey => "Missing key",
        WriteErrorCode.KeyTypeCoercion => "Invalid key value",
        WriteErrorCode.IncompleteKey => "Incomplete key",
        WriteErrorCode.ValidationFailed => "Validation failed",
        WriteErrorCode.BulkNotEnabled => "Bulk writes not enabled",
        WriteErrorCode.PreconditionRequired => "Precondition required",
        WriteErrorCode.ConcurrencyConflict => "Concurrency conflict",
        WriteErrorCode.WriteConflict => "Write conflict",
        WriteErrorCode.NoWritePlan => "Not found",
        _ => "Write rejected",
    };

    /// <summary>
    /// Builds the extension members for a write failure: the wire <c>code</c> and, for a validation
    /// failure, the list of offending <c>fields</c>. Never emits internal detail.
    /// </summary>
    private static Dictionary<string, object?> BuildWriteExtensions(VistaWriteException write)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = write.ErrorCode,
        };

        if (write is VistaValidationException validation && validation.Fields.Count > 0)
        {
            extensions["fields"] = validation.Fields;
        }

        return extensions;
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
