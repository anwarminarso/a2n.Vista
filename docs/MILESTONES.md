# a2n.Vista — Milestones & Roadmap Tracker

> Status: **LIVING DOCUMENT** — update as milestones land.
> Last updated: 2026-06-28
> Purpose: an **at-a-glance** map of every milestone from foundation to release, what is done, what
> remains, the dependencies between them, and where we are right now. This is the readable companion to
> the two deeper docs:
> - **`ROADMAP.md`** — the *vision*: why Vista exists, the three pillars, the release stages.
> - **`docs/PROJECT-STATUS.md`** — the *detailed, authoritative* snapshot: every settled decision
>   (`D###`), code locations, and reconciliation history.
>
> When this document and the code/PROJECT-STATUS disagree, **the code is the source of truth**; reconcile
> here, not the reverse.

---

## 1. Global status

```
Pillar 1  — View engine            ██████████ 100%   done
Pillar 2  — server half (engine)   ██████████ 100%   done & hardened
Pillar 2  — client half (adapters) ████░░░░░░  ~40%   DataTables + export + QB schema landed
Pillar 3  — source generator       ██░░░░░░░░  ~15%   Phase 1 landed (shape-driven export accessors)
```

Rough progress toward **v1.0 (production-ready): ~52%**. Foundation, the full server-half query engine,
the HTTP action surface, the first grid adapter, the export pipeline, the QueryBuilder schema emitter, and
**source-generator Phase 1** (incremental pipeline + shape-driven export accessors, AOT-clean) are done.
The heavy remaining work is the rest of the source generator (executable plans, JSON contexts), the write
path, and the ecosystem (more adapters, TS client, OpenAPI).

---

## 2. Milestones — DONE

| # | Milestone | Spec | Key decisions |
|---|-----------|------|---------------|
| **M1** | Core View engine — contracts, authoring Style A + B, `FilterCompiler`, `EfViewExecutor`, endpoints, one-door auth | `pilar-1-core` | D1–D50, DR1–DR10 |
| **M2** | Hardening — auth fail-safe, masked-field defaults, route groups + single-source route | `pilar-1-hardening` | D94, D95, D101, D103 |
| **M3** | Query-engine hardening — view key model, deterministic paging, `IQueryDialect` port, DoS guards, composite key | `query-engine-hardening` | D104, D106–D109 |
| **M4** | HTTP action surface — `POST list/detail/export` + `GET metadata`, query/key in JSON body | `http-surface-redesign` | D110 |
| **M5** | DataTables.NET adapter + multi-channel request (`Search`/`Scope` slots) | `datatables-adapter` | D111–D114 |
| **M6** | Close-out — startup provider guard, opt-in metadata cache, HTTP TestServer tests, doc reconciliation, language policy | — | D107 (guard) |
| **M7** | Export pipeline — pluggable `IViewExportWriter`, built-in CSV + XLSX (zero-dependency), developer-overridable; `POST {route}/export?format=` | `export-pipeline` | D115 |
| **M8** | Metadata schema adapters — `IViewMetadataAdapter` + jQuery-QueryBuilder `metadataQB` emitter, `GET {route}/querybuilder` | `metadata-schema-adapters` | D116 |

**Verified at M8:** solution build green on net8/9/10, **108 tests passing per TFM**, Northwind self-test
PASS.

---

## 3. Milestones — REMAINING (to release)

🔴 = critical path (the linchpin and what it unblocks). 🟡 = in progress. 🔵 = ready to start now.

| # | Milestone | Depends on | Notes |
|---|-----------|-----------|-------|
| **M9** 🔴🟡 | **Source Generator (Pillar 3)** — compile-time accessors/metadata + execution plan + `JsonSerializerContext`; removes the `[RequiresUnreferencedCode]` reflection paths (AOT-clean) | M1 | **Phase 1 landed** (`source-generator` spec, D117): incremental generator + shape-driven export accessors for typed Style B views, registered via `[ModuleInitializer]` into a Core `ViewAccessorRegistry`; export pipeline prefers them over reflection (coexistence); `VISTA0001`/`VISTA0002` diagnostics; snapshot + AOT test harness. **Remaining phases:** executable plans/`CompiledView`, member-access for filter/sort, `JsonSerializerContext`, OpenAPI, projection/`MapWritable` DSL analysis, Style A. |
| **M10** | **Style B executable (DR5)** — class-per-view becomes executable (not metadata-only) | M9 | Falls out of M9 |
| **M11** | **D105 — single-source PK auto-derivation** — derive `KeyFields` from `DbContext.Model` at startup | M10 | Consumer (single-source executable views) only exists here |
| **M12** | **Write path / CRUD (DR7)** — Create/Update/Delete, mass-assignment whitelist, concurrency, bulk ops | M10 | Currently returns 501 |
| **M13** | **Masking runtime** — apply `MaskField` transforms on materialization | M10 | Small |
| **M14** | **Observability (D100)** — OpenTelemetry `ActivitySource`/`Meter`/`ILogger`, health checks | M1 | Cross-cutting; parallelizable |
| **M15** | **Versioning & deprecation (D99)** — policy + wire-version seam (route groups as the vehicle) | M4 | Seam already exists |
| **M16** | **More grid adapters** — AG Grid, MudBlazor, OData, Telerik, Syncfusion, TanStack, PrimeNG, GraphQL | M5, M8 | Repetitive once the contract is mature |
| **M17** | **TypeScript client generator** — typed client from `ViewMetadata` | M9 | v1.0 goal |
| **M18** | **OpenAPI/Swagger** — compile-time docs | M9 | v1.0 goal |
| **M19** | **CI workflow** (build + test across net8/9/10) + final NuGet/name availability check | — | Verify `.github/workflows` state |

