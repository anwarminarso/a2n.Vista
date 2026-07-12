# a2n.Vista — Milestones & Roadmap Tracker

> Status: **LIVING DOCUMENT** — update as milestones land.
> Last updated: 2026-07-12 (`source-generator-json-typeinfo` **LANDED**: M9 per-view `JsonTypeInfo` phase
> (M9-P5), D125 (the generated per-view `JsonTypeInfo` provider + Core-resident, serializer-neutral
> `GeneratedJsonContextStore`) + D126 (the seam integration that auto-chains the generated contexts). A
> fourth incremental generator (`ViewJsonContextGenerator`) emits, per covered typed Style B view, a
> reflection-free `IJsonTypeInfoResolver` built via `JsonMetadataServices` (NOT `[JsonSerializable]` — the
> generator-of-generator constraint) for `TRow`/`ViewListResult<TRow>`/`PagedResult<TRow>`/`TCrud` (plus the
> collection/nullable/enum metadata they reach) + a `[ModuleInitializer]` filling the Core store (opaque
> handles → Core stays STJ-free); `a2n.Vista.AspNetCore` drains it into the `TypeInfoResolverChain` ahead of
> the developer context, making `App_Json_Context` **optional**. Mechanism-only (no wire change); parity with
> the reflection oracle is the guard (master Property 1 + round-trip Property 2). Non-blocking diagnostics
> `VISTA0050`/`VISTA0051`. Build green net8/9/10, **281 tests/TFM** + **89 generator tests** (0
> failed/skipped), AOT probe clean on the full typed Style B round-trip with **no developer context and the
> reflection fallback removed**, Northwind read + write self-tests PASS with `NorthwindJsonContext`
> **removed**. See §2 (M9-P5).
> Prior: 2026-07-12 (`source-generator-http-surface` **LANDED**: M9 HTTP-surface phase (M9-P4),
> D123 (the generated dispatch invoker) + D124 (the AOT-clean serialization seam). A third incremental
> generator (`ViewInvokerGenerator`) now emits, per covered typed Style B view, a reflection-free
> `IViewInvoker` + a `[ModuleInitializer]` filling a Core-resident `ViewInvokerStore`, so
> `ViewRequestExecutor` prefers the generated dispatch over `MakeGenericMethod`/`ViewListResult<TRow>`
> reflection (RUC confined to the fallback). AspNetCore gains a unified serialization seam — a
> `TypeInfoResolverChain` over `VistaJson.Options` = shipped `VistaStaticJsonContext` → developer
> `App_Json_Context`(s) via `AddVistaJsonContext(...)` → opt-out reflection fallback — with a shared
> `VistaJsonWriter`; List/Detail/Export and write binding now (de)serialize through it. Core stays
> STJ/EF/HTTP-free. Mechanism-only (no wire change); byte-for-byte parity with the reflection oracle is
> the guard (master Property 1). Non-blocking diagnostics `VISTA0040`/`VISTA0041`. Build green net8/9/10,
> **267 tests/TFM** + **64 generator tests** (0 failed/skipped), AOT probe clean (zero IL2026/IL3050),
> Northwind read + write self-tests PASS — write now reports `ViewInvoker: GENERATED`. See §2.14.
> Prior: 2026-07-09 (`source-generator-write-mapper` **LANDED**: M9 write-DSL phase, D121 + D122.
> A second incremental generator (`WriteMapperGenerator`) now emits a reflection-free `WriteMapper` per
> analyzable typed Style B writable view into the M12 `GeneratedWriteMapperStore` — the write path is now
> AOT-clean for typed Style B (reflection mapper is a fallback only). The interim write-authoring startup
> guards were promoted to build-time diagnostics `VISTA0030`/`VISTA0031`/`VISTA0032` (errors) +
> `VISTA0033` (unanalyzable → warning + reflection fallback). Build green net8/9/10, **206 tests/TFM** +
> 21 generator tests, Northwind write self-test now reports `WriteMapper: GENERATED`.
> Prior: 2026-07-07 (`write-path` **LANDED**: M12, D119 + D120. Writable Style B views now have a
> real Create/Update/Delete facet on `IViewExecutor` (DR8) — the DR7 501 stub is gone. Secure-by-default:
> `MapWritable` mass-assignment whitelist, protected keys/token, optimistic concurrency (`If-Match`/`ETag`),
> server-trusted scope, minimal write responses, and a reflection write mapper behind a fixed-signature
> seam a future source generator fills. Build green net8/9/10, **204 tests/TFM**, Northwind read + write
> self-tests PASS.
> Prior: 2026-07-01 (`style-b-executable` **LANDED**: D118, source-gen Phase 2 = M10 + M11 + M13 —
> executable typed Style B List/Detail via generated `ICompiledViewExecutionPlan`, D105 single-source PK
> auto-derivation, masking runtime; 156 tests/TFM, AOT probe clean))
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
Pillar 1  — View engine            ██████████ 100%   done (read + write/CRUD)
Pillar 2  — server half (engine)   ██████████ 100%   done & hardened
Pillar 2  — client half (adapters) ██░░░░░░░░  ~15%   only DataTables real (+ export + QB schema); 8 grid adapters are empty scaffolds
Pillar 3  — source generator       █████████░  ~88%   Phase 1 (export accessors) + Phase 2 (executable Style B + masking + D105) + write-DSL (generated write mapper) + HTTP-surface (dispatch invoker + serialization seam) + per-view JsonTypeInfo (App_Json_Context now optional)
```

Rough progress toward **v1.0 (production-ready): ~83%**. Foundation, the full server-half query engine,
the HTTP action surface, the first grid adapter, the export pipeline, the QueryBuilder schema emitter,
**source-generator Phase 1** (shape-driven export accessors), **Phase 2** (executable typed Style B via
generated `ICompiledViewExecutionPlan`, single-source PK auto-derivation, masking runtime), the
**write path / CRUD (M12)**, the **generated write mapper (M9 write-DSL, D121/D122)**, the
**HTTP-surface phase (M9-P4, D123/D124)**, and the **per-view `JsonTypeInfo` phase (M9-P5, D125/D126)** are
done — the full typed Style B `request → authorize → execute → serialize` path is now AOT-clean
(IL2026/IL3050-free) **without any developer `App_Json_Context`**. The heavy remaining work is the rest of
the source generator (OpenAPI emitter, Style A coverage) and the ecosystem (more adapters, TS client,
observability, versioning, CI).

> **Adapter reality check (from the source tree):** of the ten projects under `src/Adapters/`, only
> **DataTables.NET** and the **Npgsql dialect** contain real implementations. The other eight
> (`AgGrid`, `MudBlazor`, `OData`, `GraphQL`, `PrimeNG`, `Syncfusion`, `TanStackTable`, `Telerik`) are
> empty scaffolds — `.csproj` + an `AssemblyMarker.cs` only. The `a2n.Vista.Client.TypeScript` generator
> is likewise a stub (`Main => 0`).

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
| **M12** | Write path / CRUD — Create/Update/Delete on the `IViewExecutor` write facet (DR8), replacing the DR7 501 stub. `MapWritable` mass-assignment whitelist (default-deny), protected keys + concurrency token, optimistic concurrency (`If-Match`/`ETag`), server-trusted scope, single `SaveChanges`, minimal write responses, RFC 7807 write-error vocabulary. Reflection write mapper behind a fixed-signature seam (`WriteMapperResolver`) the future generated mapper fills; `[RequiresUnreferencedCode]` confined to the reflection branch. Bulk deferred (array body → 400). | `write-path` | D119, D120 |
| **M9-P3** | Source Generator, write-DSL phase — the **generated write mapper**. A second `IIncrementalGenerator` (`WriteMapperGenerator`) statically analyzes each analyzable typed Style B writable view's `MapWritable` chain and emits a reflection-free `WriteMapper` (`Action<object,object>` = casts + one whitelisted scalar assignment per safe mapping, declaration-ordered, defense-in-depth) + a `[ModuleInitializer]` filling the M12 `GeneratedWriteMapperStore`; `WriteMapperResolver` prefers it over `ReflectionWriteMapper` with **zero executor changes** → typed Style B write is now AOT-clean. Interim startup guards promoted to build-time diagnostics `VISTA0030`/`0031`/`0032` (errors, gate emission) + `VISTA0033` (unanalyzable → warning + reflection fallback). | `source-generator-write-mapper` | D121, D122 |
| **M9-P4** | Source Generator, HTTP-surface phase — closes the last large reflection surface for typed Style B. **D123:** a third `IIncrementalGenerator` (`ViewInvokerGenerator`) emits, per covered view, a Core-only reflection-free `IViewInvoker` (closes `ListAsync<TRow>`/`DetailAsync<TRow>`/`CreateAsync<TCrud>`/`UpdateAsync<TCrud>` at compile time, awaits directly — no `MakeGenericMethod`/`Task.Result`/`ViewListResult<TRow>` reflection) + a `[ModuleInitializer]` filling a Core-resident, first-wins `ViewInvokerStore`; `ViewRequestExecutor` prefers it and confines `[RequiresUnreferencedCode]` to private `*ReflectionAsync` fallback helpers (the executor read facets' RUC was relaxed accordingly). **D124:** an AOT-clean serialization seam in AspNetCore — a `TypeInfoResolverChain` over `VistaJson.Options` (shipped `VistaStaticJsonContext` → developer `App_Json_Context`(s) via `AddVistaJsonContext(...)` → opt-out `DefaultJsonTypeInfoResolver` fallback), a reflection-free `FilterNodeJsonConverter`, and a shared `VistaJsonWriter`; List/Detail/Export responses and `VistaWriteBinding` now (de)serialize through it (byte-for-byte parity). Core stays STJ/EF/HTTP-free. Non-blocking diagnostics `VISTA0040` (uncovered candidate) + `VISTA0041` (serialization guidance). Per-view `JsonTypeInfo` auto-generation is a **non-goal** here (generator-of-generator constraint) — deferred. | `source-generator-http-surface` | D123, D124 |
| **M9-P5** | Source Generator, per-view `JsonTypeInfo` phase — makes serialization self-service for typed Style B, so the developer `App_Json_Context` is **optional**. **D125:** a fourth `IIncrementalGenerator` (`ViewJsonContextGenerator`) emits, per covered view, a reflection-free `file sealed IJsonTypeInfoResolver` built by hand via `JsonMetadataServices` (NOT `[JsonSerializable]` — the generator-of-generator constraint) providing the `JsonTypeInfo` for `TRow`/`ViewListResult<TRow>`/`PagedResult<TRow>`/`TCrud` **plus** the collection/nullable/enum/leaf metadata those DTOs reach (so a covered app resolves with no reflection fallback); a `[ModuleInitializer]` fills a new Core-resident, serializer-neutral `GeneratedJsonContextStore` (opaque `object` handles → Core stays STJ-free). **D126:** `a2n.Vista.AspNetCore` drains the store and chains each generated context into the `TypeInfoResolverChain` ahead of the developer `App_Json_Context`(s) and the reflection fallback (keeping `VistaStaticJsonContext` first) — no seam/invoker/API change. Non-blocking diagnostics `VISTA0050` (covered) + `VISTA0051` (non-emittable member → fallback). OpenAPI and Style A coverage remain the last M9 phases. | `source-generator-json-typeinfo` | D125, D126 |

**Verified at M9-P5 (D125/D126, 2026-07-12):** solution build green on net8/9/10, **281 tests passing per
TFM** in `a2n.Vista.Tests` + **89** in `a2n.Vista.SourceGenerators.Tests` (0 failed, 0 skipped), AOT probe
clean on the full typed Style B round-trip with **no developer `App_Json_Context` and the reflection
fallback removed**, Northwind **read + write** self-tests PASS with the developer `NorthwindJsonContext`
**removed** — now exercising the generated per-view serialization. The master oracle-parity property
(Property 1) + the mandatory round-trip (Property 2) prove the generated `JsonTypeInfo` (de)serializes
byte-for-byte / value-equivalently to the reflection oracle.

**Verified at M9-P4 (D123/D124, 2026-07-12):** solution build green on net8/9/10 (0 warnings besides a
pre-existing unrelated CS8619 in the DataTablesNet adapter), **267 tests passing per TFM** in
`a2n.Vista.Tests` + **64** in `a2n.Vista.SourceGenerators.Tests` (0 failed, 0 skipped), AOT probe clean
(zero IL2026/IL3050 on the full generated Style B HTTP round-trip), Northwind **read + write** self-tests
PASS — the write self-test now reports `ViewInvoker: GENERATED (source generator)` alongside `WriteMapper:
GENERATED`. The master oracle-parity property (Property 1) proves the generated dispatch + serialization
is byte-for-byte equivalent to the reflection path for every request shape.

**Verified at M9-P3 (D121/D122, 2026-07-09):** solution build green on net8/9/10 (0 warnings), **206
tests passing per TFM** in `a2n.Vista.Tests` + **21** in `a2n.Vista.SourceGenerators.Tests` (0 failed, 0
skipped), Northwind **read + write** self-tests PASS — the write self-test now reports `WriteMapper:
GENERATED (source generator)`, confirming the generated mapper is live and parity-equivalent to the
reflection oracle.

**Verified at M12 (D119/D120, 2026-07-07):** solution build green on net8/9/10, **204 tests passing per
TFM** (0 failed, 0 skipped), Northwind **read + write** self-tests PASS (Create/Update/Delete, 0 failed
operations). Writable Style B endpoints now execute the write facet — the **501** stub is gone (R16.6).

**Verified at M10/M11/M13 (D118, 2026-07-01):** solution build green on net8/9/10, **156 tests passing
per TFM**, Northwind self-test PASS, AOT probe clean (zero IL2026/IL3050 on the generated Style B
List/Detail path).

---

## 3. Milestones — REMAINING (to release)

🔴 = critical path (the linchpin and what it unblocks). 🟡 = in progress. 🔵 = ready to start now.

> **`source-generator-json-typeinfo` (M9-P5, D125/D126) has LANDED** — the generated per-view
> `JsonTypeInfo` provider + seam auto-chaining make the developer `App_Json_Context` **optional**, so an app
> of covered typed Style B views is AOT-clean for serialization with no hand-authored context. Earlier M9
> phases also landed: **HTTP-surface** (`source-generator-http-surface`, D123/D124 — the dispatch invoker +
> serialization seam), **write-DSL** (`source-generator-write-mapper`, D121/D122 — the generated write
> mapper), **Phase 2** (`style-b-executable`, D118 — **M10 + M11 + M13**), and **Phase 1**
> (`source-generator`, D117 — export accessors); **`write-path` (M12, D119/D120)** delivered the write
> facet. Typed Style B views are now fully executable read + write **and** AOT-clean end to end — including
> serialization — with no developer serialization context. The last M9 phases are the **OpenAPI emitter**
> and **Style A (anonymous) coverage**.

| # | Milestone | Depends on | Notes |
|---|-----------|-----------|-------|
| **M9** 🟡 | **Source Generator (Pillar 3)** — compile-time accessors/metadata + execution plan + write mapper + HTTP dispatch/serialization + per-view `JsonTypeInfo`; removes the `[RequiresUnreferencedCode]` reflection paths (AOT-clean) | M1 | **Phase 1 landed** (`source-generator`, D117): incremental generator + shape-driven export accessors. **Phase 2 landed** (`style-b-executable`, D118): executable typed Style B plans (member-access, typed sort appliers), masking runtime, D105 PK derivation. **Write-DSL phase landed** (`source-generator-write-mapper`, D121/D122): the generated write mapper + build-time diagnostics. **HTTP-surface phase landed** (`source-generator-http-surface`, D123/D124): the generated dispatch invoker (`IViewInvoker` + Core `ViewInvokerStore`) + AOT-clean serialization seam — the full typed Style B HTTP round-trip is now IL2026/IL3050-clean. **Per-view `JsonTypeInfo` phase landed** (`source-generator-json-typeinfo`, D125/D126): the generated per-view `IJsonTypeInfoResolver` (via `JsonMetadataServices`) + Core `GeneratedJsonContextStore` + seam auto-chaining — the developer `App_Json_Context` is now optional (see M9-P5 in §2). **Remaining phases:** OpenAPI emitter, Style A (anonymous) coverage. |
| **M14** | **Observability (D100)** — OpenTelemetry `ActivitySource`/`Meter`/`ILogger`, health checks | M1 | Cross-cutting; parallelizable |
| **M15** | **Versioning & deprecation (D99)** — policy + wire-version seam (route groups as the vehicle) | M4 | Seam already exists |
| **M16** | **More grid adapters** — AG Grid, MudBlazor, OData, Telerik, Syncfusion, TanStack, PrimeNG, GraphQL | M5, M8 | Repetitive once the contract is mature |
| **M17** | **TypeScript client generator** — typed client from `ViewMetadata` | M9-P5 | v1.0 goal (consumes the serialization seam + generated per-view contexts + AOT-clean surface) |
| **M18** | **OpenAPI/Swagger** — compile-time docs | M9-P5 | v1.0 goal (consumes the metadata + generated JSON contexts) |
| **M19** 🔵 | **CI workflow** (build + test across net8/9/10) + final NuGet/name availability check | — | Verify `.github/workflows` state |

> **Landed (was remaining):** **M10** Style B executable, **M11** D105 single-source PK auto-derivation,
> **M13** masking runtime (all `style-b-executable`, D118), **M12** write path / CRUD
> (`write-path`, D119/D120), **M9-P3** the generated write mapper
> (`source-generator-write-mapper`, D121/D122), and **M9-P4** the HTTP-surface phase — generated dispatch
> invoker + AOT-clean serialization seam (`source-generator-http-surface`, D123/D124), and **M9-P5** the
> per-view `JsonTypeInfo` phase — generated per-view contexts + seam auto-chaining making the developer
> `App_Json_Context` optional (`source-generator-json-typeinfo`, D125/D126). See §2. **M17** (TS client) and
> **M18** (OpenAPI) are now fully unblocked on the AOT-clean surface + the generated serialization contexts;
> the remaining M9 phases are the OpenAPI emitter and Style A coverage.
>
> **Bulk write ops** remain deferred to a later phase (v1.x): a bulk/array body is currently rejected with
> HTTP 400, and the `AllowBulk` authoring flag enables no execution path yet (Requirement 15).

---

## 4. Mapping to release stages (from `ROADMAP.md`)

### v0.x — Foundation
- Done: **M1–M8**, first reference adapter (**M5**), export (**M7**), metadata schema (**M8**),
  **M9 source-generator Phase 1** (export accessors), **M10/M11/M13** via source-generator Phase 2
  (executable Style B, D105 PK auto-derivation, masking runtime), **M9-P3** the source-generator
  write-DSL phase (generated write mapper, D121/D122), **M9-P4** the source-generator HTTP-surface
  phase (dispatch invoker + serialization seam, D123/D124), and **M9-P5** the source-generator per-view
  `JsonTypeInfo` phase (generated per-view contexts + seam auto-chaining → `App_Json_Context` optional,
  D125/D126).
- Remaining: the **rest of M9** (OpenAPI emitter, Style A coverage), **M19** (CI).

### v1.0 — Production-ready
- **M14–M15** (observability, versioning), **M17** (TS client), **M18** (OpenAPI), and two major adapters
  from **M16** (AG Grid + MudBlazor). (**M11/M13** landed with D118; **M12** write path landed with
  D119/D120.)

### v1.x — Ecosystem
- Remaining **M16** adapters, bulk ops, audit log, soft delete, SignalR live updates.

---

## 5. Recommended execution order

The key insight: **do not build the write path on the reflection path** — it would be throwaway work.
The Source Generator (M9) is the linchpin that makes Style B executable, the write path, and the AOT-clean
serialization all fall into place.

```
M9 Source Generator 🟡
     │  Phase 1 ✓ (export accessors)   Phase 2 ✓ (executable Style B + D105 + masking)
     │  write-DSL ✓ (generated write mapper, D121/D122)
     │  M9-P4 ✓ HTTP-surface (dispatch invoker + serialization seam, D123/D124)
     │  M9-P5 ✓ per-view JsonTypeInfo (App_Json_Context optional, D125/D126)
     │  remaining: OpenAPI emitter · Style A coverage
     ┌───────────────────┼────────────────────┐
     ▼                    ▼                    ▼
M10 Style B ✓        M14 Observability     M15 Versioning
     │              (parallel)
 ┌───────────┼───────────┐
 ▼           ▼           ▼
M11 D105 ✓  M12 Write/CRUD ✓  M13 Masking ✓
                 │
                 ▼ (write mapper now generated ✓)
   M16 adapters · M19 CI  ·  M9-P5 ✓ ─▶ M17 TS client · M18 OpenAPI
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

**M9 (Source Generator, Pillar 3) — Phase 1, Phase 2, AND the write-DSL phase have landed.**
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

**Write-DSL phase** (`source-generator-write-mapper`, D121/D122, 2026-07-09): a second
`IIncrementalGenerator` (`WriteMapperGenerator`) statically analyzes each analyzable typed Style B writable
view's `MapWritable` chain and emits a reflection-free `WriteMapper` + `[ModuleInitializer]` into the M12
`GeneratedWriteMapperStore`; `WriteMapperResolver` prefers it over the reflection mapper with zero executor
changes, so the typed Style B write path is now AOT-clean. The interim write-authoring startup guards are
now build-time diagnostics (`VISTA0030`/`0031`/`0032` errors + `VISTA0033` fallback warning). Verified
build green net8/9/10 (0 warnings), 206 tests/TFM + 21 generator tests, Northwind write self-test reports
`WriteMapper: GENERATED`.

**HTTP-surface phase** (`source-generator-http-surface`, D123/D124, 2026-07-12): a third
`IIncrementalGenerator` (`ViewInvokerGenerator`) emits a Core-only reflection-free `IViewInvoker` +
`[ModuleInitializer]` per covered typed Style B view into a Core-resident `ViewInvokerStore`;
`ViewRequestExecutor` prefers it over `MakeGenericMethod`/`ViewListResult<TRow>` reflection with the RUC
confined to private `*ReflectionAsync` fallbacks (D123). AspNetCore gains a unified serialization seam — a
`TypeInfoResolverChain` over `VistaJson.Options` (shipped `VistaStaticJsonContext` → developer
`App_Json_Context`(s) via `AddVistaJsonContext(...)` → opt-out reflection fallback), a reflection-free
`FilterNodeJsonConverter`, and a shared `VistaJsonWriter`; List/Detail/Export + write binding
(de)serialize through it (D124). Core stays STJ/EF/HTTP-free; mechanism-only (no wire change), parity with
the reflection oracle proven byte-for-byte (master Property 1). Non-blocking diagnostics
`VISTA0040`/`VISTA0041`. Verified build green net8/9/10, **267 tests/TFM** + 64 generator tests, AOT probe
clean, Northwind read + write self-tests PASS (`ViewInvoker: GENERATED`).

**Per-view `JsonTypeInfo` phase** (`source-generator-json-typeinfo`, D125/D126, 2026-07-12): a fourth
`IIncrementalGenerator` (`ViewJsonContextGenerator`) emits, per covered typed Style B view, a
reflection-free `IJsonTypeInfoResolver` built by hand via `JsonMetadataServices` (NOT `[JsonSerializable]`
— the generator-of-generator constraint) providing the `JsonTypeInfo` for `TRow`/`ViewListResult<TRow>`/
`PagedResult<TRow>`/`TCrud` plus the collection/nullable/enum/leaf metadata those DTOs reach + a
`[ModuleInitializer]` filling a new Core-resident, serializer-neutral `GeneratedJsonContextStore` (opaque
`object` handles → Core stays STJ-free). `a2n.Vista.AspNetCore` drains the store and chains each generated
context into the `TypeInfoResolverChain` ahead of the developer `App_Json_Context`(s) and the reflection
fallback, so the developer context is now **optional** — an app of covered typed Style B views is AOT-clean
for serialization with no hand-authored context. Non-blocking diagnostics `VISTA0050`/`VISTA0051`. Verified
build green net8/9/10, **281 tests/TFM** + 89 generator tests, AOT probe clean on the full round-trip with
no developer context and the reflection fallback removed, Northwind read + write self-tests PASS with
`NorthwindJsonContext` removed.

**Remaining M9 phases:** the OpenAPI emitter and Style A (anonymous) serialization coverage (permanently
RUC by D96).

**M12 (`write-path`, D119/D120) has LANDED (2026-07-07).** Writable Style B views execute
Create/Update/Delete through the `IViewExecutor` write facet (DR8): a default-deny `MapWritable`
mass-assignment whitelist, protected keys + concurrency token, optimistic concurrency via
`If-Match`/`ETag`, server-trusted scope enforcement, a single `SaveChanges` per operation, minimal
(PK-only) write responses, and an RFC 7807 write-error vocabulary. The `TCrud → entity` mapping runs
through a reflection mapper today, behind a fixed-signature seam (`WriteMapperResolver`) that a future M9
write-DSL phase fills with a generated mapper — zero executor changes at that point;
`[RequiresUnreferencedCode]` is confined to the reflection branch. Bulk ops are deferred (array body →
400). Verified build green net8/9/10, **204 tests/TFM**, Northwind write self-test PASS.

**M9-P5 (`source-generator-json-typeinfo`, D125/D126) has LANDED (2026-07-12).** Serialization is now
self-service for typed Style B — the developer `App_Json_Context` is optional. The full typed Style B HTTP
round-trip (dispatch **and** serialization) is AOT-clean with no hand-authored context; the reflection
dispatch/serialization path is a permanent fallback only (Style A, anonymous/`object` row types, or
non-emittable/unanalyzable views).

**Next up — the remaining M9 phases and parallelizable v1.0 workstreams:** the OpenAPI emitter and Style A
coverage; plus **M14** observability, **M15** versioning, **M19** CI, and the two flagship **M16** adapters
(AG Grid + MudBlazor) — the eight adapter scaffolds under `src/Adapters/` are still empty. **M17** TS client
and **M18** OpenAPI are now fully unblocked on the AOT-clean surface, the serialization seam (M9-P4), and
the generated per-view contexts (M9-P5).
