using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using System.Net.Http;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.Diagnostics;
using a2n.Vista.Filter;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Observability contract (D100, D159): activities and metrics for view operations, including write
/// error codes, and a disabled path that does not emit or allocate.
/// </summary>
/// <remarks>
/// Listeners are process-wide. Every test that subscribes shares <see cref="TelemetryGate"/> so they
/// do not overlap. Assertions filter on a view name reserved for this fixture so a parallel test that
/// happens to execute a view while a listener is attached is ignored.
/// </remarks>
[NotInParallel(TelemetryGate)]
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Telemetry tests drive the reflection executor and the HTTP mapping path on purpose.")]
public sealed class VistaTelemetryTests
{
    private const string TelemetryGate = "a2n.Vista.Telemetry";
    private const string ViewName = "TelemetryProbe";

    [Test]
    public async Task Names_Are_The_Operational_Contract()
    {
        await Assert.That(VistaTelemetry.ActivitySourceName).IsEqualTo("a2n.Vista");
        await Assert.That(VistaTelemetry.MeterName).IsEqualTo("a2n.Vista");
        await Assert.That(VistaTelemetry.ActivitySource.Name).IsEqualTo(VistaTelemetry.ActivitySourceName);
        await Assert.That(VistaTelemetry.Meter.Name).IsEqualTo(VistaTelemetry.MeterName);
        await Assert.That(VistaTelemetry.OperationCount.Name).IsEqualTo(VistaTelemetry.MetricNames.OperationCount);
        await Assert.That(VistaTelemetry.OperationDuration.Name).IsEqualTo(VistaTelemetry.MetricNames.OperationDuration);
        await Assert.That(VistaTelemetry.OperationDuration.Unit).IsEqualTo("ms");
        await Assert.That(VistaTelemetry.OperationRows.Name).IsEqualTo(VistaTelemetry.MetricNames.OperationRows);
        await Assert.That(VistaTelemetry.ErrorCodes.WriteConflict).IsEqualTo("write-conflict");
        await Assert.That(VistaTelemetry.ErrorCodes.WriteConcurrencyConflict).IsEqualTo("write-concurrency-conflict");
        await Assert.That(VistaTelemetry.ErrorCodes.WriteConstraintViolation).IsEqualTo("write-constraint-violation");
        await Assert.That(VistaTelemetry.ErrorCodes.WritePersistenceFailed).IsEqualTo("write-persistence-failed");
    }

    [Test]
    public async Task List_Emits_Activity_And_Metrics_With_Row_Tags()
    {
        using var session = new TelemetrySession();
        using (var scope = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.List))
        {
            scope.Succeed(rowsReturned: 5, rowsFiltered: 25, rowsUnfiltered: 100);
        }

