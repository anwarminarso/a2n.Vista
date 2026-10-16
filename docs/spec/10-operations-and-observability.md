# Spec 10 — Operations & Observability (cross-cutting)

> Status: **PARTIAL — traces and metrics landed (D159); logging, health checks, and startup
> validation remain design intent**
> Date: 2026-06-20; operation telemetry updated 2026-10-16
> Scope: Vista operational contract — observability (tracing, metrics, logging), health checks,
> and configuration validation at startup. Vendor-neutral and organization-neutral. **Not** included:
> a specific APM implementation, particular dashboards, or user SLA policies.
>
> Decision Log source: Spec 01 §13.2 (D94 health auth, D100 observability vendor-neutral, D159
> operation traces and metrics). Wiring: `docs/observability.md`.

---

## 1. Purpose

Vista exposes queries that are partly client-controlled (filter tree, scope, paging). For a library
like this, **observability is part of the security & operational contract**, not an add-on.
This document defines *what* Vista emits and *how* operators read it — without
binding to any particular monitoring backend.

Two principles shape the entire document:

1. **Vendor-neutral.** Instrumentation uses the OpenTelemetry primitives built into .NET
   (`System.Diagnostics.ActivitySource`, `System.Diagnostics.Metrics.Meter`, `ILogger`). Any backend
   (OTLP collector, Jaeger, Prometheus, Datadog, IBM Instana, Serilog sink, etc.) consumes them
   without Vista knowing who the consumer is. **No APM dependency in any package** — consistent
   with the D48 layering.
2. **Operator ≠ author.** Assume that whoever operates the application is often not the one who wrote the code and
   may have no access to source. Therefore operational signals must be **self-describing** and actionable
   without reading code (health checks, actionable startup messages, clearly named metrics).

## 2. Non-Goals

