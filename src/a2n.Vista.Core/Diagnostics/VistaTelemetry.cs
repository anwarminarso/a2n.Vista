using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace a2n.Vista.Diagnostics;

/// <summary>
/// Vendor-neutral view telemetry for Vista (Decision Log D100, D159). One <see cref="ActivitySource"/>
/// and one <see cref="Meter"/>, both named <see cref="ActivitySourceName"/>, implemented entirely with
/// BCL <c>System.Diagnostics</c> types. Core does not reference an OpenTelemetry package; an application
/// that wants to export subscribes to these names from its own composition.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zero cost when unused.</b> <see cref="Begin"/> returns an empty scope — no activity, no timestamp,
/// no tags, no <see cref="AsyncLocal{T}"/> — unless a listener is subscribed to the source or a meter
/// listener has enabled one of the instruments. That is the standard .NET behavior of
/// <see cref="ActivitySource.HasListeners"/> and <see cref="Instrument.Enabled"/>, and it stays
/// allocation-free on the request path.
/// </para>
/// <para>
/// <b>One signal per outermost operation.</b> A nested <see cref="Begin"/> (an export that runs a list,
/// or an HTTP facet that calls <c>IViewExecutor</c>) joins the ambient operation. It does not start a
/// second activity or emit a second metric. The outer operation name wins, so an export is recorded as
/// <c>export</c> rather than as the inner <c>list</c>.
/// </para>
/// <para>
/// <b>What is never recorded.</b> SQL text, connection strings, exception messages, row values, filter
/// values, and the raw route string of an unknown view. The view tag is the canonical registered name,
/// or <see cref="UnregisteredViewName"/> when the name was not resolved. Error codes are copied from
/// <see cref="IVistaErrorCode"/> only when they match a safe token; anything else collapses to
/// <see cref="Outcomes.Error"/>.
/// </para>
/// <para>
/// Authoritative contract: <c>docs/spec/10-operations-and-observability.md</c>. Wiring:
/// <c>docs/observability.md</c>.
/// </para>
/// </remarks>
public static class VistaTelemetry
{
    /// <summary>Maximum length of a view name copied into a tag. Longer names collapse to <see cref="UnregisteredViewName"/>.</summary>
    private const int MaxViewNameLength = 128;

    /// <summary>The <see cref="ActivitySource"/> name. Pass this to <c>TracerProviderBuilder.AddSource</c>.</summary>
    public const string ActivitySourceName = "a2n.Vista";

    /// <summary>The <see cref="Meter"/> name. Pass this to <c>MeterProviderBuilder.AddMeter</c>.</summary>
    public const string MeterName = "a2n.Vista";

    /// <summary>
    /// View-tag value used when the operation did not resolve a registered view. The caller-supplied
    /// string is never copied, so probing random route names cannot grow the tag cardinality.
    /// </summary>
    public const string UnregisteredViewName = "unregistered";

    /// <summary>The activity source that emits one span per outermost view operation.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>The meter that owns <see cref="OperationCount"/>, <see cref="OperationDuration"/>, and <see cref="OperationRows"/>.</summary>
    public static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Count of view operations. Tags: <see cref="MetricTags.View"/>, <see cref="MetricTags.Operation"/>,
    /// <see cref="MetricTags.Outcome"/>.
    /// </summary>
    public static readonly Counter<long> OperationCount = Meter.CreateCounter<long>(
        MetricNames.OperationCount,
        unit: "{operation}",
        description: "Number of view operations, split by view, operation, and outcome.");