---

## 4. Mapping to release stages (from `ROADMAP.md`)

### v0.x — Foundation
- Done: **M1–M8**, first reference adapter (**M5**), export (**M7**), metadata schema (**M8**), and
  **M9 source-generator Phase 1** (incremental pipeline + shape-driven export accessors).
- Remaining: the **rest of M9** (executable plans, JSON contexts, the later phases), **M19** (CI).

### v1.0 — Production-ready
- **M11–M15** (write path, masking, D105, observability, versioning), **M17** (TS client),
  **M18** (OpenAPI), and two major adapters from **M16** (AG Grid + MudBlazor).

### v1.x — Ecosystem
- Remaining **M16** adapters, bulk ops, audit log, soft delete, SignalR live updates.

---

## 5. Recommended execution order

The key insight: **do not build the write path on the reflection path** — it would be throwaway work.
The Source Generator (M9) is the linchpin that makes Style B executable, the write path, and the AOT-clean
serialization all fall into place.

```
M9 Source Generator 🔴
     │
     ┌───────────────────┼────────────────────┐
     ▼                    ▼                    ▼
M10 Style B          M14 Observability     M15 Versioning
     │              (parallel)
 ┌───────────┼───────────┐
 ▼           ▼           ▼
M11 D105   M12 Write/CRUD  M13 Masking
                 │
                 ▼
   M16 adapters · M17 TS client · M18 OpenAPI · M19 CI
```

How we keep it fast, integrated, and high-quality:
- **DynData as a behavioral oracle**, not a copy source: map its behavior → derive golden test cases →
  re-implement secure-by-default in Vista.
- **Contracts-first in Core (the hourglass):** EF / AspNetCore / adapters stay thin mappers, so pillars
  progress with low coupling.
- **Vertical slices:** every milestone lands build-green + tests + Northwind self-test — main is never
  left broken.
- **Parallel workstreams** (e.g. observability, docs) can be delegated while the core path proceeds.
- **Single source of truth:** this tracker + `PROJECT-STATUS.md` + the decision log.

---

## 6. Where we are now

**M1–M8 are complete and verified** (build green net8/9/10, Northwind self-test PASS). Pillar 1 and the
full Pillar 2 server half are done; the Pillar 2 client half now has the DataTables adapter, the export
pipeline (CSV/XLSX, pluggable), and the QueryBuilder metadata-schema emitter.

**M9 (Source Generator, Pillar 3) — Phase 1 has landed** (`source-generator` spec, D117): an incremental
generator emits shape-driven field accessors for typed Style B views, registered via a `[ModuleInitializer]`
into a Core `ViewAccessorRegistry` that the export pipeline prefers over reflection (coexistence — nothing
broke). Verified build green net8/9/10, **122 tests/TFM** in `a2n.Vista.Tests` + **4 tests/TFM** in the new
`a2n.Vista.SourceGenerators.Tests`, Northwind self-test (net8.0) PASS, and an AOT-probe build proving the
generated-accessor export path is trim/AOT-clean. This is the major turn that unlocks the rest of Pillar 3
and, in later phases, Style B executable (M10), the write path (M12), masking (M13), the TS client (M17),
and OpenAPI (M18). **Remaining M9 phases:** executable plans/`CompiledView`, member-access for filter/sort,
`JsonSerializerContext`, OpenAPI, projection/`MapWritable` DSL analysis, and Style A coverage.

**Deferred-with-reason:** **M11 (D105)** is intentionally parked until M10 — it only benefits single-source
*executable* views, which do not exist until Style B is executable; explicit `.PrimaryKey()`/`Key(...)`
with registration fail-fast is safe and correct meanwhile.
