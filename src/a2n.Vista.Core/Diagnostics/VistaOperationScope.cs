using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace a2n.Vista.Diagnostics;

/// <summary>
/// A view operation started by <see cref="VistaTelemetry.Begin"/>. Dispose records one metric sample
/// and stops the activity. A default scope (telemetry disabled) does nothing and allocates nothing.
/// Nested scopes share the outermost state and do not record a second sample.
/// </summary>
public readonly struct VistaOperationScope : IDisposable
{
    private readonly VistaOperationState? _state;
    private readonly bool _owns;

    internal VistaOperationScope(VistaOperationState state, bool owns)
    {
        _state = state;
        _owns = owns;
    }

    /// <summary>
    /// Records a successful completion. Later calls do not override a not-found or failure outcome.
    /// Null row arguments leave a value already contributed by an inner operation in place.
    /// </summary>
    /// <param name="rowsReturned">Rows materialized for the caller, when known.</param>
    /// <param name="rowsFiltered">Rows matching the client filter, when known.</param>
    /// <param name="rowsUnfiltered">Rows in scope before the client filter, when known.</param>
    public void Succeed(long? rowsReturned = null, long? rowsFiltered = null, long? rowsUnfiltered = null) =>
        _state?.Succeed(rowsReturned, rowsFiltered, rowsUnfiltered);

    /// <summary>
    /// Records that detail, update, or delete matched no row. This is not a failure. A prior failure
    /// is left unchanged. Rows returned default to 0 when no count was contributed yet.
    /// </summary>
    public void NotFound() => _state?.NotFound();

    /// <summary>
    /// Records a failure. The first failure wins. The exception message is not copied onto the activity.
    /// </summary>
    /// <param name="exception">The failure that escaped the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public void Fail(Exception exception)
    {
        if (_state is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(exception);
        _state.Fail(exception);
    }

    /// <summary>
    /// Stops the activity and records metrics when this scope owns the operation. Nested scopes no-op.
    /// Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_owns)
        {
            _state?.Complete();
        }
    }
}

/// <summary>Mutable state for one outermost view operation. Not part of the public contract.</summary>
internal sealed class VistaOperationState
{
    private readonly long _startTimestamp;
    private readonly Activity? _activity;
    private int _completed;
    private bool _failed;
    private string _viewName;
    private string? _metricOutcome;
    private string? _activityOutcome;
    private string? _errorCode;
    private long? _rowsReturned;
    private long? _rowsFiltered;
    private long? _rowsUnfiltered;

    public VistaOperationState(string viewName, string operation)
    {
        _viewName = viewName;
        Operation = operation;
        _startTimestamp = Stopwatch.GetTimestamp();

        if (!VistaTelemetry.ActivitySource.HasListeners())
        {
            return;
        }

        _activity = VistaTelemetry.ActivitySource.StartActivity(
            VistaTelemetry.ActivityNameFor(operation),
            ActivityKind.Internal);
        if (_activity is null)
        {
            return;
        }

        _activity.SetTag(VistaTelemetry.Tags.ViewName, viewName);
        _activity.SetTag(VistaTelemetry.Tags.Operation, operation);
    }

    public string Operation { get; }

    public void SetView(string viewName)
    {
        _viewName = viewName;
        _activity?.SetTag(VistaTelemetry.Tags.ViewName, viewName);
    }

    public void Succeed(long? rowsReturned, long? rowsFiltered, long? rowsUnfiltered)
    {
        AssignRows(rowsReturned, rowsFiltered, rowsUnfiltered);
        if (_failed || _metricOutcome == VistaTelemetry.Outcomes.NotFound)
        {
            return;
        }

        _metricOutcome = VistaTelemetry.Outcomes.Ok;
        _activityOutcome = VistaTelemetry.Outcomes.Ok;
    }

    public void NotFound()
    {
        if (_failed)
        {
            return;
        }

        _metricOutcome = VistaTelemetry.Outcomes.NotFound;
        _activityOutcome = VistaTelemetry.Outcomes.NotFound;
        _rowsReturned ??= 0;
    }

    public void Fail(Exception exception)
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        var classified = VistaTelemetry.Classify(exception);
        if (classified == VistaTelemetry.Outcomes.Cancelled)
        {
            _metricOutcome = VistaTelemetry.Outcomes.Cancelled;
            _activityOutcome = VistaTelemetry.Outcomes.Cancelled;
            _errorCode = null;
            return;
        }

