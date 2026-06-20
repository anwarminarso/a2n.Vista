# Spec 10 — Operations & Observability (cross-cutting)

> Status: **DRAFT (design intent, cross-pillar)**
> Date: 2026-06-20
> Scope: Vista operational contract — observability (tracing, metrics, logging), health checks,
> and configuration validation at startup. Vendor-neutral and organization-neutral. **Not** included:
> a specific APM implementation, particular dashboards, or user SLA policies.
>
> Decision Log source: Spec 01 §13.2 (D94 health auth, D100 observability vendor-neutral).

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

All instrumentation is **opt-in** and **zero-cost when not enabled**. `ActivitySource`/`Meter`
incur no overhead when there is no listener (standard .NET behavior). Vista does not force
a reference to the OpenTelemetry package; it only uses BCL types. Users who want to export turn on
listeners/exporters in their own composition.

## 4. Core insight: enrich, don't duplicate

Modern APM/OTel **already** automatically traces HTTP spans (ASP.NET Core) and DB spans (EF Core: SQL,
duration). Vista's added value is the **View semantics** that auto-instrumentation cannot guess:
which view, which facet, how many rows are scanned vs returned, how complex the client filter is. Vista
adds these as **attributes/activities on top of** existing spans, not a competing trace.

## 5. Tracing — `ActivitySource "a2n.Vista"`

A single `ActivitySource` named `a2n.Vista`. A span per facet execution (List/Detail/Write) with attributes
namespaced under `vista.*`:

| Attribute | Example | Description |
|---|---|---|
| `vista.view.name` | `vProductCategory` | view name |
| `vista.view.facet` | `List` / `Detail` / `Create` / `Update` / `Delete` | facet (aligned with `ViewFacet`) |
| `vista.rows.filtered` | `42` | = `recordsFiltered` (after filter) |
| `vista.rows.unfiltered` | `1000` | = `recordsTotal` (scope only) |
| `vista.page.size` | `50` | effective page size (after clamp) |
| `vista.page.index` | `0` | 0-based |
| `vista.provider` | `Sqlite` / `SqlServer` / `Npgsql` | detected EF provider |
| `vista.filter.leaf_count` | `8` | number of client filter leaves (complexity/abuse signal) |
| `vista.filter.depth` | `3` | filter tree depth |
| `vista.auth.decision` | `allow` / `deny` | result of `IsAllowedAsync` |

These attributes attach to `Activity.Current` (the HTTP span already created by ASP.NET), so in the backend
operators see one complete trace: HTTP → View semantics → SQL.

## 6. Metrics — `Meter "a2n.Vista"`

| Instrument | Type | Tags | Purpose |
|---|---|---|---|
| `vista.query.duration` | Histogram (ms) | `view`, `facet`, `provider` | execution latency; detect slow-view |
| `vista.query.rows_scanned` | Histogram | `view`, `facet` | difference between scanned vs returned → indicates a poor index/filter |
| `vista.query.errors` | Counter | `view`, `facet`, `code` | errors per code (aligned with `FilterErrorCode`/Problem `type`) |
| `vista.auth.denied` | Counter | `view`, `facet` | denial trend; detect probing/abuse |
| `vista.deprecated.hit` | Counter | `surface`, `name` | usage of a deprecated surface (see Spec 11) |

## 7. Logging — structured `ILogger` + correlation

- Structured logging via `ILogger` with a stable EventId and scope (`view`, `facet`).
- **Correlation**: the `traceId` in the RFC 7807 error (Spec 05 §9) is aligned with the W3C trace context
  (`Activity.Current`). Operators can jump from a single Problem Details (e.g., 400 `filter-field-not-allowed`)
  to the trace + SQL that triggered it without reading code.
- The `detail` of a log/error must not leak internals (raw SQL, other row values) — aligned with Spec 05 §9.1.

## 8. Health checks — operational status without source

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

Once operators build dashboards/alerts on top of `vista.query.duration` or the `vista.view.name`
attribute, **those names become a contract**. A rename = a breaking change for operations, just as
serious as changing the wire API. Therefore the list of `ActivitySource`/`Meter`/attribute/health
names in this document is **subject to the versioning & deprecation policy** (Spec 11): stable per MAJOR, renames
only with a transition period (emit old name + new name) and a record.

## 11. What Vista does NOT do

- Does not depend on any APM/exporter (the user chooses & turns it on).
- Does not duplicate HTTP/DB spans (already provided by auto-instrumentation).
- Does not force overhead when observability is not enabled.

## 12. Decision Log

| # | Decision | Status | Notes |
|---|-----------|--------|---------|
| D94 | Authorizer status is exposed via the `vista:authorizer` health check; non-Development without authorizer/opt-in = unhealthy + fail-closed startup. | **Decided** | §8, §9. Spec 01 §5.6. |
| D100 | Vendor-neutral observability (OTel-native `ActivitySource`/`Meter`/`ILogger`); enrich auto-instrumented spans; opt-in & zero-cost. | **Decided** | §3–§7. |
| D102 | `ActivitySource`/`Meter`/attribute/health names = operational contract, subject to the Spec 11 deprecation policy. | **Decided** | §10. |

## 13. Next / Forward References

- `11-versioning-and-deprecation.md` — name stability & deprecation policy.
- `05-aspnetcore-mapping.md` — RFC 7807 error model (`traceId`) correlated to the trace.
