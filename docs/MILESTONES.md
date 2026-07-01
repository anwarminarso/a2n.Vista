# a2n.Vista — Milestones & Roadmap Tracker

> Status: **LIVING DOCUMENT** — update as milestones land.
> Last updated: 2026-07-01 (`style-b-executable` **LANDED**: D118, source-gen Phase 2 = M10 + M11 + M13.
> Typed Style B views are now executable (List/Detail) via generated `ICompiledViewExecutionPlan`; D105
> single-source PK auto-derivation + masking runtime landed. Build green net8/9/10, 156 tests/TFM,
> Northwind self-test PASS, AOT probe clean)
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
Pillar 3  — source generator       █████░░░░░  ~45%   Phase 1 (export accessors) + Phase 2 (executable Style B + masking + D105)
```

Rough progress toward **v1.0 (production-ready): ~62%**. Foundation, the full server-half query engine,
the HTTP action surface, the first grid adapter, the export pipeline, the QueryBuilder schema emitter,
**source-generator Phase 1** (shape-driven export accessors) and **Phase 2** (executable typed Style B
via generated `ICompiledViewExecutionPlan`, single-source PK auto-derivation, masking runtime) are done.
The heavy remaining work is the write path (CRUD), the rest of the source generator (JSON contexts,
OpenAPI, Style A), and the ecosystem (more adapters, TS client).

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
| **M9-P1** | Source Generator, Phase 1 — incremental generator + shape-driven export accessors for typed Style B views (`[ModuleInitializer]` → `ViewAccessorRegistry`, coexists with reflection) | `source-generator` | D117 |
| **M10** | Style B executable — typed class-per-view becomes executable (List/Detail) via generated AOT-clean `ICompiledViewExecutionPlan` (compile-time projection, member-access, typed sort appliers); `AddVista` adopts it (**DR5 closed for typed views**) | `style-b-executable` | D118 |
| **M11** | D105 — single-source PK auto-derivation from `DbContext.Model` at startup (`VistaModelKeyDerivationService`); never overrides declared keys; fails closed otherwise | `style-b-executable` | D118 (D105) |
| **M13** | Masking runtime — apply `MaskField` transforms on materialization (post-projection, in-memory, SQL unchanged), fail-closed; AOT-clean generated `MaskAccessor` + RUC fallback | `style-b-executable` | D118 |

**Verified at M10/M11/M13 (D118, 2026-07-01):** solution build green on net8/9/10, **156 tests passing
per TFM**, Northwind self-test PASS, AOT probe clean (zero IL2026/IL3050 on the generated Style B
List/Detail path). Writable Style B endpoints still return **501** (write path = M12, unbuilt).

---

## 3. Milestones — REMAINING (to release)

🔴 = critical path (the linchpin and what it unblocks). 🟡 = in progress. 🔵 = ready to start now.

> **`style-b-executable` (D118) has LANDED** — source-generator **Phase 2** delivered **M10 + M11 + M13**
> over one shared materialization + execution-plan seam. Typed Style B views are now executable
> (List/Detail) through the generated `ICompiledViewExecutionPlan`; single-source PK auto-derivation
> (D105) and the masking runtime are live. The **write path (M12)** remains out of scope — write endpoints
> still return **501**.

| # | Milestone | Depends on | Notes |
|---|-----------|-----------|-------|
| **M9** 🔴🟡 | **Source Generator (Pillar 3)** — compile-time accessors/metadata + execution plan + `JsonSerializerContext`; removes the `[RequiresUnreferencedCode]` reflection paths (AOT-clean) | M1 | **Phase 1 landed** (`source-generator`, D117): incremental generator + shape-driven export accessors. **Phase 2 landed** (`style-b-executable`, D118): executable typed Style B plans (member-access, typed sort appliers), masking runtime, D105 PK derivation. **Remaining phases:** `JsonSerializerContext`, OpenAPI, projection/`MapWritable` DSL analysis for the write path, Style A (anonymous) coverage. |
| **M12** | **Write path / CRUD (DR7)** — Create/Update/Delete, mass-assignment whitelist, concurrency, bulk ops | M10 ✓ | Separate spec; currently returns 501 (unchanged by D118). **Now unblocked** — M10 is done. |
| **M14** | **Observability (D100)** — OpenTelemetry `ActivitySource`/`Meter`/`ILogger`, health checks | M1 | Cross-cutting; parallelizable |
| **M15** | **Versioning & deprecation (D99)** — policy + wire-version seam (route groups as the vehicle) | M4 | Seam already exists |
| **M16** | **More grid adapters** — AG Grid, MudBlazor, OData, Telerik, Syncfusion, TanStack, PrimeNG, GraphQL | M5, M8 | Repetitive once the contract is mature |
| **M17** | **TypeScript client generator** — typed client from `ViewMetadata` | M9 | v1.0 goal |
| **M18** | **OpenAPI/Swagger** — compile-time docs | M9 | v1.0 goal |
| **M19** 🔵 | **CI workflow** (build + test across net8/9/10) + final NuGet/name availability check | — | Verify `.github/workflows` state |

> **Landed (was remaining):** **M10** Style B executable, **M11** D105 single-source PK auto-derivation,
> **M13** masking runtime — all delivered by `style-b-executable` (D118). See §2.

---

## 4. Mapping to release stages (from `ROADMAP.md`)

### v0.x — Foundation
- Done: **M1–M8**, first reference adapter (**M5**), export (**M7**), metadata schema (**M8**),
  **M9 source-generator Phase 1** (export accessors), and **M10/M11/M13** via source-generator Phase 2
  (executable Style B, D105 PK auto-derivation, masking runtime).
- Remaining: the **rest of M9** (JSON contexts, OpenAPI, Style A, write-path DSL analysis), **M19** (CI).

### v1.0 — Production-ready
- **M12** (write path), **M14–M15** (observability, versioning), **M17** (TS client), **M18** (OpenAPI),
  and two major adapters from **M16** (AG Grid + MudBlazor). (**M11/M13** already landed with D118.)

### v1.x — Ecosystem
- Remaining **M16** adapters, bulk ops, audit log, soft delete, SignalR live updates.

---

## 5. Recommended execution order

The key insight: **do not build the write path on the reflection path** — it would be throwaway work.
The Source Generator (M9) is the linchpin that makes Style B executable, the write path, and the AOT-clean
serialization all fall into place.

```
M9 Source Generator 🔴
     │  Phase 1 ✓ (export accessors)   Phase 2 ✓ (executable Style B + D105 + masking)
     ┌───────────────────┼────────────────────┐
     ▼                    ▼                    ▼