- Choosing/standardizing a monitoring backend (that is the user's decision).
- Building HTTP/DB tracing ourselves — ASP.NET Core & EF Core are already auto-instrumented by APM/OTel;
  Vista **enriches**, it does not duplicate (§4).
- Ready-made dashboards/alerts (these may be a separate sample, not part of the contract).

## 3. Principle: opt-in & zero-cost

All instrumentation is **opt-in** and **zero-cost when not enabled**. `VistaTelemetry.Begin` checks
`ActivitySource.HasListeners` and `Instrument.Enabled` before it touches `AsyncLocal` or allocates.
With no listener it returns an empty scope. Vista does not reference an OpenTelemetry package; it
only uses BCL types. Users who want to export subscribe to `a2n.Vista` in their own composition
(`docs/observability.md`).

## 4. Core insight: one view span, enrich the parent

Modern APM/OTel **already** traces HTTP spans (ASP.NET Core) and DB spans (EF Core: SQL, duration).
Vista does not start those spans again. It starts **one child span per outermost view operation**
so a direct `IViewExecutor` call (no HTTP parent) and an export (which runs a list internally) each
have their own span. The same low-cardinality tags are copied onto `Activity.Parent` when a parent
exists. Vista does not change the parent's status: ASP.NET Core already derives that from the
response. Nested `Begin` calls join the ambient operation and do not emit a second span or metric.

## 5. Tracing — `ActivitySource "a2n.Vista"` (landed, D159)

A single `ActivitySource` named `a2n.Vista` (`VistaTelemetry.ActivitySourceName`). Span names are a
fixed set, never an interpolated caller string:

| Span name | Operation |
|---|---|
| `vista.view.list` | list / query |
| `vista.view.detail` | detail by key |
| `vista.view.export` | export (distinct from the inner list) |
| `vista.view.create` | create |
| `vista.view.update` | update |
| `vista.view.delete` | delete |
| `vista.view` | anything that is not a safe operation token |

| Attribute | Example | Description |
|---|---|---|
| `vista.view.name` | `vProductCategory` or `unregistered` | Canonical registered name. Longer than 128 characters, blank, or not yet resolved → `unregistered`. The raw route string is never copied. |
| `vista.operation` | `list` / `detail` / `export` / `create` / `update` / `delete` | Safe token, or `unknown`. |
| `vista.outcome` | `ok` / `not-found` / `cancelled` / `error` | Coarse result. `not-found` is a completed miss (detail/update/delete), status Ok. `cancelled` does not set an error status. |
| `vista.error.code` | `write-constraint-violation` | Present only when `vista.outcome` is `error` and the exception carries a safe `IVistaErrorCode`. |
| `vista.rows.returned` | `50` | Rows materialized for the caller. `0` on a miss, `1` on a hit or a successful write. |
| `vista.rows.filtered` | `42` | `recordsFiltered`, list and export. |
| `vista.rows.unfiltered` | `1000` | `recordsTotal`, list and export. |

**Not recorded:** SQL text, exception messages, row values, filter values, page index, provider name,
filter shape, or the authorization decision as its own attribute. A denied call still fails the
operation, and the wire code (`forbidden`, `filter-unknown-field`, …) is the error tag. The draft
attributes `vista.view.facet`, `vista.page.*`, `vista.provider`, `vista.filter.*`, and
`vista.auth.decision` were never shipped; D159 replaces that table.

Write codes flow through `IVistaErrorCode.ErrorCode` when the value is a safe token (1–64 characters,
lowercase letters, digits, single interior hyphens). On this branch those codes include
`write-conflict` and `write-concurrency-conflict`. `write-constraint-violation` and
`write-persistence-failed` are part of the same contract and are recorded when an exception carries
them (the unmerged save-failure split, D158). An unsafe code collapses to outcome `error` with no
`vista.error.code`.

## 6. Metrics — `Meter "a2n.Vista"` (landed, D159)

| Instrument | Type | Tags | Purpose |
|---|---|---|---|
| `vista.operation.count` | Counter (`{operation}`) | `view`, `operation`, `outcome` | Operations. On failure, `outcome` is the safe wire code (`write-conflict`, `write-constraint-violation`, `write-persistence-failed`, …) so the series splits by code. Success uses `ok`, `not-found`, or `cancelled`. |
| `vista.operation.duration` | Histogram (`ms`) | `view`, `operation`, `outcome` | Latency of the same operation. Default histogram buckets. |
| `vista.operation.rows` | Histogram (`{row}`) | `view`, `operation` | Rows returned, when known. Outcome is omitted so this series stays smaller. |

The draft instruments `vista.query.duration`, `vista.query.rows_scanned`, `vista.query.errors`,
`vista.auth.denied`, and `vista.deprecated.hit` were never shipped. `vista.auth.denied` and
`vista.deprecated.hit` remain future work; a denied operation is already counted as
`vista.operation.count` with outcome `forbidden`.

## 7. Logging — structured `ILogger` + correlation (not built)

- Structured logging via `ILogger` with a stable EventId and scope (`view`, `facet`).
- **Correlation**: the `traceId` in the RFC 7807 error (Spec 05 §9) is aligned with the W3C trace context
  (`Activity.Current`). Operators can jump from a single Problem Details (e.g., 400 `filter-field-not-allowed`)
  to the trace + SQL that triggered it without reading code.
- The `detail` of a log/error must not leak internals (raw SQL, other row values) — aligned with Spec 05 §9.1.

## 8. Health checks — operational status without source (not built)

Vista registers standard health checks (`Microsoft.Extensions.Diagnostics.HealthChecks`) so that
operators (who may have no source) can *gate* go-live and set up alerts:

| Check | Unhealthy when | Purpose |
|---|---|---|
| `vista:authorizer` | non-Development & no authorizer & no `AllowAnonymousAccess()` opt-in (D94) | prevents a deploy that forgets auth from silently being open |
| `vista:registry` | no view registered / execution plan missing | detects registration mis-wiring |
| `vista:config` | invalid configuration / a removed-or-renamed setting detected (Spec 11) | breaking change deploy-time-detectable |

These health checks complement the **fail-closed startup of D94**: in non-Development, the absence of an authorizer
without opt-in not only fails startup, but is also reported as `Unhealthy`, which an operator can act on
via a dashboard/probe, not just an exception in the console.

## 9. Configuration validation at startup (deploy-time detectable)

Because operators may not be able to read the source, configuration changes/errors must be
**detected at boot** with an actionable message:

- A setting that is **removed/renamed** between versions → startup fails with the message "setting X removed in
  vN, use Y" (not silently ignored).
- A missing authorizer in non-Development without opt-in (D94) → startup fails.
- A view without an execution plan when mapped → startup fails (fail-fast), not a 500 per request.

Details of the setting deprecation policy are in `11-versioning-and-deprecation.md`.

## 10. Names = operational contract

Once operators build dashboards/alerts on top of `vista.operation.duration` or the `vista.view.name`
attribute, **those names become a contract**. A rename = a breaking change for operations, just as
serious as changing the wire API. Therefore the list of `ActivitySource`/`Meter`/attribute/health
names in this document is **subject to the versioning & deprecation policy** (Spec 11): stable per MAJOR, renames
only with a transition period (emit old name + new name) and a record. The D159 names are the first
shipped set. The draft query/facet names in the pre-D159 text of §5–§6 were never emitted, so there
is no old series to dual-publish.

## 11. What Vista does NOT do

- Does not depend on any APM/exporter (the user chooses & turns it on).
- Does not duplicate HTTP/DB spans (already provided by auto-instrumentation).
- Does not force overhead when observability is not enabled.

## 12. Decision Log

| # | Decision | Status | Notes |
|---|-----------|--------|---------|
| D94 | Authorizer status is exposed via the `vista:authorizer` health check; non-Development without authorizer/opt-in = unhealthy + fail-closed startup. | **Decided** | §8, §9. Spec 01 §5.6. |
| D100 | Vendor-neutral observability (OTel-native `ActivitySource`/`Meter`/`ILogger`); enrich auto-instrumented spans; opt-in & zero-cost. | **Decided** | §3–§7. Traces and metrics landed as D159. `ILogger` is still §7. |
| D102 | `ActivitySource`/`Meter`/attribute/health names = operational contract, subject to the Spec 11 deprecation policy. | **Decided** | §10. |
| D159 | **Operation traces and metrics.** One `ActivitySource` and one `Meter`, both `a2n.Vista`, in Core (`a2n.Vista.Diagnostics.VistaTelemetry`). One span per outermost list/detail/export/create/update/delete. Tags and instruments are §5–§6. No OpenTelemetry package reference. Empty scope when no listener is attached. `ILogger`, health checks, `vista.auth.denied`, and `vista.deprecated.hit` stay unbuilt. D154–D158 belong to other in-flight work; this slice is D159. | **Done** | §3–§6. `docs/observability.md`. |

## 13. Next / Forward References

- `11-versioning-and-deprecation.md` — name stability & deprecation policy.
- `05-aspnetcore-mapping.md` — RFC 7807 error model (`traceId`) correlated to the trace.