        var activity = session.SingleActivity();
        await Assert.That(activity.Source.Name).IsEqualTo(VistaTelemetry.ActivitySourceName);
        await Assert.That(activity.OperationName).IsEqualTo("vista.view.list");
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Ok);
        await AssertTag(activity, VistaTelemetry.Tags.ViewName, ViewName);
        await AssertTag(activity, VistaTelemetry.Tags.Operation, VistaTelemetry.Operations.List);
        await AssertTag(activity, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.Ok);
        await Assert.That(activity.GetTagItem(VistaTelemetry.Tags.ErrorCode)).IsNull();
        await AssertTag(activity, VistaTelemetry.Tags.RowsReturned, 5L);
        await AssertTag(activity, VistaTelemetry.Tags.RowsFiltered, 25L);
        await AssertTag(activity, VistaTelemetry.Tags.RowsUnfiltered, 100L);
        AssertNoSensitiveTags(activity);

        var count = session.Single(VistaTelemetry.MetricNames.OperationCount);
        await Assert.That(count.Value).IsEqualTo(1L);
        await Assert.That(count.Tags[VistaTelemetry.MetricTags.View]).IsEqualTo(ViewName);
        await Assert.That(count.Tags[VistaTelemetry.MetricTags.Operation]).IsEqualTo(VistaTelemetry.Operations.List);
        await Assert.That(count.Tags[VistaTelemetry.MetricTags.Outcome]).IsEqualTo(VistaTelemetry.Outcomes.Ok);

        var duration = session.Single(VistaTelemetry.MetricNames.OperationDuration);
        await Assert.That((double)duration.Value!).IsGreaterThanOrEqualTo(0);
        await Assert.That(duration.Tags[VistaTelemetry.MetricTags.Outcome]).IsEqualTo(VistaTelemetry.Outcomes.Ok);

        var rows = session.Single(VistaTelemetry.MetricNames.OperationRows);
        await Assert.That(rows.Value).IsEqualTo(5L);
        await Assert.That(rows.Tags.ContainsKey(VistaTelemetry.MetricTags.Outcome)).IsFalse();
    }

    [Test]
    public async Task Nested_Export_Records_One_Export_Not_The_Inner_List()
    {
        using var session = new TelemetrySession();
        using (var export = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.Export))
        {
            using (var list = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.List))
            {
                list.Succeed(rowsReturned: 25, rowsFiltered: 25, rowsUnfiltered: 25);
            }

            export.Succeed();
        }

        await Assert.That(session.Activities.Count).IsEqualTo(1);
        var activity = session.SingleActivity();
        await Assert.That(activity.OperationName).IsEqualTo("vista.view.export");
        await AssertTag(activity, VistaTelemetry.Tags.Operation, VistaTelemetry.Operations.Export);
        await AssertTag(activity, VistaTelemetry.Tags.RowsReturned, 25L);

        var counts = session.For(VistaTelemetry.MetricNames.OperationCount);
        await Assert.That(counts.Count).IsEqualTo(1);
        await Assert.That(counts[0].Tags[VistaTelemetry.MetricTags.Operation]).IsEqualTo(VistaTelemetry.Operations.Export);
        await Assert.That(session.Single(VistaTelemetry.MetricNames.OperationRows).Value).IsEqualTo(25L);
    }

    [Test]
    public async Task Write_Conflict_And_Future_Persistence_Codes_Are_Outcomes()
    {
        var conflict = new VistaWriteConflictException();
        await AssertOutcome(conflict, conflict.ErrorCode, VistaTelemetry.ErrorCodes.WriteConflict);

        var concurrency = new VistaConcurrencyConflictException();
        await AssertOutcome(concurrency, concurrency.ErrorCode, concurrency.ErrorCode);

        await AssertOutcome(
            new CodedFailure(VistaTelemetry.ErrorCodes.WriteConstraintViolation, "SELECT secret FROM Users WHERE Email = 'a@b.c'"),
            VistaTelemetry.ErrorCodes.WriteConstraintViolation,
            VistaTelemetry.ErrorCodes.WriteConstraintViolation);

        await AssertOutcome(
            new CodedFailure(VistaTelemetry.ErrorCodes.WritePersistenceFailed, "row value 42 / Server=prod;Password=hunter2"),
            VistaTelemetry.ErrorCodes.WritePersistenceFailed,
            VistaTelemetry.ErrorCodes.WritePersistenceFailed);
    }

    [Test]
    public async Task Filter_Validation_Uses_The_Wire_Code_And_Drops_The_Message()
    {
        var exception = new FilterValidationException(
            FilterErrorCode.UnknownField,
            "SELECT name FROM Customers WHERE Email = 'person@example.com'",
            field: "Email");

        await AssertOutcome(exception, exception.ErrorCode, FilterErrorCodes.UnknownField);
    }

    [Test]
    public async Task Unsafe_Error_Code_Collapses_To_Error_Without_The_Message()
    {
        await AssertOutcome(
            new CodedFailure("Email=person@example.com", "password=hunter2"),
            VistaTelemetry.Outcomes.Error,
            errorCode: null);

        using var session = new TelemetrySession();
        using (var scope = VistaTelemetry.Begin(ViewName, "DROP TABLE"))
        {
            scope.Fail(new InvalidOperationException("SELECT * FROM Secrets"));
        }

        var activity = session.SingleActivity();
        await Assert.That(activity.OperationName).IsEqualTo("vista.view");
        await AssertTag(activity, VistaTelemetry.Tags.Operation, VistaTelemetry.Operations.Unknown);
        await AssertTag(activity, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.Error);
        await Assert.That(activity.GetTagItem(VistaTelemetry.Tags.ErrorCode)).IsNull();
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Error);
        AssertNoSensitiveTags(activity);
        await Assert.That(session.Single(VistaTelemetry.MetricNames.OperationCount).Tags[VistaTelemetry.MetricTags.Outcome])
            .IsEqualTo(VistaTelemetry.Outcomes.Error);
    }

    [Test]
    public async Task Cancellation_Is_Not_An_Error_Status()
    {
        using var session = new TelemetrySession();
        using (var scope = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.Detail))
        {
            scope.Fail(new OperationCanceledException());
        }

        var activity = session.SingleActivity();
        await AssertTag(activity, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.Cancelled);
        await Assert.That(activity.GetTagItem(VistaTelemetry.Tags.ErrorCode)).IsNull();
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That(session.Single(VistaTelemetry.MetricNames.OperationCount).Tags[VistaTelemetry.MetricTags.Outcome])
            .IsEqualTo(VistaTelemetry.Outcomes.Cancelled);
    }

    [Test]
    public async Task NotFound_Is_Distinct_From_Error_And_Unresolved_Names_Are_Not_Tagged()
    {
        using var session = new TelemetrySession();
        using (var scope = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.Detail))
        {
            scope.NotFound();
        }

        var activity = session.SingleActivity();
        await AssertTag(activity, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.NotFound);
        await AssertTag(activity, VistaTelemetry.Tags.ViewName, ViewName);
        await AssertTag(activity, VistaTelemetry.Tags.RowsReturned, 0L);
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Ok);
        await Assert.That(session.Single(VistaTelemetry.MetricNames.OperationCount).Tags[VistaTelemetry.MetricTags.Outcome])
            .IsEqualTo(VistaTelemetry.Outcomes.NotFound);

        using (var scope = VistaTelemetry.Begin(new string('x', 300), VistaTelemetry.Operations.List))
        {
            scope.Fail(new VistaViewNotFoundException("attacker-supplied-name"));
        }

        var missed = session.Activities[^1];
        await AssertTag(missed, VistaTelemetry.Tags.ViewName, VistaTelemetry.UnregisteredViewName);
        await AssertTag(missed, VistaTelemetry.Tags.ErrorCode, VistaTelemetry.ErrorCodes.ViewNotFound);
        await Assert.That(TagValues(missed)).DoesNotContain("attacker-supplied-name");
    }

    [Test]
    public async Task NoteView_Replaces_The_Placeholder_With_The_Registered_Name()
    {
        using var session = new TelemetrySession();
        using (var scope = VistaTelemetry.Begin(VistaTelemetry.UnregisteredViewName, VistaTelemetry.Operations.Update))
        {
            VistaTelemetry.NoteView(ViewName);
            scope.NotFound();
        }

        await AssertTag(session.SingleActivity(), VistaTelemetry.Tags.ViewName, ViewName);
        await Assert.That(session.Single(VistaTelemetry.MetricNames.OperationCount).Tags[VistaTelemetry.MetricTags.View])
            .IsEqualTo(ViewName);
    }

    [Test]
    public async Task Parent_Span_Is_Enriched_With_The_Same_Low_Cardinality_Tags()
    {
        using var parentSource = new ActivitySource("test.vista.parent");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == parentSource.Name || source.Name == VistaTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var meters = new TelemetrySession();

        using var parent = parentSource.StartActivity("GET /api/views/probe", ActivityKind.Server);
        using (var scope = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.List))
        {
            scope.Succeed(rowsReturned: 2, rowsFiltered: 2, rowsUnfiltered: 2);
        }

        await Assert.That(parent).IsNotNull();
        await AssertTag(parent!, VistaTelemetry.Tags.ViewName, ViewName);
        await AssertTag(parent!, VistaTelemetry.Tags.Operation, VistaTelemetry.Operations.List);
        await AssertTag(parent!, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.Ok);
        await AssertTag(parent!, VistaTelemetry.Tags.RowsReturned, 2L);
        await Assert.That(parent!.GetTagItem(VistaTelemetry.Tags.ErrorCode)).IsNull();
    }

    [Test]
    public async Task Executor_List_Detail_And_Failures_Emit_The_Contract()
    {
        using var session = new TelemetrySession();
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView(ViewName);
        var scope = new ViewScope();

        var page = await harness.Executor.ListAsync<WidgetRow>(
            view,
            new ViewQueryRequest(null, [], 0, 5),
            scope,
            CancellationToken.None);

        await Assert.That(page.Page.Items.Count).IsEqualTo(5);
        var listed = session.SingleActivity();
        await AssertTag(listed, VistaTelemetry.Tags.ViewName, ViewName);
        await AssertTag(listed, VistaTelemetry.Tags.Operation, VistaTelemetry.Operations.List);
        await AssertTag(listed, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.Ok);
        await AssertTag(listed, VistaTelemetry.Tags.RowsReturned, 5L);
        await AssertTag(listed, VistaTelemetry.Tags.RowsFiltered, (long)WidgetTestHarness.SeededRowCount);
        await AssertTag(listed, VistaTelemetry.Tags.RowsUnfiltered, (long)WidgetTestHarness.SeededRowCount);

        var missing = await harness.Executor.DetailAsync<WidgetRow>(view, 99999, scope, CancellationToken.None);
        await Assert.That(missing).IsNull();
        var detail = session.Activities.Last(activity =>
            Tag(activity, VistaTelemetry.Tags.ViewName) == ViewName
            && Tag(activity, VistaTelemetry.Tags.Operation) == VistaTelemetry.Operations.Detail);
        await AssertTag(detail, VistaTelemetry.Tags.Operation, VistaTelemetry.Operations.Detail);
        await AssertTag(detail, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.NotFound);
        await AssertTag(detail, VistaTelemetry.Tags.RowsReturned, 0L);

        var rejected = await CaptureAsync(() => harness.Executor.ListAsync<WidgetRow>(
            view,
            new ViewQueryRequest(new FilterLeaf("Missing", FilterOperator.Equals, "secret-value"), [], 0, 10),
            scope,
            CancellationToken.None));
        await Assert.That(rejected).IsTypeOf<FilterValidationException>();
        var failed = session.Activities.Last(activity =>
            Tag(activity, VistaTelemetry.Tags.ViewName) == ViewName
            && Tag(activity, VistaTelemetry.Tags.ErrorCode) == FilterErrorCodes.UnknownField);
        await AssertTag(failed, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.Error);
        await AssertTag(failed, VistaTelemetry.Tags.ErrorCode, FilterErrorCodes.UnknownField);
        await Assert.That(TagValues(failed)).DoesNotContain("secret-value");

        var pageSize = await CaptureAsync(() => harness.Executor.ListAsync<WidgetRow>(
            view,
            new ViewQueryRequest(null, [], 0, -1),
            scope,
            CancellationToken.None));
        await Assert.That(pageSize).IsTypeOf<ArgumentOutOfRangeException>();
        var invalidPage = session.Activities.Last(activity =>
            Tag(activity, VistaTelemetry.Tags.ViewName) == ViewName
            && Tag(activity, VistaTelemetry.Tags.ErrorCode) == VistaTelemetry.ErrorCodes.InvalidPageSize);
        await AssertTag(invalidPage, VistaTelemetry.Tags.ErrorCode, VistaTelemetry.ErrorCodes.InvalidPageSize);

        var outcomes = session.For(VistaTelemetry.MetricNames.OperationCount)
            .Where(sample => (string?)sample.Tags.GetValueOrDefault(VistaTelemetry.MetricTags.View) == ViewName)
            .Select(sample => (string)sample.Tags[VistaTelemetry.MetricTags.Outcome]!)
            .ToArray();
        await Assert.That(outcomes).Contains(VistaTelemetry.Outcomes.Ok);
        await Assert.That(outcomes).Contains(VistaTelemetry.Outcomes.NotFound);
        await Assert.That(outcomes).Contains(FilterErrorCodes.UnknownField);
        await Assert.That(outcomes).Contains(VistaTelemetry.ErrorCodes.InvalidPageSize);
    }

    [Test]
    public async Task Http_List_Export_And_Detail_Miss_Emit_One_Operation_Each()
    {
        using var session = new TelemetrySession();
        await using var app = await TelemetryHost.StartAsync();

        var list = await app.Client.PostAsync(
            "/api/views/TelemetryWidgets/list",
            Json("""{"page":0,"pageSize":5}"""));
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var listActivity = session.Activities.Single(activity =>
            Tag(activity, VistaTelemetry.Tags.ViewName) == "TelemetryWidgets"
            && Tag(activity, VistaTelemetry.Tags.Operation) == VistaTelemetry.Operations.List);
        await AssertTag(listActivity, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.Ok);
        await AssertTag(listActivity, VistaTelemetry.Tags.RowsReturned, 5L);
        await Assert.That(session.Activities.Count(activity =>
            Tag(activity, VistaTelemetry.Tags.ViewName) == "TelemetryWidgets")).IsEqualTo(1);

        var export = await app.Client.PostAsync(
            "/api/views/TelemetryWidgets/export",
            Json("""{"format":"csv"}"""));
        await Assert.That(export.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var exportActivities = session.Activities.Where(activity =>
            Tag(activity, VistaTelemetry.Tags.ViewName) == "TelemetryWidgets"
            && Tag(activity, VistaTelemetry.Tags.Operation) == VistaTelemetry.Operations.Export).ToArray();
        await Assert.That(exportActivities.Length).IsEqualTo(1);
        await Assert.That(exportActivities[0].OperationName).IsEqualTo("vista.view.export");
        await Assert.That(Convert.ToInt64(exportActivities[0].GetTagItem(VistaTelemetry.Tags.RowsReturned))).IsGreaterThan(0);

        var detail = await app.Client.PostAsync(
            "/api/views/TelemetryWidgets/detail",
            Json("""{"key":99999}"""));
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var detailActivity = session.Activities.Last(activity =>
            Tag(activity, VistaTelemetry.Tags.ViewName) == "TelemetryWidgets"
            && Tag(activity, VistaTelemetry.Tags.Operation) == VistaTelemetry.Operations.Detail);
        await AssertTag(detailActivity, VistaTelemetry.Tags.Outcome, VistaTelemetry.Outcomes.NotFound);

        var badFilter = await app.Client.PostAsync(
            "/api/views/TelemetryWidgets/list",
            Json("""{"page":0,"pageSize":5,"filter":{"field":"Missing","op":"Equals","value":"secret-value"}}"""));
        await Assert.That(badFilter.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var filterActivity = session.Activities.Last(activity =>
            Tag(activity, VistaTelemetry.Tags.ViewName) == "TelemetryWidgets"
            && Tag(activity, VistaTelemetry.Tags.Operation) == VistaTelemetry.Operations.List
            && Tag(activity, VistaTelemetry.Tags.Outcome) == VistaTelemetry.Outcomes.Error);
        await AssertTag(filterActivity, VistaTelemetry.Tags.ErrorCode, FilterErrorCodes.UnknownField);
        await Assert.That(TagValues(filterActivity)).DoesNotContain("secret-value");

        var exportCounts = session.For(VistaTelemetry.MetricNames.OperationCount)
            .Where(sample => (string?)sample.Tags[VistaTelemetry.MetricTags.View] == "TelemetryWidgets"
                && (string?)sample.Tags[VistaTelemetry.MetricTags.Operation] == VistaTelemetry.Operations.Export)
            .ToArray();
        await Assert.That(exportCounts.Length).IsEqualTo(1);
        var listDuringExport = session.For(VistaTelemetry.MetricNames.OperationCount)
            .Count(sample => (string?)sample.Tags[VistaTelemetry.MetricTags.View] == "TelemetryWidgets"
                && (string?)sample.Tags[VistaTelemetry.MetricTags.Operation] == VistaTelemetry.Operations.List);
        // One successful list plus one rejected list. The export must not add a third list sample.
        await Assert.That(listDuringExport).IsEqualTo(2);
    }

    [Test]
    public async Task Disabled_Path_Emits_Nothing_And_Does_Not_Allocate()
    {
        using (var gate = new TelemetrySession())
        {
            using (var scope = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.List))
            {
                scope.Succeed(1, 1, 1);
            }

            await Assert.That(gate.Activities.Count(activity => Tag(activity, VistaTelemetry.Tags.ViewName) == ViewName)).IsEqualTo(1);
        }

        // TUnit's HTML reporter subscribes to every ActivitySource, and a meter listener can keep
        // Instrument.Enabled true after our own session is disposed. Suspend both so this measurement
        // sees the production disabled path, then put them back. The suspension is process-wide and
        // this fixture does not overlap itself.
        using var quietActivities = ForeignActivityListeners.Suspend(VistaTelemetry.ActivitySource);
        using var quietMeters = ForeignMeterListeners.Suspend(
            VistaTelemetry.OperationCount,
            VistaTelemetry.OperationDuration,
            VistaTelemetry.OperationRows);

        var started = 0;
        using (var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == VistaTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => started++,
        })
        {
            ActivitySource.AddActivityListener(listener);
        }

        for (var i = 0; i < 64; i++)
        {
            var warmup = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.List);
            warmup.Succeed(1, 1, 1);
            warmup.NotFound();
            warmup.Dispose();
            VistaTelemetry.NoteView(ViewName);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            var scope = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.List);
            scope.Succeed(1, 1, 1);
            scope.NotFound();
            scope.Dispose();
            VistaTelemetry.NoteView(ViewName);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(started).IsEqualTo(0);
        await Assert.That(VistaTelemetry.ActivitySource.HasListeners()).IsFalse();
        await Assert.That(VistaTelemetry.IsEnabled).IsFalse();
        await Assert.That(allocated).IsEqualTo(0);
    }

    private static bool IsFixtureView(string? view) =>
        view is ViewName or "TelemetryWidgets" || view == VistaTelemetry.UnregisteredViewName;

    private static async Task AssertOutcome(Exception exception, string metricOutcome, string? errorCode)
    {
        using var session = new TelemetrySession();
        using (var scope = VistaTelemetry.Begin(ViewName, VistaTelemetry.Operations.Create))
        {
            scope.Fail(exception);
        }

        var activity = session.SingleActivity();
        await Assert.That(activity.Status).IsEqualTo(
            metricOutcome == VistaTelemetry.Outcomes.Cancelled ? ActivityStatusCode.Unset : ActivityStatusCode.Error);
        await AssertTag(
            activity,
            VistaTelemetry.Tags.Outcome,
            metricOutcome is VistaTelemetry.Outcomes.Error or VistaTelemetry.Outcomes.Cancelled
                ? metricOutcome
                : VistaTelemetry.Outcomes.Error);
        await Assert.That(activity.GetTagItem(VistaTelemetry.Tags.ErrorCode)).IsEqualTo(errorCode);
        AssertNoSensitiveTags(activity);

        var count = session.Single(VistaTelemetry.MetricNames.OperationCount);
        await Assert.That(count.Tags[VistaTelemetry.MetricTags.Outcome]).IsEqualTo(metricOutcome);
        await Assert.That(count.Tags[VistaTelemetry.MetricTags.View]).IsEqualTo(ViewName);
        await Assert.That(count.Tags[VistaTelemetry.MetricTags.Operation]).IsEqualTo(VistaTelemetry.Operations.Create);
    }

    private static async Task AssertTag(Activity activity, string name, object expected) =>
        await Assert.That(activity.GetTagItem(name)).IsEqualTo(expected);

    private static string? Tag(Activity activity, string name) => activity.GetTagItem(name) as string;

    private static List<string> TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => tag.Value?.ToString() ?? string.Empty).ToList();

    private static void AssertNoSensitiveTags(Activity activity)
    {
        foreach (var tag in activity.TagObjects)
        {
            var text = tag.Value?.ToString() ?? string.Empty;
            if (text.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                || text.Contains('@')
                || text.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || text.Contains("secret", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Tag '{tag.Key}' leaked '{text}'.");
            }
        }
    }

    private static async Task<Exception> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Expected the operation to throw.");
    }

    private static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    private sealed class CodedFailure : Exception, IVistaErrorCode
    {
        public CodedFailure(string code, string message)
            : base(message) =>
            ErrorCode = code;

        public string ErrorCode { get; }
    }

    private sealed class TelemetryViews : ViewTemplate<WidgetContext>
    {
        protected override void Configure(IViewTemplateBuilder<WidgetContext> views) =>
            views.AddView("TelemetryWidgets", (db, _) =>
                    from w in db.Widgets
                    select new { w.Id, w.Name, w.Price })
                .Field(x => x.Id, f => f.PrimaryKey());
    }

    private sealed class TelemetryHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private TelemetryHost(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<TelemetryHost> StartAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<WidgetContext>(options => options.UseSqlite(connection));
                        services.AddVista(vista => vista.RegisterTemplate<TelemetryViews, WidgetContext>());
                        services.AddVistaEndpoints(endpoints => endpoints.AllowAnonymousAccess());
                    })
                    .Configure(app =>
                    {
                        app.UseVistaExceptionHandling();
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapVistaViews());
                    }))
                .StartAsync();

            using (var scope = host.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<WidgetContext>();
                context.Database.EnsureCreated();
                context.Widgets.AddRange(Enumerable.Range(1, WidgetTestHarness.SeededRowCount)
                    .Select(i => new Widget { Id = i, Name = $"Widget {i}", Price = i * 10m }));
                context.SaveChanges();
            }

            return new TelemetryHost(host, connection, host.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            _connection.Dispose();
        }
    }

    /// <summary>
    /// Detaches every <see cref="ActivityListener"/> currently subscribed to one source and reattaches
    /// those same instances on dispose. Used so the disabled-path measurement is not kept hot by the
    /// test host's own reporter.
    /// </summary>
    private sealed class ForeignActivityListeners : IDisposable
    {
        private readonly List<ActivityListener> _suspended;

        private ForeignActivityListeners(List<ActivityListener> suspended) => _suspended = suspended;

        public static ForeignActivityListeners Suspend(ActivitySource source)
        {
            var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var all = typeof(ActivitySource).GetField("s_allListeners", flags)!.GetValue(null)!;
            var suspended = ReadListeners(all)
                .Where(listener => listener.ShouldListenTo?.Invoke(source) == true)
                .ToList();

            var detach = typeof(ActivitySource).GetMethod("DetachListener", flags)!;
            foreach (var listener in suspended)
            {
                detach.Invoke(null, [listener]);
            }

            return new ForeignActivityListeners(suspended);
        }

        public void Dispose()
        {
            foreach (var listener in _suspended)
            {
                ActivitySource.AddActivityListener(listener);
            }
        }

        /// <summary>
        /// net8 stores listeners in <c>_list</c>. net9 and net10 store an immutable <c>_volatileArray</c>.
        /// </summary>
        private static ActivityListener[] ReadListeners(object all)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var type = all.GetType();
            if (type.GetField("_volatileArray", flags)?.GetValue(all) is ActivityListener[] array)
            {
                return array.Where(listener => listener is not null).ToArray();
            }

            if (type.GetField("_list", flags)?.GetValue(all) is IEnumerable list)
            {
                return list.Cast<ActivityListener>().ToArray();
            }

            throw new InvalidOperationException($"Unrecognized activity-listener list '{type.FullName}'.");
        }
    }

    /// <summary>
    /// Disables measurement events for Vista's instruments on every current <see cref="MeterListener"/>
    /// and re-enables the same listener/state pairs on dispose.
    /// </summary>
    private sealed class ForeignMeterListeners : IDisposable
    {
        private readonly List<(MeterListener Listener, Instrument Instrument, object? State)> _suspended;

        private ForeignMeterListeners(List<(MeterListener Listener, Instrument Instrument, object? State)> suspended) =>
            _suspended = suspended;

        public static ForeignMeterListeners Suspend(params Instrument[] instruments)
        {
            var suspended = new List<(MeterListener, Instrument, object?)>();
            foreach (var instrument in instruments)
            {
                foreach (var (listener, state) in Subscribers(instrument))
                {
                    listener.DisableMeasurementEvents(instrument);
                    suspended.Add((listener, instrument, state));
                }
            }

            return new ForeignMeterListeners(suspended);
        }

        public void Dispose()
        {
            foreach (var (listener, instrument, state) in _suspended)
            {
                listener.EnableMeasurementEvents(instrument, state);
            }
        }

        private static List<(MeterListener Listener, object? State)> Subscribers(Instrument instrument)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var subscriptions = typeof(Instrument).GetField("_subscriptions", flags)!.GetValue(instrument)!;
            var node = subscriptions.GetType().GetField("_first", flags)!.GetValue(subscriptions);
            var subscribers = new List<(MeterListener, object?)>();
            while (node is not null)
            {
                var nodeType = node.GetType();
                var value = nodeType.GetField("Value")!.GetValue(node)!;
                var valueType = value.GetType();
                var listener = (MeterListener)valueType.GetProperty("Listener", flags)!.GetValue(value)!;
                var state = valueType.GetProperty("State", flags)!.GetValue(value);
                subscribers.Add((listener, state));
                node = nodeType.GetField("Next")!.GetValue(node);
            }

            return subscribers;
        }
    }

    private sealed class Measurement
    {
        public Measurement(string name, object? value, Dictionary<string, object?> tags)
        {
            Name = name;
            Value = value;
            Tags = tags;
        }

        public string Name { get; }

        public object? Value { get; }

        public Dictionary<string, object?> Tags { get; }
    }

    private sealed class TelemetrySession : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<Activity> _activities = [];
        private readonly List<Measurement> _measurements = [];
        private readonly ActivityListener _activitiesListener;
        private readonly MeterListener _meters;

        public TelemetrySession()
        {
            _activitiesListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == VistaTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (!IsFixtureView(activity.GetTagItem(VistaTelemetry.Tags.ViewName) as string))
                    {
                        return;
                    }

                    lock (_gate)
                    {
                        _activities.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_activitiesListener);

            _meters = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == VistaTelemetry.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
            _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
            _meters.Start();
        }

        public List<Activity> Activities
        {
            get
            {
                lock (_gate)
                {
                    return _activities.ToList();
                }
            }
        }

        public List<Measurement> Measurements
        {
            get
            {
                lock (_gate)
                {
                    return _measurements.ToList();
                }
            }
        }

        public Activity SingleActivity() =>
            Activities.Single(activity => Tag(activity, VistaTelemetry.Tags.ViewName) == ViewName);

        public Measurement Single(string name) =>
            For(name).Single(sample => (string?)sample.Tags.GetValueOrDefault(VistaTelemetry.MetricTags.View) == ViewName);

        public List<Measurement> For(string name) =>
            Measurements.Where(sample => sample.Name == name).ToList();

        public void Dispose()
        {
            _meters.Dispose();
            _activitiesListener.Dispose();
        }

        private void Record(string name, object? value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            if (!IsFixtureView(copy.GetValueOrDefault(VistaTelemetry.MetricTags.View) as string))
            {
                return;
            }

            lock (_gate)
            {
                _measurements.Add(new Measurement(name, value, copy));
            }
        }
    }
}