        if (classified == VistaTelemetry.Outcomes.Error)
        {
            _metricOutcome = VistaTelemetry.Outcomes.Error;
            _activityOutcome = VistaTelemetry.Outcomes.Error;
            _errorCode = null;
            return;
        }

        _metricOutcome = classified;
        _activityOutcome = VistaTelemetry.Outcomes.Error;
        _errorCode = classified;
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        if (ReferenceEquals(VistaTelemetry.Ambient.Value, this))
        {
            VistaTelemetry.Ambient.Value = null;
        }

        var metricOutcome = _metricOutcome ?? VistaTelemetry.Outcomes.Ok;
        var activityOutcome = _activityOutcome ?? VistaTelemetry.Outcomes.Ok;

        try
        {
            WriteActivity(activityOutcome);
            WriteMetrics(metricOutcome);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    private void AssignRows(long? rowsReturned, long? rowsFiltered, long? rowsUnfiltered)
    {
        if (rowsReturned is long returned)
        {
            _rowsReturned = returned;
        }

        if (rowsFiltered is long filtered)
        {
            _rowsFiltered = filtered;
        }

        if (rowsUnfiltered is long unfiltered)
        {
            _rowsUnfiltered = unfiltered;
        }
    }

    private void WriteActivity(string activityOutcome)
    {
        if (_activity is null)
        {
            return;
        }

        _activity.SetTag(VistaTelemetry.Tags.ViewName, _viewName);
        _activity.SetTag(VistaTelemetry.Tags.Operation, Operation);
        _activity.SetTag(VistaTelemetry.Tags.Outcome, activityOutcome);
        if (_errorCode is not null)
        {
            _activity.SetTag(VistaTelemetry.Tags.ErrorCode, _errorCode);
        }

        if (_rowsReturned is long rows)
        {
            _activity.SetTag(VistaTelemetry.Tags.RowsReturned, rows);
        }

        if (_rowsFiltered is long filtered)
        {
            _activity.SetTag(VistaTelemetry.Tags.RowsFiltered, filtered);
        }

        if (_rowsUnfiltered is long unfiltered)
        {
            _activity.SetTag(VistaTelemetry.Tags.RowsUnfiltered, unfiltered);
        }

        // Status on the Vista span only. The parent HTTP span's status stays with ASP.NET Core,
        // which already derives it from the response status code.
        if (activityOutcome == VistaTelemetry.Outcomes.Error)
        {
            _activity.SetStatus(ActivityStatusCode.Error);
        }
        else if (activityOutcome != VistaTelemetry.Outcomes.Cancelled)
        {
            _activity.SetStatus(ActivityStatusCode.Ok);
        }

        if (_activity.Parent is { } parent)
        {
            parent.SetTag(VistaTelemetry.Tags.ViewName, _viewName);
            parent.SetTag(VistaTelemetry.Tags.Operation, Operation);
            parent.SetTag(VistaTelemetry.Tags.Outcome, activityOutcome);
            if (_errorCode is not null)
            {
                parent.SetTag(VistaTelemetry.Tags.ErrorCode, _errorCode);
            }

            if (_rowsReturned is long parentRows)
            {
                parent.SetTag(VistaTelemetry.Tags.RowsReturned, parentRows);
            }
        }
    }

    private void WriteMetrics(string metricOutcome)
    {
        if (VistaTelemetry.OperationCount.Enabled || VistaTelemetry.OperationDuration.Enabled)
        {
            var tags = new TagList
            {
                { VistaTelemetry.MetricTags.View, _viewName },
                { VistaTelemetry.MetricTags.Operation, Operation },
                { VistaTelemetry.MetricTags.Outcome, metricOutcome },
            };

            if (VistaTelemetry.OperationCount.Enabled)
            {
                VistaTelemetry.OperationCount.Add(1, tags);
            }

            if (VistaTelemetry.OperationDuration.Enabled)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
                VistaTelemetry.OperationDuration.Record(elapsedMs, tags);
            }
        }

        if (_rowsReturned is long rows && VistaTelemetry.OperationRows.Enabled)
        {
            var rowTags = new TagList
            {
                { VistaTelemetry.MetricTags.View, _viewName },
                { VistaTelemetry.MetricTags.Operation, Operation },
            };
            VistaTelemetry.OperationRows.Record(rows, rowTags);
        }
    }
}