    /// <summary>
    /// Duration of a view operation in milliseconds. Tags match <see cref="OperationCount"/>.
    /// </summary>
    public static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        MetricNames.OperationDuration,
        unit: "ms",
        description: "Duration of a view operation in milliseconds, split by view, operation, and outcome.");

    /// <summary>
    /// Rows returned by a view operation, when that count is known. Tags: <see cref="MetricTags.View"/>
    /// and <see cref="MetricTags.Operation"/> only (outcome is intentionally omitted so this series stays smaller).
    /// </summary>
    public static readonly Histogram<long> OperationRows = Meter.CreateHistogram<long>(
        MetricNames.OperationRows,
        unit: "{row}",
        description: "Rows returned by a view operation. Tagged by view and operation only.");

    /// <summary>Ambient outermost operation. Flowed across awaits; unset when telemetry is disabled.</summary>
    internal static readonly AsyncLocal<VistaOperationState?> Ambient = new();

    /// <summary>
    /// <see langword="true"/> when an <see cref="ActivityListener"/> is subscribed to
    /// <see cref="ActivitySource"/> or a <see cref="MeterListener"/> has enabled any Vista instrument.
    /// </summary>
    public static bool IsEnabled =>
        ActivitySource.HasListeners()
        || OperationCount.Enabled
        || OperationDuration.Enabled
        || OperationRows.Enabled;

    /// <summary>
    /// Starts an outermost view operation, or joins the ambient one when already inside Vista telemetry.
    /// Returns an empty scope when <see cref="IsEnabled"/> is <see langword="false"/> and nothing is ambient.
    /// </summary>
    /// <param name="viewName">
    /// The canonical registered view name, or <see cref="UnregisteredViewName"/> until
    /// <see cref="NoteView"/> replaces it. Do not pass an unresolved route parameter: an unknown name is
    /// collapsed, but a resolved name is recorded as given.
    /// </param>
    /// <param name="operation">
    /// One of <see cref="Operations"/>. Any other value is kept only when it is a safe token; otherwise
    /// it is recorded as <see cref="Operations.Unknown"/>.
    /// </param>
    /// <returns>A scope whose <see cref="VistaOperationScope.Dispose"/> records the operation once.</returns>
    /// <exception cref="ArgumentException"><paramref name="viewName"/> or <paramref name="operation"/> is null or whitespace.</exception>
    public static VistaOperationScope Begin(string viewName, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        // Disabled and not already inside an operation: do not touch AsyncLocal, do not allocate.
        if (!IsEnabled)
        {
            return default;
        }

        var ambient = Ambient.Value;
        if (ambient is not null)
        {
            return new VistaOperationScope(ambient, owns: false);
        }

        var state = new VistaOperationState(CanonicalView(viewName), CanonicalOperation(operation));
        Ambient.Value = state;
        return new VistaOperationScope(state, owns: true);
    }

    /// <summary>
    /// Sets the canonical registered view name on the ambient operation, if one is in progress.
    /// No-op when telemetry is disabled or no operation is ambient. Call this only with a name returned
    /// by the view registry — never with the raw request string of a miss.
    /// </summary>
    /// <param name="registeredViewName">The registered view name.</param>
    public static void NoteView(string registeredViewName)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(registeredViewName))
        {
            return;
        }

        Ambient.Value?.SetView(CanonicalView(registeredViewName));
    }

    /// <summary>
    /// Maps a failure to the metric/activity outcome string. Cancellation is
    /// <see cref="Outcomes.Cancelled"/>. A safe <see cref="IVistaErrorCode.ErrorCode"/> is returned
    /// verbatim (this is how <c>write-conflict</c>, <c>write-constraint-violation</c>, and
    /// <c>write-persistence-failed</c> reach the tags). An <see cref="ArgumentOutOfRangeException"/>
    /// is <see cref="ErrorCodes.InvalidPageSize"/>, matching the HTTP mapper. Everything else is
    /// <see cref="Outcomes.Error"/>. The exception message is never read.
    /// </summary>
    /// <param name="exception">The failure that escaped the operation.</param>
    /// <returns>A bounded outcome token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static string Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return Outcomes.Cancelled;
        }

        if (exception is IVistaErrorCode coded && IsSafeToken(coded.ErrorCode))
        {
            return coded.ErrorCode;
        }

        if (exception is ArgumentOutOfRangeException)
        {
            return ErrorCodes.InvalidPageSize;
        }

        return Outcomes.Error;
    }

    /// <summary>
    /// A span name from a fixed set. The operation tag still carries the token; the span name itself
    /// never interpolates a caller string.
    /// </summary>
    internal static string ActivityNameFor(string operation) => operation switch
    {
        Operations.List => "vista.view.list",
        Operations.Detail => "vista.view.detail",
        Operations.Export => "vista.view.export",
        Operations.Create => "vista.view.create",
        Operations.Update => "vista.view.update",
        Operations.Delete => "vista.view.delete",
        _ => "vista.view",
    };

    internal static string CanonicalView(string viewName) =>
        string.IsNullOrWhiteSpace(viewName) || viewName.Length > MaxViewNameLength
            ? UnregisteredViewName
            : viewName;

    internal static string CanonicalOperation(string operation) =>
        IsSafeToken(operation) ? operation : Operations.Unknown;

    /// <summary>
    /// A telemetry token: 1–64 characters, lowercase letters, digits, and single interior hyphens.
    /// </summary>
    internal static bool IsSafeToken(string? value)
    {
        if (value is not { Length: > 0 and <= 64 })
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                continue;
            }

            if (c != '-' || i == 0 || i == value.Length - 1 || value[i - 1] == '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>View operation names. These strings are the <c>vista.operation</c> tag and the metric <c>operation</c> tag.</summary>
    public static class Operations
    {
        /// <summary>List / query.</summary>
        public const string List = "list";

        /// <summary>Detail by key.</summary>
        public const string Detail = "detail";

        /// <summary>Export (file or JSON). Distinct from <see cref="List"/> even though it runs a bounded list.</summary>
        public const string Export = "export";

        /// <summary>Create.</summary>
        public const string Create = "create";

        /// <summary>Update.</summary>
        public const string Update = "update";

        /// <summary>Delete.</summary>
        public const string Delete = "delete";

        /// <summary>Fallback when a caller passes an unsafe operation string.</summary>
        public const string Unknown = "unknown";
    }

    /// <summary>Coarse activity outcomes. Metric outcomes use these for success paths and the wire code for failures.</summary>
    public static class Outcomes
    {
        /// <summary>The operation completed and found its target (a page, a row, or a write).</summary>
        public const string Ok = "ok";

        /// <summary>Detail, update, or delete completed and matched no row. Not an error.</summary>
        public const string NotFound = "not-found";

        /// <summary>The operation was cancelled (<see cref="OperationCanceledException"/>).</summary>
        public const string Cancelled = "cancelled";

        /// <summary>A failure that has no safe wire code.</summary>
        public const string Error = "error";
    }

    /// <summary>
    /// Stable error-code tokens telemetry knows by name. Filter and write exceptions already expose
    /// their own wire codes; the values here are the ones the operational contract calls out, including
    /// the persistence split (<see cref="WriteConstraintViolation"/>, <see cref="WritePersistenceFailed"/>)
    /// which is recorded verbatim when an <see cref="IVistaErrorCode"/> carries it.
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>
        /// Constraint-shaped save failure on the current branch (<c>VistaWriteConflictException</c>).
        /// Recorded because that exception reports it. The unmerged write-error split retires this token;
        /// telemetry copies whatever safe code the exception carries, so no change is required here when it does.
        /// </summary>
        public const string WriteConflict = "write-conflict";

        /// <summary>Optimistic concurrency failure. The code <c>VistaConcurrencyConflictException</c> reports today.</summary>
        public const string WriteConcurrencyConflict = "write-concurrency-conflict";

        /// <summary>Database constraint violation (unique, foreign key, NOT NULL, CHECK). Recorded when the exception reports it.</summary>
        public const string WriteConstraintViolation = "write-constraint-violation";

        /// <summary>Persistence failed for a reason other than concurrency or a constraint. Recorded when the exception reports it.</summary>
        public const string WritePersistenceFailed = "write-persistence-failed";

        /// <summary>The authorizer denied the facet. Matches the problem-details <c>code</c>.</summary>
        public const string Forbidden = "forbidden";

        /// <summary>No view is registered under the requested name. The requested string is not a tag.</summary>
        public const string ViewNotFound = "view-not-found";

        /// <summary>The request body or key was rejected and no finer write code applies.</summary>
        public const string InvalidRequest = "invalid-request";

        /// <summary>Page size was rejected (<c>length=-1</c> and any other non-positive size).</summary>
        public const string InvalidPageSize = "invalid-page-size";

        /// <summary>A grid adapter could not bind its native request.</summary>
        public const string AdapterBindFailed = "adapter-bind-failed";
    }

    /// <summary>Activity tag names. Low cardinality; treated as an operational contract (Spec 11).</summary>
    public static class Tags
    {
        /// <summary>Canonical registered view name, or <see cref="UnregisteredViewName"/>.</summary>
        public const string ViewName = "vista.view.name";

        /// <summary>Operation: <see cref="Operations"/>.</summary>
        public const string Operation = "vista.operation";

        /// <summary>Coarse outcome: <see cref="Outcomes"/>.</summary>
        public const string Outcome = "vista.outcome";

        /// <summary>Wire code when <see cref="Outcome"/> is <see cref="Outcomes.Error"/>. Absent otherwise.</summary>
        public const string ErrorCode = "vista.error.code";

        /// <summary>Rows materialized for the caller (page size, 0/1 for detail and writes).</summary>
        public const string RowsReturned = "vista.rows.returned";

        /// <summary>Rows matching the client filter (<c>recordsFiltered</c>). List and export only.</summary>
        public const string RowsFiltered = "vista.rows.filtered";

        /// <summary>Rows in scope before the client filter (<c>recordsTotal</c>). List and export only.</summary>
        public const string RowsUnfiltered = "vista.rows.unfiltered";
    }

    /// <summary>Metric instrument names.</summary>
    public static class MetricNames
    {
        /// <summary>Counter. Tags: view, operation, outcome.</summary>
        public const string OperationCount = "vista.operation.count";

        /// <summary>Histogram in milliseconds. Tags: view, operation, outcome.</summary>
        public const string OperationDuration = "vista.operation.duration";

        /// <summary>Histogram of rows returned. Tags: view, operation.</summary>
        public const string OperationRows = "vista.operation.rows";
    }

    /// <summary>Metric tag names. Shorter than the activity attributes; same vocabulary.</summary>
    public static class MetricTags
    {
        /// <summary>Canonical view name.</summary>
        public const string View = "view";

        /// <summary>Operation name.</summary>
        public const string Operation = "operation";

        /// <summary>
        /// <see cref="Outcomes.Ok"/>, <see cref="Outcomes.NotFound"/>, <see cref="Outcomes.Cancelled"/>,
        /// <see cref="Outcomes.Error"/>, or a safe wire code such as <see cref="ErrorCodes.WriteConflict"/>.
        /// </summary>
        public const string Outcome = "outcome";
    }
}
