# Observability

Vista records one trace span and three metric instruments for each outermost view operation
(list, detail, export, create, update, delete). The implementation is BCL
`System.Diagnostics.ActivitySource` and `Meter` in `a2n.Vista.Core`. Core does not reference an
OpenTelemetry package. Health checks and `ILogger` events from Spec 10 are not part of this slice.

The names below are the operational contract (Decision Log D159). Authoritative detail:
[`docs/spec/10-operations-and-observability.md`](./spec/10-operations-and-observability.md).

## Turn it on

Nothing is emitted until a listener subscribes. An application that exports via OpenTelemetry adds
the source and the meter by name:

```csharp
using a2n.Vista.Diagnostics;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(VistaTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(VistaTelemetry.MeterName));
```

`VistaTelemetry.ActivitySourceName` and `VistaTelemetry.MeterName` are both `a2n.Vista`.

Any other `ActivityListener` / `MeterListener` works the same way. When none is attached,
`VistaTelemetry.Begin` returns an empty scope: no activity, no metric, no `AsyncLocal`.

## What you see

| Signal | Name | Tags |
|---|---|---|
| Span | `vista.view.list`, `vista.view.detail`, `vista.view.export`, `vista.view.create`, `vista.view.update`, `vista.view.delete` (unknown operations use `vista.view`) | `vista.view.name`, `vista.operation`, `vista.outcome`, `vista.error.code` (failures only), `vista.rows.returned`, `vista.rows.filtered`, `vista.rows.unfiltered` |
| Counter | `vista.operation.count` (`{operation}`) | `view`, `operation`, `outcome` |
| Histogram | `vista.operation.duration` (`ms`) | `view`, `operation`, `outcome` |
| Histogram | `vista.operation.rows` (`{row}`) | `view`, `operation` |

`vista.outcome` on the span is `ok`, `not-found`, `cancelled`, or `error`. The metric `outcome`
uses those same four tokens, except a failure with a safe wire code is tagged with that code
(`write-conflict`, `write-concurrency-conflict`, `write-constraint-violation`,
`write-persistence-failed`, `filter-unknown-field`, and the other `IVistaErrorCode` values).

A detail, update, or delete that matches no row is `not-found` with status Ok, not an error.
Cancellation is `cancelled` and does not set an error status. The same tags are copied onto the
parent span when one exists (the ASP.NET Core request). Vista does not change that parent span's
status and does not start a SQL span.

## What is never a tag

SQL text, exception messages, row values, filter values, and the raw route string of a view that
did not resolve. An unresolved or over-long name is the constant `unregistered`. Error codes are
copied only when they are a short lowercase token; anything else collapses to `error`.