M10 Style B ✓        M14 Observability     M15 Versioning
     │              (parallel)
 ┌───────────┼───────────┐
 ▼           ▼           ▼
M11 D105 ✓  M12 Write/CRUD  M13 Masking ✓
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

**M9 (Source Generator, Pillar 3) — Phase 1 AND Phase 2 have landed.**
- **Phase 1** (`source-generator`, D117): an incremental generator emits shape-driven field accessors for
  typed Style B views, registered via a `[ModuleInitializer]` into a Core `ViewAccessorRegistry` the
  export pipeline prefers over reflection (coexistence — nothing broke).
- **Phase 2** (`style-b-executable`, D118, 2026-07-01): a second emitter produces an AOT-clean
  `ICompiledViewExecutionPlan` per typed Style B view (compile-time projection, per-field member-access,
  strongly-typed sort appliers, masked-field accessors) registered into `GeneratedExecutionPlanStore`;
  `AddVista` adopts it so `EfViewExecutor` runs List/Detail through a non-RUC compiled path (**DR5 closed
  for typed views**). Bundled M11 (D105 single-source PK auto-derivation at startup) and M13 (masking
  runtime on materialization, fail-closed, SQL unchanged). Verified build green net8/9/10, **156 tests/TFM**,
  Northwind self-test PASS, AOT probe clean. Writable Style B endpoints still return **501**.

**Remaining M9 phases:** `JsonSerializerContext`, OpenAPI, projection/`MapWritable` DSL analysis (for the
write path), and Style A (anonymous) coverage.

**Next up — `write-path` (M12, DR7).** Now unblocked by M10: Create/Update/Delete currently return 501.
It needs `TCrud → entity` mapping (reflection now, source-gen later), the mass-assignment whitelist,
concurrency, SaveChanges, and bulk ops. Parallelizable workstreams (**M14** observability, **M15**
versioning, **M19** CI) can proceed alongside.
