# a2n.Vista — Project Status & Session Handoff

> Status: **LIVING DOCUMENT** — update as work proceeds.
> Last updated: 2026-07-31 (**audit remediation, tranche 6** — `PERF-03` the XLSX worksheet now streams into its
> archive entry one row at a time (peak memory was ~2× the document in two LOH buffers; byte output unchanged),
> `DEAD-09` the real accessor-map escaping drift is closed behind one shared literal writer (goldens unchanged;
> its live impact was nil, since a member name cannot contain a quote), and **`PERF-06` is declined as
> specified** — the proposed `CompilationProvider` hoist is the incremental-generator anti-pattern and would
> invalidate every candidate on every keystroke. See §2.27. Remaining: the `DEAD-01`/`DEAD-03`/`DEAD-08` scope
> calls, `DEAD-07`/R12.2, the deferred generator dedup, and `PERF-08` (needs a `ViewQueryRequest` decision).)
> Prior: 2026-07-31 (**audit remediation, tranche 5** — the `DEAD-*` batch turned into a **method
> correction**: the audit's dead-code section established which members are *unreferenced*, never cross-checking
> `.kiro/specs/*/requirements.md`, and that mislabelled a required-but-unimplemented feature as dead code. The
> report now carries the three-way test to apply first. Landed: **D149** display-format metadata (server
> publishes, client applies — `.Format("N2")` was silent data loss; additive, `/metadata` byte-identical when
> unset) and **`DEAD-06`** `RegisterAssembly` now registers on the same terms as `Register<TView>()`, with its
> first test coverage. Reverted after cross-check and now open **scope calls**: `DEAD-07` (finding withdrawn —
> openapi-emitter **R12.2** requires an adapter-documentation extension point that is unimplemented), plus
> `DEAD-01`/`DEAD-03`/`DEAD-08`. See §2.26. Remaining: those scope calls, `DEAD-09`, `PERF-03`/`06`/`08`.)
> Prior: 2026-07-31 (**audit remediation, tranche 4** — the metadata/authoring/read-path findings:
> **D147** every Vista read is now `AsNoTracking` (a tracked entity-bearing projection could let a later
> `SaveChanges` persist a *mask* over real data) and the reflection mask is non-destructive, so an anonymous
> Style A row is maskable at all for the first time; **D148** `ViewMetadata` equality/hash are hand-written
> over declarative content (the synthesized record equality compared a per-instance lock object, so two
> identical snapshots were never equal); plus `PERF-04` — `Configure` now runs **once** per view against one
> cached builder, so metadata, masks, the write facet, and row filters all come from the same authoring
> result. See §2.25. Remaining open audit items: the `DEAD-*` API removals and `PERF-03`/`06`/`08`.)
> Prior: 2026-07-31 (**audit remediation, tranche 3** — the three contract-free caching findings are
> fixed: **`PERF-05`** one shared `ViewFieldLookup.For(view)` replaces four per-call field-lookup builders,
> **`PERF-02`** the export reflection fallback memoizes its member lookup instead of resolving it per cell,
> **`PERF-07`** the metadata projection, payload, and `ETag` are computed once per view so a 304 is one string
> comparison. Pure memoization: **no decision needed, no contract/route/envelope/error change**; every cache is
> a reference-keyed `ConditionalWeakTable`. See §2.24. Remaining open audit items: `BUG-07`, `BUG-10`, the
> `DEAD-*` API removals, and `PERF-03`/`04`/`06`/`08` — each needs a design change, not just caching.)
> Prior: 2026-07-31 (**audit remediation, tranche 2** — the decision-bearing findings are settled and
> implemented: **D143** masked fields default non-sortable (closes the `ORDER BY` + paging probing vector),
> **D144** paging carries an absolute `Offset` so the page-size clamp can no longer move a grid's window,
> **D145** the endpoint authorizes before it binds (no more `428`/`400` to an unauthorized caller), **D146**
> a declared concurrency token must be model-backed, the database performs the atomic check, and the update
> `ETag` is the post-write token (a delete emits none). See §2.23. Remaining open audit items: `BUG-07`,
> `BUG-10`, the `DEAD-*` API removals, and `PERF-02`–`PERF-08`.)
> Prior: 2026-07-31 (**audit remediation, tranche 1** — the self-contained findings of
> `docs/audit/2026-07-31-full-code-audit.md` are fixed with a regression test each; two behaviour-visible
> defaults changed: **D141** Style A row-level security now fails closed when the request scope carries a
> filter it cannot apply pre-projection (`IViewScope.RowFilterCount` added), and **D142** the OpenAPI document
> endpoint is authorized by default (skipped under the D94 anonymous opt-in or an explicit opt-out). See
> §2.22 for the fixed/open split; the report carries the per-finding table.)
> Prior: 2026-07-14 (`northwind-sample-showcase` **LANDED** — D137–D140, purely additive at the
> sample/example layer (no Core/EF/AspNetCore/adapter contract, route, envelope, or error change). The
> `a2n.Vista.Examples.AgGridNorthwind` host became a **three-page showcase** behind a shared nav, reaching
> feature parity with the legacy DynData "Table Browser" on the read surface. **D137** — the single
> `AgGridNorthwind` host serves all three pages and registers `DataTablesAdapter` +
> `QueryBuilderSchemaAdapter` + `AgGridAdapter` + the OpenAPI emitter, keeping `AllowAnonymousAccess()`
> (D94); the standalone `Northwind` host stays the separate DataTables-only single-view sample (this revised
> the earlier D137 draft that had proposed extending the `Northwind` host). **D138** — an additive read-only
> catalog endpoint `GET /api/showcase/views` (a pure `ShowcaseCatalog.Project(IViewRegistry)` → `[]` on an
> empty registry) supplies the browsable-view list, secure-by-default (only registered views), inside the
> host auth pipeline — there was no HTTP "list all views" endpoint before. **D139** — static HTML +
> TypeScript compiled by `tsc` (no bundler), a `tsc --noEmit` gate, and fast-check property tests for the
> pure transforms (`columns.ts` metadata→columns, `search.ts` min-length gate). **D140** — a third read-only
> view `vOrder` so the registered set (`vProductCategory`/`vOrderDetail`/`vOrder`) spans
> string/numeric/date/FK/composite-key. Three pages: **Simple Wiring** (AG Grid infinite row model →
> `POST {route}/aggrid`, `?q=`), **View Browser** (DataTables.NET + jQuery-QueryBuilder — view selection,
> dynamic columns from `GET {route}/metadata`, server-side paging + min-length global search + single/multi
> sort + a `GET {route}/querybuilder`-driven advanced filter through `POST {route}/datatable`), and **Custom
> Renderer** (consumer-owned community `cellRenderer`s, presentation-only). The old standalone
> `AgGridSelfTest` was removed when the showcase took over the host; the host self-test gained a view-browser
> round-trip (paging + global search + multi-sort + `jsonQB` in one request). Build green net8/9/10, **516
> tests/TFM (net8) / 517 tests/TFM (net9/net10)** in `a2n.Vista.Tests` (+1 — the `ShowcaseCatalog` CsCheck
> Property 2; Properties 1 & 3 are fast-check under Node) + **112 generator tests** unchanged (0
> failed/skipped), the showcase host read + write + OpenAPI self-tests PASS (view-browser round-trip
> included; 19 OpenAPI paths, 4 views). See §2.21.
> Prior: 2026-07-14 (`ag-grid-adapter` **LANDED** — **M16**, the **second** Pillar 2 client-half grid
> adapter: `a2n.Vista.Adapters.AgGrid` (Core-only, D48), D133–D136. A new `AgGridAdapter :
> ViewAdapter<AgGridRowsRequest, AgGridRowsResponse>` implements the three pure mapping steps against the
> **landed** neutral contract (no Core/EF/AspNetCore type added or changed): **D133** — `Id="aggrid"` +
> `RouteSuffix="aggrid"` → exposed at `POST {route}/aggrid` through the **existing** DataTables glue
> verbatim; **D134** — the pure `AgGridFilterModelParser` maps the AG Grid `filterModel` (text/number/date/set
> + combined AND/OR) to a `FilterNode` per a locked table, Advanced Filter deferred for v1 (rejected loudly →
> `AdapterBindException` → 400 `adapter-bind-failed`, never silently dropped); **D135** — block paging
> (`PageSize = EndRow - StartRow`, `Page = StartRow / PageSize`; non-positive pass-through so the engine
> rejects it) and the `{rowData, rowCount}` `LoadSuccessParams` response (`rowCount = RecordsFiltered` for
> last-block detection, `RecordsTotal` not surfaced); **D136** — quick-filter transport via `?q=` folded into
> `AdapterRequest.Values` (no host change) + a thin hand-written `IServerSideDatasource` for the sample (the
> M17 generated client is OpenAPI-driven and adapter endpoints are not yet in the document). `filterModel`
> lands only in the `Filter` channel, quick filter only in `Search`; the adapter never enforces the
> tri-whitelist (per-channel engine validation, D111). Request POCOs (de)serialize through a source-gen
> `AgGridJsonContext` (AOT-clean; anonymous Style A rows ride the documented D96 RUC path — no new reflection
> path). Ships an `a2n.Vista.Examples.AgGridNorthwind` sample (net8.0-only): ASP.NET host + AG Grid + TS
> front-end (`tsc --noEmit` gate) + a guarded `dotnet run -- selftest` round-trip. Additive-only (no server
> route/wire/behavior change). Build green net8/9/10, **515 tests/TFM (net8) / 517 tests/TFM (net9/net10)** in
> `a2n.Vista.Tests` (+67/TFM, 8 PBT properties ≥100 iters + unit/glue-integration) + **112 generator tests**
> unchanged (0 failed/skipped), Northwind read + write + OpenAPI self-tests PASS unchanged **and** the new AG
> Grid sample self-test PASSES. See §2.20.
> Prior: 2026-07-14 (**M19 LANDED** — the CI + NuGet publish workflows under `.github/workflows/`.
> `ci.yml` restores + builds the full solution (`src/a2n.Vista.slnx`) in Release, then runs the three TUnit
> suites (`a2n.Vista.Tests`, `a2n.Vista.SourceGenerators.Tests`, `a2n.Vista.Client.TypeScript.Tests`) via
> `dotnet run --project … --framework <tfm>` (not `dotnet test`, per the repo convention) over a
> **net8.0/net9.0/net10.0** matrix, on `push`/`pull_request` to `main` + `workflow_dispatch`. `publish.yml`
> packs + pushes to nuget.org on a published GitHub Release (tag drives the version, leading `v` stripped) or
> manual `workflow_dispatch`, using **NuGet Trusted Publishing (OIDC)** via `NuGet/login@v1` — no long-lived
> API key stored (`permissions: id-token: write`; the nuget.org account name is the `NUGET_USER` secret; the
> registered Trusted Publishing policy's **Workflow File** must be `publish.yml`). It ships only the **7
> implemented libraries** (Core, EntityFrameworkCore, AspNetCore, OpenApi, EntityFrameworkCore.Npgsql,
> Adapters.DataTablesNet, Client.TypeScript); the empty scaffolds (Newtonsoft + the 8 grid-adapter shells)
> and `a2n.Vista.SourceGenerators` (packaging model unsettled — `IncludeBuildOutput=false` with no
> `analyzers/dotnet/cs` pack items → would emit an empty package) are intentionally excluded. Additive-only:
> no source, wire, route, or package-content change; the first green Actions run is the verification. With
> M19 the v0.x foundation is complete. See §2.19.
> Prior: 2026-07-14 (`typescript-client` **LANDED** — **M17**, the OpenAPI-driven TypeScript client
> generator (`src/a2n.Vista.Client.TypeScript`), a **.NET CLI executable** that is a pure downstream consumer
> of the Vista HTTP surface: it reads an **OpenAPI 3.0.4** document (M18) from a file or an HTTPS URL and
> emits framework-agnostic TypeScript — per-view `TRow`/`TCrud` DTO types, the fixed Vista request/response
> envelopes, the presence-discriminated `FilterNode` union, the RFC 7807 `ProblemDetails` type, one generic
> re-lifted `ViewListResult<TRow>`/`PagedResult<TRow>`, and a per-view typed client over an injectable HTTP
> transport + injectable auth provider. Read facets (list/detail/metadata/export) are the default; write
> facets (create/update/delete) are gated **off by default** behind an explicit opt-in. **D131** — the
> OpenAPI document is the single generation source, over a one-way, buffered, pure pipeline
> (**acquire → parse → resolve → model → emit → write**) that makes determinism + all-or-nothing failure
> structural; the generator holds **no** `a2n.Vista` project reference (Core/EF/AspNetCore/OpenApi all
> absent — a pure document consumer). **D132** — secure-by-default client posture: read surface default,
> write surface opt-in; never embeds a credential; defaults transport to HTTPS (non-HTTPS non-loopback base
> URL → typed config failure); surfaces `401`/`403`/`404`/`428`/`409` as distinct typed `ClientResult`
> members, never throwing. Two design facts reconciled against the live M18 emitter (code is the oracle):
> `FilterNode` carries **no** OpenAPI `discriminator` (so the union is **presence-discriminated** on the same
> required members the server uses), and the document **monomorphizes** row-parameterized envelopes
> (`ViewListResult_*`), which the model builder **re-lifts** back into one generic TS type per view.
> Additive-only — changes no server route, envelope, header, error shape, or behavior. Build green
> net8/9/10, a **new `a2n.Vista.Client.TypeScript.Tests` = 136 tests/TFM** (0 failed/skipped) via CsCheck on
> the TUnit runner + a TypeScript generated-runtime property harness (fast-check under Node); the existing
> suites are unchanged — **448 tests/TFM (net8) / 450 (net9/net10)** in `a2n.Vista.Tests` + **112 generator
> tests** — and the Northwind read + write + OpenAPI self-tests PASS unchanged (M17 touches no server code).
> See §2.18.
> Prior: 2026-07-13 (`style-a-coverage` **LANDED** — the **final planned M9 Source Generator
> phase**: Style A (central-template) coverage, D129 (recognition of `ViewTemplate<TDbContext>.AddView<TRow>`
> **invocation** call sites + shape-driven emission for the *nameable* Style A subset) + D130 (the reaffirmed
> permanent by-design `[RequiresUnreferencedCode]` boundary for anonymous projections + the non-blocking
> coverage diagnostics + the AOT-probe asymmetry demonstration). A fifth incremental generator
> (`StyleAShapeGenerator`) is the first to key off an **invocation** (not a class declaration): for a covered
> view it emits — into the template's own assembly, keyed by the **constant** `AddView` name — (a) export
> accessors for a **named** `TRow`, (b) read-DTO `JsonTypeInfo` (`TRow`/`ViewListResult<TRow>`/
> `PagedResult<TRow>`) for a named `TRow`, and (c) write-model `TCrud` `JsonTypeInfo` for **any** writable
> view (`TCrud` is always named, D38) — all **shape-only** (no projection reconstruction), registered into
> the **existing** `ViewAccessorRegistry` (D117) and `GeneratedJsonContextStore` (D125). **No new store, no
> new seam:** the D126 drain and the `ExportColumns.Value(...)` export seam pick up Style A entries unchanged.
> An **anonymous** read `TRow` is unnameable in generated source, so its read serialization/export stay RUC
> **forever** (D96/D130, `VISTA0061`); the write side of the same view still binds AOT-clean — the D96
> asymmetry *within one view*. Non-blocking diagnostics `VISTA0060` (covered, Info), `VISTA0061` (anonymous
> read → RUC by design, Info), `VISTA0062` (non-constant name, Info), `VISTA0063` (non-emittable member,
> Warning). Mechanism-only (no wire change); byte-for-byte parity with the reflection oracle is the guard
> (master Property 1 + round-trip Property 2 + accessor Property 3). Build green net8/9/10, **448 tests/TFM
> (net8) / 450 tests/TFM (net9/net10)** in `a2n.Vista.Tests` + **112 generator tests** (0 failed/skipped),
> AOT probe clean on the covered named-row export + read-DTO serialization + the writable anonymous-row
> `TCrud` write binding (while its anonymous read row legitimately stays RUC), Northwind read + write +
> OpenAPI self-tests PASS unchanged (no regression). See §2.17.
> Prior: 2026-07-13 (`openapi-emitter` **LANDED**: M18 — the OpenAPI emitter, D127 (the runtime,
> metadata-driven `VistaOpenApiDocumentBuilder` + a new opt-in `a2n.Vista.OpenApi` package with its own
> deterministically serializable OpenAPI object model) + D128 (the opt-in serve endpoint
> `AddVistaOpenApi()`/`MapVistaOpenApi()` + an optional `Microsoft.AspNetCore.OpenApi` pipeline provider on
> net9/net10). The new package references `a2n.Vista.AspNetCore` and is a pure downstream consumer of two
> already-landed foundations it never modifies: the metadata model (`ViewMetadata`/`IViewRegistry`, the
> endpoint-parity oracle) and the serialization seam (`VistaJson.Options`, the schema/wire-parity oracle).
> `VistaOpenApiDocumentBuilder` turns each registered `ViewMetadata` into the fixed operation set
> (`list`/`detail`/`metadata`/`export` for every view + `create`/`update`/`delete` iff `!IsReadOnly`) over a
> hand-authored `OpenApiDocument` object model serialized byte-stably through its own source-gen
> `JsonSerializerContext`. Structure (paths, operationIds, per-facet security, RFC 7807 error responses, the
> polymorphic `FilterNode` `oneOf`), the Vista envelope descriptors, and `ProblemDetails` are all
> **reflection-free (AOT-clean)**; only per-view `TRow`/`TCrud`/nested-POCO schemas come from the single
> `[RequiresUnreferencedCode]` `DtoSchemaGenerator` branch (D96 asymmetry), which emits a permissive `{}`
> schema + a non-fatal notice for an unresolvable member (never omits, never throws). Property names /
> enum-as-string / nullability / BCL formats track the seam so schemas match the wire. Serving is opt-in and
> **off by default** (`GET /openapi/v1.json`, inside the host auth pipeline); the emitter is additive-only —
> every existing response is byte-for-byte unchanged; Core/EF/AspNetCore gain no dependency on the new
> package; adapter-endpoint documentation is deferred (extension hook only). Correctness rests on two oracles
> (endpoint parity from the route table; schema/wire parity validated instance-against-schema) with
> determinism as the stabilizer; **no new VISTA diagnostics** (unresolvable-member notices go through
> `ILogger`). Build green net8/9/10 (0 warnings), **431 tests/TFM (net8) / 433 tests/TFM (net9/net10)** in
> `a2n.Vista.Tests` + **89 generator tests** unchanged (0 failed/skipped), AOT probe clean on an
> envelopes+`FilterNode`-only document (the RUC `DtoSchemaGenerator` is not reached on that path), Northwind
> read + write self-tests PASS **and** a new OpenAPI self-test PASSES (`GET /openapi/v1.json` → 200, `openapi
> 3.0.4`, 15 paths, endpoint parity with 0 missing/phantom, byte-for-byte coexistence). See §2.16.
> Prior: 2026-07-12 (`source-generator-json-typeinfo` **LANDED**: M9 per-view `JsonTypeInfo` phase
> (M9-P5) — D125 (the generated per-view `JsonTypeInfo` provider + Core-resident, serializer-neutral
> `GeneratedJsonContextStore`) + D126 (the seam integration that auto-chains the generated contexts,
> making the developer `App_Json_Context` optional). A fourth incremental generator
> (`ViewJsonContextGenerator`) emits, per covered typed Style B view, a reflection-free
> `IJsonTypeInfoResolver` built by hand via `JsonMetadataServices` (NOT `[JsonSerializable]` — the
> generator-of-generator constraint) providing the `JsonTypeInfo` for `TRow`, `ViewListResult<TRow>`,
> `PagedResult<TRow>`, and — when writable — `TCrud`, plus the collection/nullable/enum metadata those DTOs
> reach; a `[ModuleInitializer]` fills the Core-resident `GeneratedJsonContextStore` (opaque `object`
> handles → Core stays STJ-free). `a2n.Vista.AspNetCore` drains the store and chains each generated context
> into the existing `TypeInfoResolverChain` ahead of the developer `App_Json_Context`(s) and the reflection
> fallback — no seam/invoker/API change. Mechanism-only (no wire change); byte-for-byte parity with the
> reflection oracle is the guard (master Property 1 + round-trip Property 2). Non-blocking diagnostics
> `VISTA0050`/`VISTA0051`. Build green net8/9/10, **281 tests/TFM** in `a2n.Vista.Tests` + **89 generator
> tests** (0 failed/skipped), AOT probe clean on the full typed Style B round-trip with **no developer
> context and the reflection fallback removed**, Northwind read + write self-tests PASS with its developer
> `NorthwindJsonContext` **removed** (now exercising the generated per-view serialization). See §2.15.
> Prior: 2026-07-12 (`source-generator-http-surface` **LANDED**: M9 HTTP-surface phase (M9-P4) —
> D123 (the generated dispatch invoker + Core `ViewInvokerStore`) + D124 (the AOT-clean serialization
> seam). A third incremental generator (`ViewInvokerGenerator`) emits, per covered typed Style B view, a
> Core-only reflection-free `IViewInvoker` (closes `IViewExecutor.List/Detail/Create/Update<T>` at compile
> time — no `MakeGenericMethod`/`Task.Result`/`ViewListResult<TRow>` reflection) + a `[ModuleInitializer]`
> filling a Core-resident, first-wins `ViewInvokerStore`; `ViewRequestExecutor` prefers it and confines
> `[RequiresUnreferencedCode]` to private `*ReflectionAsync` fallbacks (the executor read facets' RUC was
> relaxed to match). AspNetCore gains a unified serialization seam — a `TypeInfoResolverChain` over
> `VistaJson.Options` (shipped `VistaStaticJsonContext` → developer `App_Json_Context`(s) via
> `AddVistaJsonContext(...)` → opt-out `DefaultJsonTypeInfoResolver` fallback), a reflection-free
> `FilterNodeJsonConverter`, and a shared `VistaJsonWriter`; List/Detail/Export + `VistaWriteBinding`
> (de)serialize through it (byte-for-byte parity). Core stays STJ/EF/HTTP-free. Mechanism-only (no wire
> change); byte-for-byte parity with the reflection oracle is the guard (master Property 1). Non-blocking
> diagnostics `VISTA0040`/`VISTA0041`. Build green net8/9/10, **267 tests/TFM** + **64 generator tests**
> (0 failed/skipped), AOT probe clean (zero IL2026/IL3050 on the full generated Style B HTTP round-trip),
> Northwind read + write self-tests PASS — write now reports `ViewInvoker: GENERATED`. See §2.14.
> Prior: 2026-07-09 (`source-generator-write-mapper` **LANDED**: M9 write-DSL phase — D121 (the
> generated write mapper) + D122 (interim write-authoring startup guards promoted to build-time analyzer
> diagnostics). A second incremental generator (`WriteMapperGenerator`) now emits, per analyzable typed
> Style B writable view, a reflection-free `WriteMapper` as C# source + a `[ModuleInitializer]` that fills
> the M12 `GeneratedWriteMapperStore`, so `WriteMapperResolver` silently prefers the generated mapper with
> **zero executor changes** — the reflection `[RequiresUnreferencedCode]` write path is now AOT-clean for
> typed Style B. Build-time diagnostics `VISTA0030`/`VISTA0031`/`VISTA0032` (errors: zero-mapping /
> non-scalar target / key-or-token target) replace the interim startup fail-fast guards (D122); `VISTA0033`
> (warning) marks an unanalyzable `MapWritable` chain → silent reflection fallback. Build green net8/9/10,
> **206 tests/TFM** (0 failed/skipped) + 21 generator tests, Northwind read + write self-tests PASS (write
> now reports `WriteMapper: GENERATED`). See §2.13.
> Prior: 2026-07-07 (`write-path` **LANDED**: M12 — D119 (write mapping seam) + D120 (write
> error vocabulary + concurrency signalling). Writable Style B views now execute Create/Update/Delete
> through the `IViewExecutor` write facet (DR8), replacing the DR7 501 stub: default-deny `MapWritable`
> mass-assignment whitelist, protected keys + concurrency token, optimistic concurrency (`If-Match`/`ETag`),
> server-trusted scope, single `SaveChanges`, minimal (PK-only) write responses, RFC 7807 write-error
> vocabulary; reflection write mapper behind a fixed-signature seam (`WriteMapperResolver`) a future
> generated mapper fills, `[RequiresUnreferencedCode]` confined to that branch. Build green net8/9/10,
> **204 tests/TFM** (0 failed/skipped), Northwind read + write self-tests PASS. See §2.12.
> Prior: 2026-07-01 (`style-b-executable` **LANDED**: D118 — source-generator Phase 2 = M10 + M11 + M13.
> Second generator emitter produces AOT-clean `ICompiledViewExecutionPlan` per typed Style B view →
> executable List/Detail (DR5 closed for typed views); D105 single-source PK auto-derivation at startup;
> masking runtime on materialization. 156 tests/TFM, AOT probe clean. M9 Phase 1 earlier: D117 —
> incremental generator + shape-driven export accessors)
> Purpose: a single, authoritative snapshot of *where the project is*, *what was decided*, and *what
> is next*, so a new chat/work session can continue without re-litigating settled decisions ("no
> dispute"). When this document and the code disagree, **the code is the source of truth**; reconcile
> the docs to the code, not the other way around.

---

## 0. How to use this document

- For an **at-a-glance milestone/roadmap tracker** (progress bars, what's done, what's next, dependency
  graph), read **`docs/MILESTONES.md`** first — it's the readable companion to this detailed snapshot.
- Read §1–§3 for the current state and what is implemented.
- Read §4 (**Settled decisions — do not re-litigate**) before proposing design changes. Each entry
  cites where the full rationale lives.
- §5 is the **decision-number map** (which spec owns which `D###`/`DR##`, what was superseded). Use the
  next free number when adding decisions.
- §6 is the **next-work plan** (Spec 02 gap analysis → `query-engine-hardening`).
- §7 is the **backlog / known gaps**.
- §8 has **build/test commands** (important: TUnit specifics).
- §9 has **key code locations**.

---

## 1. Project shape

`a2n.Vista` is a .NET library that exposes EF Core data as declarative, secure-by-default **Views**
(read + optional write) over HTTP, replacing/evolving the older `a2n.DynData` (a read-only reference
repo at `d:\GitHub\DynData`, **do not modify**; see `.kiro/steering/readonly-external-roots.md`).

Three pillars (see `ROADMAP.md`):
- **Pillar 1 — Core View engine** (View concept, neutral query/filter contract, EF execution, ASP.NET
  endpoints). **Implemented — read (List/Detail) and write (Create/Update/Delete) (M12, D119/D120).**
- **Pillar 2 — Adapters + neutral query engine** (server half = query engine, **built & hardened**:
  PK-in-metadata, deterministic paging, DoS guards, `IQueryDialect` port, composite keys — see §2.4;
  HTTP surface is now action-style POST + `GET metadata` — see §2.5; multi-channel Search/Scope request —
  see §2.7; client half = grid adapters, **two built: the DataTables.NET reference adapter (§2.7) and the
  AG Grid adapter (M16, D133–D136, §2.20)**; the other seven grid adapters are empty scaffolds).
- **Pillar 3 — Source generator** (AOT-clean codegen). **Phase 1 landed (M9, D117):** an incremental
  generator emits shape-driven field accessors for typed Style B views, registered into a Core store the
  export pipeline prefers over reflection (coexistence) — see §2.10. **Phase 2 landed (M10+M11+M13,
  D118):** a second emitter produces an AOT-clean `ICompiledViewExecutionPlan` per typed Style B view
  (compile-time projection, per-field member-access, typed sort appliers, masked-field accessors) →
  executable List/Detail (DR5 closed for typed views), plus startup single-source PK auto-derivation
  (D105) and the masking runtime on materialization — see §2.11. **Phase 3 landed (M9 write-DSL,
  D121/D122):** a second generator (`WriteMapperGenerator`) emits a reflection-free `WriteMapper` per
  analyzable typed Style B writable view into the M12 `GeneratedWriteMapperStore`, so the write path is
  now AOT-clean for typed Style B (the reflection mapper is a fallback only) — see §2.13. The remaining
  reflection paths (Style A serialization/write; there is no typed-`TCrud` Style A write) stay
  `[RequiresUnreferencedCode]` until later phases. The **write path (M12, D119/D120)** is implemented on
  the executor write facet — see §2.12. **Phase 4 landed (M9-P4, D123/D124, spec
  `source-generator-http-surface`):** the generated dispatch invoker (`IViewInvoker` + Core-resident
  `ViewInvokerStore`, D123) + the AOT-clean serialization seam (`TypeInfoResolverChain` over
  `VistaJson.Options`, D124) make the full typed Style B `request → authorize → execute → serialize` path
  IL2026/IL3050-clean — the ASP.NET Core `ViewRequestExecutor`/`VistaWriteBinding` reflection is now a
  permanent fallback (Style A / uncovered views), with RUC confined to it — see §2.14. **Phase 5 landed
  (M9-P5, D125/D126, spec `source-generator-json-typeinfo`):** a fourth generator
  (`ViewJsonContextGenerator`) emits a reflection-free per-view `JsonTypeInfo` set (via
  `JsonMetadataServices`, not `[JsonSerializable]`) registered into a Core-resident, serializer-neutral
  `GeneratedJsonContextStore`, and the AspNetCore seam auto-chains it — making the developer
  `App_Json_Context` **optional** so an app of covered typed Style B views is AOT-clean for serialization
  with no hand-authored context — see §2.15. **OpenAPI emitter landed (M18, D127/D128, spec
  `openapi-emitter`):** a new opt-in `a2n.Vista.OpenApi` package emits a deterministic OpenAPI v3.x document
  for every mapped view from `ViewMetadata` (structure reflection-free; per-view DTO schemas the one RUC
  branch), served off-by-default at `GET /openapi/v1.json`, additive-only and byte-for-byte non-regressing —
  see §2.16. **Style A coverage landed (M9-P6, D129/D130, spec `style-a-coverage`):** the fifth generator
  (`StyleAShapeGenerator`) covers the nameable Style A subset (named-`TRow` export accessors + read-DTO
  `JsonTypeInfo`, and every writable view's `TCrud` `JsonTypeInfo`) into the existing stores; anonymous read
  serialization stays permanently RUC by design (D96) — **with this, M9 (the Source Generator, Pillar 3) is
  complete** — see §2.17. **TypeScript client landed (M17, D131/D132, spec `typescript-client`):** the
  standalone `a2n.Vista.Client.TypeScript` CLI generates a framework-agnostic typed TS client from the
  emitted OpenAPI document — see §2.18. **CI + NuGet publish workflows landed (M19):** `.github/workflows/
  ci.yml` (build + TUnit across net8/9/10) + `publish.yml` (pack + push to nuget.org via NuGet Trusted
  Publishing / OIDC, off long-lived keys) — see §2.19. **AG Grid adapter landed (M16, D133–D136, spec
  `ag-grid-adapter`):** the second Pillar 2 client-half grid adapter (`a2n.Vista.Adapters.AgGrid`, Core-only)
  + an AG Grid + TypeScript Northwind sample — see §2.20. **Still to come (planned, not started):** the
  remaining ecosystem — the other seven grid adapters (MudBlazor next), observability (M14, D100), and
  versioning (M15, D99) — see §6.

Multi-target: `net8.0;net9.0;net10.0`. Nullable enabled. Central Package Management. Test framework:
**TUnit**.

Packages / layering (Decision D48, enforced):
- `a2n.Vista.Core` — EF-free & HTTP-free. Contracts, metadata, authoring builders, ports
  (`IViewExecutor`, `IViewScope`, `IViewRegistry`), `FilterCompiler`.
- `a2n.Vista.EntityFrameworkCore` — implements `IViewExecutor` (`EfViewExecutor`), registration
  (`AddVista`/`IVistaBuilder`), the default `IQueryDialect` (`DefaultQueryDialect`).
- `a2n.Vista.EntityFrameworkCore.Npgsql` — optional PostgreSQL dialect (`NpgsqlQueryDialect`, ILIKE) via
  `AddVistaNpgsql()`; keeps the Npgsql dependency out of Core/EF.
- `a2n.Vista.AspNetCore` — HTTP: action-style endpoint mapping, JSON envelopes + polymorphic
  `FilterNode` converter, `IViewAuthorizer`, error model. **No EF reference.**
- `a2n.Vista.OpenApi` — **optional, opt-in** OpenAPI v3.x emitter (M18, D127/D128). References
  `a2n.Vista.AspNetCore`; on net9/net10 pulls `Microsoft.AspNetCore.OpenApi` for the optional pipeline
  provider. No other Vista package references it; it is a read-only downstream consumer of `ViewMetadata` +
  the serialization seam.
- `a2n.Vista.Client.TypeScript` — **standalone CLI tool** (M17, D131/D132), a TypeScript client generator.
  It is a pure downstream consumer of the emitted OpenAPI document and references **no** Vista package
  (not Core, EF, AspNetCore, or OpenApi); its only inputs are the document (file/HTTPS) + a small config,
  its only output is TypeScript source. Multi-targets net8/9/10.
- EF and AspNetCore do **not** reference each other; they meet at Core ports.

---

## 2. Implemented & verified

### 2.1 `pilar-1-core` (`.kiro/specs/pilar-1-core`, Tasks 1–13 complete)
Core contracts (`FilterNode`/`FilterLeaf`/`FilterAnd/Or/Not`, `FilterOperator`, `SortSpec`,
`ViewQueryRequest`, `FilterOrigin`), `PagedResult<T>`, `ViewListResult<TRow>`, `FieldMetadata`,
`ViewMetadata`, `HardLimits`; ports `IViewScope`/`IViewExecutor`/`IViewRegistry`; authoring Style A
(`ViewTemplate`) + Style B (`View<TQuery>`/`View<TQuery,TCrud>`); `FilterCompiler` with tri-whitelist;
`EfViewExecutor` (List/Detail end-to-end, provider-aware, two counts); AspNetCore endpoints + one-door
auth; Northwind example.

### 2.2 `pilar-1-hardening` (`.kiro/specs/pilar-1-hardening`, Tasks 1–6 complete)
- **D94** auth fail-safe posture (see §4).
- **D95** masked field defaults non-filterable/non-searchable.
- **D101 + D103** route groups + single source (model R, see §4).
- **D99** wire-version seam (deferred).

### 2.3 Verification status (as of last update)
- Full solution build green on **net8.0 / net9.0 / net10.0** (incl. `a2n.Vista.EntityFrameworkCore.Npgsql`).
- Test suite green on all three TFMs (existing suite + `QueryEngineHardeningTests` + `HttpSurfaceTests`).
- Northwind example **selftest PASS**: List paging, filter+search, Detail by single key, **and composite
  Detail (OrderId+ProductId) via a name→value map**. Example targets **net8.0 only**.

### 2.4 `query-engine-hardening` (engine work landed; spec `.kiro/specs/query-engine-hardening`)
Implemented and tested (D104–D109):
- **D104** view key model surfaced: `FieldMetadata.IsPrimaryKey` + `ViewMetadata.KeyFields` (ordered,
  composite-capable), both added as **`init` properties** (records stay immutable). `.PrimaryKey()`
  now propagates into metadata; a view-level `Key(...)` override exists on both authoring styles.
- **D106** deterministic paging: `EfViewExecutor.ApplySort` appends `KeyFields` as the ordered
  tiebreaker; empty sort orders by `KeyFields`. Registration **fail-fast** when a view has no key; the
  old `Id`/`{Type}Id`/first-field name convention is **removed**. `IViewExecutionPlan.KeyFieldName`
  removed (metadata is the single source).
- **D107** `IQueryDialect` port (Core) + `FilterCompiler(IQueryDialect?)`; `DefaultQueryDialect` (EF,
  `LIKE`+ESCAPE) registered by `AddVista`; **`ProviderAwareFilterCompiler` retired**;
  `a2n.Vista.EntityFrameworkCore.Npgsql` (`NpgsqlQueryDialect` ILIKE) + `AddVistaNpgsql()`.
- **D108** DoS guards enforced in `FilterCompiler` from `HardLimits` (`MaxFilterDepth/Leaves/StringLength/
  MaxInValues`), new `FilterErrorCode.RequestTooComplex` (wire `filter-too-complex`).
- **D109** composite Detail-by-key at the executor: `DetailAsync(object key)` unchanged; the executor
  normalizes a scalar or `IReadOnlyDictionary<string,object?>` (by-name) against `KeyFields`. Key
  coercion reuses `FilterCompiler.CoerceValue` (internal).

**Deferred / not done in this pass (tracked):**
- **D105 single-source PK auto-derivation** — NOT implemented. The EF model is not available at
  `AddVista` registration time, so a key must currently be declared explicitly (`.PrimaryKey()` /
  `Key(...)`); registration fails fast otherwise. Auto-derivation needs a startup/model hook (e.g. an
  `IHostedService` that reads `DbContext.Model`) — follow-up.
- **D107 startup provider guard** — **DONE (close-out 2026-06-27).** `VistaDialectStartupValidator`
  (EF `Hosting/`, `IHostedService` auto-registered by `AddVista`): provider-specific dialect on a
  mismatched `DbContext.Database.ProviderName` → throw; default dialect on PostgreSQL → warn; best-effort
  skip when no context/provider is observable. Adds `Microsoft.Extensions.Hosting.Abstractions` to the EF
  package. Covered by `DialectStartupGuardTests`.

(The Northwind composite-key example view was added with `http-surface-redesign` — see §2.5.)

### 2.5 `http-surface-redesign` (landed; spec `.kiro/specs/http-surface-redesign`)
Action-style endpoints implemented (**D110**, **supersedes DR3**), build green net8/9/10, tests pass,
Northwind selftest PASS (incl. composite-key Detail):
- Endpoints: `POST {route}/list`, `POST {route}/detail`, `GET {route}/metadata`,
  `POST {route}/export`, `POST {route}/{create|update|delete}` (write actions only for writable views).
  Implemented in `VistaEndpointRouteBuilderExtensions` (action-style mapper); `VistaQueryStringParser`
  **retired**.
- Key & query in JSON body: polymorphic `FilterNodeJsonConverter` (STJ), `VistaJson` options,
  `VistaKeyReader` (scalar | name→value map), request envelopes (`VistaListRequestBody` etc.),
  serializable `VistaMetadataResponse`. Global `search` folded into the filter tree over searchable
  string fields (`VistaSearchMerge`).
- Glue: `ViewRequestExecutor.MetadataAsync` + `ExportAsync`; List/Detail fed from the body; one-door
  auth + D94 posture unchanged; new `VistaInvalidRequestException` → 400.
- Northwind: composite-key `vOrderDetail` view (keyed by `OrderId`+`ProductId`); selftest exercises
  composite Detail via a name→value map.

**Deferred within this spec (tracked):**
- Full HTTP endpoint **integration test** (TestServer) — **DONE (2026-06-27)**:
  `HttpEndpointIntegrationTests` drives list/metadata/datatable/page-size-400/metadata-cache over an
  in-process `TestServer` with a real SQLite-backed Gaya A view.
- **Export pipeline** beyond row-streaming + `MaxExportRows` (CSV/XLSX formatting) — in progress as the
  `export-pipeline` spec (pluggable `IViewExportWriter`).
- Metadata cache headers (`ETag`/`Cache-Control`) — **DONE (2026-06-27)**: opt-in via
  `AddVistaEndpoints(e => e.EnableMetadataCaching())` (off by default; emits `ETag` + `Cache-Control`,
  honors `If-None-Match` → 304).
- `docs/spec/05-aspnetcore-mapping.md` prose — **reconciled (2026-06-27)** to the action surface + D111/D112.

### 2.6 Remaining query-engine follow-ups
See §2.4 "Deferred": **D105** single-source PK auto-derivation (the only remaining engine follow-up; the
**D107 startup provider guard landed** in the 2026-06-27 close-out).

### 2.7 `datatables-adapter` (landed; spec `.kiro/specs/datatables-adapter`)
First Pillar 2 client-half adapter + the engine change that makes the Search/Scope channels real
(**D111–D114**). Build green net8/9/10, full suite green (93 tests/TFM), Northwind selftest PASS incl. the
DataTables round-trip:
- **D111 — multi-channel request (engine).** `ViewQueryRequest` gained additive `Search`/`Scope`
  `FilterNode?` slots; `EfViewExecutor.ListAsync` compiles each present sub-tree under its own
  `FilterOrigin` and AND-s them. The client `Scope` sub-tree is applied to the **unfiltered baseline**, so
  it counts toward `recordsTotal`; `Filter`+`Search` are the client filter excluded from `recordsTotal`.
  `VistaSearchMerge` now routes global search to the `Search` slot (not folded into `Filter`);
  `VistaListRequestBody` gained a `Scope` slot. **Closes the per-channel Search/Scope enforcement deferred
  by `query-engine-hardening` (DR9).** Per-origin validation in `FilterCompiler.ValidateLeaf` was reused
  unchanged.
- **Core `IViewAdapter` contract.** `a2n.Vista.Core/Adapters/`: `IViewAdapter` (non-generic, host-facing)
  + `ViewAdapter<TRequest,TResponse>` base, `AdapterRequest` (neutral HTTP bag), `AdapterListResult`
  (type-erased rows + two totals), `AdapterBindException`.
- **DataTables.NET adapter** (`a2n.Vista.Adapters.DataTablesNet`, Core-only): `DataTablesQuery`/
  `DataTablesResponse<T>` POCOs, `DataTablesAdapter` (`Id="datatables"`, `RouteSuffix="datatable"`),
  `QueryBuilderParser` (`jsonQB` → Filter, incl. D64), `ExternalFilterParser` (`externalFilter` → Scope),
  source-gen `DataTablesJsonContext`.
- **AspNetCore glue.** `AdapterRequestFactory` (HttpContext → `AdapterRequest`, query+form+JSON body),
  `ViewRequestExecutor.ListForAdapterAsync` → `AdapterListResult`, `AddVistaAdapter<TAdapter>()`,
  `MapSingleView` maps `POST {route}/{RouteSuffix}` per registered adapter, `AdapterBindException` → 400
  `adapter-bind-failed`. AspNetCore stays EF-free and references the adapter only through the Core port.

**Deferred within this spec (tracked):** D113 QueryBuilder schema emitter (`metadataQB`) — **DONE** in
`metadata-schema-adapters` (§2.9). (The full HTTP TestServer integration test landed — see §2.5.)

### 2.8 `export-pipeline` (landed; spec `.kiro/specs/export-pipeline`)
Pluggable export pipeline (**D115**). Build green net8/9/10, suite + Northwind selftest PASS:
- Core `IViewExportWriter` port (`Format`/`ContentType`/`FileExtension`/`WriteAsync`) + `ExportColumns`
  helper (non-hidden fields, read value by name, RUC).
- Built-in **`CsvViewExportWriter`** (RFC 4180, UTF-8 BOM, CRLF) and **`XlsxViewExportWriter`** (minimal
  valid OpenXML via `ZipArchive`, BCL-only — a clean re-impl of DynData's `LiteExcelWriter`). Both in
  `a2n.Vista.Core/Export/`.
- `AddVistaExportWriter<T>()` (last-per-format wins → custom overrides built-in); built-ins registered by
  `AddVistaEndpoints`. `POST {route}/export` resolves the writer by the body `format` and streams a file
  (`Content-Disposition`); no `format` → the JSON `ViewListResult` (backward compatible); unknown format
  → 400. `ViewRequestExecutor.ExportRowsAsync` returns the bounded rows + metadata.

### 2.9 `metadata-schema-adapters` (landed; spec `.kiro/specs/metadata-schema-adapters`)
Per-grid metadata schema (**D116**, closes D113). Build green net8/9/10, suite + Northwind selftest PASS:
- Core `IViewMetadataAdapter` (host-facing, type-erased: `Id`/`RouteSuffix`/`BuildSchema(ViewMetadata)`).
- `QueryBuilderSchemaAdapter` (DataTables package) emits the DynData-compatible `metadataQB`
  (`{ viewName, metaData[], queryBuilderOptions: { filters[] } }`; filters only for `IsFilterable` fields,
  a `Hidden` field only when `Scopable` per D65; operators from `AllowedOperators`). Built as nested
  dictionaries so key casing is verbatim.
- `AddVistaMetadataAdapter<T>()`; host maps `GET {route}/{RouteSuffix}` (QueryBuilder → `/querybuilder`)
  per adapter, authorized as the Metadata facet.

### 2.10 `source-generator` — M9 Source Generator, Phase 1 (landed; spec `.kiro/specs/source-generator`)
Pillar 3 stood up (**D117**, phased scope). Build green net8/9/10, **122 tests/TFM** in
`a2n.Vista.Tests` + **4 tests/TFM** in the new `a2n.Vista.SourceGenerators.Tests`, Northwind self-test
(net8.0) PASS — the generator coexists with the existing reflection registration (nothing broke).

- **Core accessor store + export seam** (`a2n.Vista.Core`): new static
  `a2n.Vista.Metadata.ViewAccessorRegistry` — a process-wide, thread-safe, idempotent (first-wins) store
  `viewName → { fieldName → Func<object,object?> }` (`Register` / `TryGetAccessor`). New AOT-clean
  `ExportColumns.Value(string viewName, object? row, string fieldName)` overload that prefers a registered
  generated accessor and falls back to the reflection read (`[RequiresUnreferencedCode]` isolated on the
  reflection branch). `CsvViewExportWriter`/`XlsxViewExportWriter` now thread `view.Name` through that
  overload, so the writers' value path is no longer RUC — **no `IViewExportWriter` contract change** (R3, R4).
- **Incremental generator** (`a2n.Vista.SourceGenerators`, `netstandard2.0`, references no Vista project,
  FQN recognition): `ViewAccessorGenerator` (`IIncrementalGenerator`) — fast syntax predicate + semantic
  transform recognizing typed Style B views (`a2n.Vista.Authoring.View<TQuery>` / `View<TQuery,TCrud>`);
  an equatable value model (`ViewModel` + `EquatableArray<T>` + a `LocationInfo` surrogate) for incremental
  caching (an unrelated edit does not regenerate every view). Emits, per view, a `file static` accessor
  map (cast + property read per public readable `TQuery` property) plus a `[ModuleInitializer]` that
  registers it into `ViewAccessorRegistry` keyed by the view's runtime `Name` (R1, R2, R3).
- **Diagnostics:** `VISTA0001` (error) — non-partial Style B view → skipped; `VISTA0002` (info) — Style B
  view lacking a public parameterless ctor → skipped (the module initializer cannot instantiate it to read
  its `Name`). Both carry the `a2n.Vista.SourceGenerators` category + a help link; analyzer release tracking
  files added (R5).
- **Tests / samples:** `src/Tests/a2n.Vista.SourceGenerators.Tests` (snapshot/golden via
  `CSharpGeneratorDriver`: single-key view, CRUD view, non-partial → VISTA0001, plus an incremental
  cache-reuse assertion); an export **parity** test in `a2n.Vista.Tests` (generated accessor vs reflection
  → identical CSV/XLSX); `src/Examples/a2n.Vista.AotProbe` (net8, `IsAotCompatible`, IL2026/IL3050-as-errors
  build proving the generated-accessor export path is trim/AOT-clean); `src/Examples/a2n.Vista.GeneratorSample`
  (a real consumer assembly exercising the generator end to end, referenced by `GeneratorEndToEndTests`)
  (R6).

**Deferred to later phases (NOT done in Phase 1):** executable Style B (`IViewExecutionPlan`/`CompiledView`),
member-access expressions for filter/sort, `JsonSerializerContext` generation, OpenAPI, projection/
`MapWritable` DSL body analysis, and Style A (anonymous) accessor/serialization generation.

### 2.11 `style-b-executable` — M9 Source Generator, Phase 2 (landed; spec `.kiro/specs/style-b-executable`)
Source-generator **Phase 2** bundling **M10 + M11 + M13** over one shared materialization + execution-plan
seam (**D118**). Build green net8/9/10, **156 tests/TFM** in `a2n.Vista.Tests`, Northwind self-test (net8.0)
PASS, AOT probe clean (zero IL2026/IL3050 on the generated read path). The generated compiled path coexists
with the reflection (RUC) path — behavioral parity is the central guard.

- **Core masking primitives** (`a2n.Vista.Core`): `MaskSpec(FieldName, ShouldMask, Masker)` and
  `MaskAccessor(FieldName, Get, Set)` records; `ViewBuilder.MaskField` now captures **both** the
  `shouldMask` predicate and the `masker` (previously the predicate was discarded), recorded as an ordered
  `IReadOnlyList<MaskSpec>` per view without putting runtime delegates on the EF-free `ViewMetadata`. D95
  defaults unchanged.
- **EF compiled-plan contract + store** (`a2n.Vista.EntityFrameworkCore`): non-RUC
  `ICompiledViewExecutionPlan` (`ViewName`, `RowType`, `SourceType`, `IsSingleSource`,
  `CreateScopedQueryable`, `TryGetMemberAccess`, `ApplyPrimarySort`, `ApplyThenSort`, `MaskAccessors`) — it
  **does not** inherit the RUC `IViewExecutionPlan` (DR8 seam split, read-only — no write member). Process-
  wide, thread-safe, first-wins idempotent `GeneratedExecutionPlanStore` (`Add`/`TryGet`), mirroring the
  Phase 1 `ViewAccessorRegistry` rationale.
- **Generator Phase-2 emitter** (`a2n.Vista.SourceGenerators`): extends the equatable model with
  `SourceTypeFqn`/`IsSingleSource`/`ProjectionModel`/`PlanFieldModel`; reproduces the `From<TSource>(...)`
  member-init/named-ctor projection from syntax+semantics and emits, per analyzable single-source typed
  view, a `file`-scoped `CompiledViewExecutionPlan_<View>` (projection as C# source, member-access map
  `field → Expression<Func<TRow,TField>>`, strongly-typed closed-generic sort appliers, `MaskAccessor`
  get/set with `with`-style rebuild for record/init rows, server-trusted row filters) plus a
  `[ModuleInitializer]` that registers it into `GeneratedExecutionPlanStore`. No `Activator.CreateInstance`/
  `PropertyInfo`/`Expression.Property(string)`/`MakeGenericMethod`/`Compile()` — trim/AOT-clean.
  Diagnostics: **`VISTA0003`** (warning, unanalyzable projection → skip plan, stay metadata-only) and
  **`VISTA0020`** (error, statically-provable keyless executable view).
- **Registration & coexistence**: `AddVista` drains the store; `Register<TView>()` looks up by runtime
  `Name` — plan present → adds to `IViewExecutionPlanRegistry` (executable) **and** publishes
  `ViewMetadata`; plan absent → metadata-only (DR5 preserved). No `Assembly.GetTypes()`/type enumeration.
- **EfViewExecutor compiled read path**: non-RUC `ListCompiledAsync`/`DetailCompiledAsync` build
  filter/order from the generated member-access lambdas + sort appliers (no `Expression.Property(string)`,
  no `MakeGenericMethod`); disallowed/non-projected/masked-without-opt-in fields rejected through the
  existing `FilterCompiler` tri-whitelist via an injected member-access resolver (compiler not forked).
  Metadata-only views fail fast on execution. Default order by `KeyFields`; client sort + `KeyFields`
  tiebreaker (D106).
- **Masking runtime (M13)**: `MaskApplier` applies masks post-projection in memory on List/Detail/export
  without altering SQL; `ShouldMask(services)` evaluated once per request; **fails closed** if a
  predicate/masker/accessor throws (never emits the original). AOT-clean via the generated `MaskAccessor`
  with an RUC reflection fallback for Style A/non-generated views; mask specs delivered via a per-view
  registry that keeps Core EF-free.
- **D105 single-source PK auto-derivation (M11)**: `VistaModelKeyDerivationService : IHostedService`
  completes `ViewMetadata.KeyFields` at `StartAsync` from `DbContext.Model.FindEntityType(SourceType)
  .FindPrimaryKey()` (composite, in declared key order) for single-source views with no declared key;
  never overrides declared keys; fails closed (naming view+entity) when a single-source source has no model
  PK or a non-single-source view lacks a declared key; skips non-single-source views; startup-only,
  run-at-most-once, never on the request hot path.
- **AOT + write-contract guard**: the Phase 1 AOT probe (`a2n.Vista.AotProbe`, `IsAotCompatible`,
  IL2026/IL3050-as-errors) now also drives the generated Style B List/Detail compiled path — green build =
  zero IL2026/IL3050 on that path; Style A keeps its RUC annotation. The generated plan implements
  List/Detail only (R4.7); the write facet (Create/Update/Delete) is delivered separately by M12 on the
  reflection mapper (§2.12) — at Phase 2 those endpoints still returned 501.
- **Property tests (1–8)** placed next to the code they validate: generated/RUC behavioral parity (model-
  based), List page-bound + unfiltered total, Detail-by-key round-trip, disallowed-field rejection-before-
  SQL (interceptor spy), conditional masking at materialization, masked-field non-probeable, single-source
  PK auto-derivation, snapshot determinism. EF-aware generator-consumer fixtures
  (`a2n.Vista.GeneratorExecSampleP5/P6`, `a2n.Vista.Examples.StyleBExecP7`) emit real compiled plans the
  tests run through `EfViewExecutor`.

**Still deferred after Phase 2 (now partly closed):** the **write path (M12)** is **DONE** (D119/D120,
see §2.12). Remaining: `JsonSerializerContext` generation; OpenAPI; TypeScript client; Style A (anonymous)
accessor/serialization generation; the **generated** write mapper (M12 ships the reflection mapper);
cross-assembly discovery polish (D97) and `MapView<TView>()` (DR10).

### 2.12 `write-path` — M12 write path / CRUD (landed; spec `.kiro/specs/write-path`)
Secure-by-default Create/Update/Delete for writable Style B views (**D119** write mapping seam, **D120**
write error vocabulary + concurrency signalling), replacing the DR7 501 stub. Build green net8/9/10,
**204 tests/TFM** (0 failed/skipped), Northwind **read + write** self-tests PASS (Create/Update/Delete,
0 failed operations).

- **Core (EF-free/HTTP-free)** — `a2n.Vista.Core/Write/`: the fixed-signature `WriteMapper`
  (`(object model, object entity) → void`) seam; `IWriteFacetRegistry` + `WriteFacetRegistry` (per-view
  captured `CrudFacetDefinition`, populated at registration by both authoring styles); `WriteErrorCode`
  vocabulary + `WriteErrorCodes` wire strings; and the typed write exceptions (`VistaWriteException` base,
  `VistaValidationException`, `VistaWriteKeyException`, `VistaPreconditionRequiredException`,
  `VistaConcurrencyConflictException`, `VistaWriteConflictException`, `VistaBulkNotEnabledException`). Both
  adapters raise/consume these without referencing each other (R14.6). `ViewMetadata` stays EF-free
  (only `CrudType`/`CrudEntityType`).
- **Style B authoring** — `CrudBuilder`/`CrudFacetState` now capture the `MapWritable` expressions
  (ordered `WritableFieldMapping` From/To lambdas), the `WithConcurrencyToken` selector, and `AllowBulk`
  into a full `CrudFacetDefinition` (matching Style A). Interim startup fail-fast guards reject a
  zero-mapping facet, a navigation/non-scalar target, and a key-field/concurrency-token target (the
  interim net for the M9 analyzer diagnostics VISTA0030/0031/0032).
- **EntityFrameworkCore** — `ReflectionWriteMapper` (RUC) compiles+caches the whitelisted assignment
  from the captured lambdas (skips key/token/navigation targets, defense in depth); `GeneratedWriteMapperStore`
  (empty this milestone, a future M9 `[ModuleInitializer]` fills it); `WriteMapperResolver` resolves one
  `WriteMapper` per write — generated-preferred, reflection fallback — with `[RequiresUnreferencedCode]`
  confined to the fallback branch. `EfViewExecutor` implements the DR8 write facet: entity resolution
  within the server-trusted `IViewScope` (AND-ed pre-projection, unvalidated), composite-capable key
  normalization/coercion (reusing `FilterCompiler.CoerceValue`), optimistic-concurrency pre-check, single
  `SaveChanges`, and `DbUpdateException`/`DbUpdateConcurrencyException` → typed 409 translation
  (leak-free message; provider detail kept only as inner exception).
- **AspNetCore (dumb mapper)** — `VistaWriteBinding` (body/model/key/`If-Match` binding), `VistaWriteResponse`
  (PK-only), `VistaWriteRequestBody`; `VistaProblemResults` maps every typed write failure onto the shared
  RFC 7807 envelope with `extensions["code"]` (and honors a `WriteErrorCode` carried by
  `VistaInvalidRequestException`); `ViewRequestExecutor` write methods authorize the write facet
  independently and fail-closed (deny/throw → 403); `HandleWriteAsync` replaces the 501 stub — write
  routes mapped only for `!IsReadOnly`, an indistinguishable 404 for miss/read-only/no-plan, a 428 gate
  when a token view omits `If-Match`, and 200 (create: PK body; update/delete: `ETag`) / 404 / 409 /
  4xx-5xx outcomes.
- **Bulk deferred (Requirement 15):** an array body → HTTP 400 `write-bulk-not-enabled`; the `AllowBulk`
  authoring flag enables no execution path this milestone.
- **Northwind:** a writable `vWritableMemo` Style B view (`Memo`/`VistaMemos`, `MapWritable` whitelist +
  `RowVersion` token) and a write self-test (Create → Update → Delete against an isolated in-memory DB, so
  the read-only shipped `northwind.db` is never mutated).
- **Tests:** the nine correctness properties (whitelisted-only assignment, request-key authority, delete
  precision, composite-key order independence, not-found indistinguishability, concurrency abort + token
  round-trip, write-response minimality, error-envelope conformance/non-leakage, rejected-write state
  preservation — CsCheck via TUnit, ≥100 iterations each) plus example/integration/layering suites.

**Still deferred (write path):** ~~the **generated** write mapper (M9 write-DSL phase; the store is created
but empty), the build-time analyzer diagnostics VISTA0030/0031/0032 (interim startup guards ship now)~~ —
**both DONE (2026-07-09, D121/D122; see §2.13).** Remaining: **bulk** Create/Update/Delete (v1.x).

### 2.13 `source-generator-write-mapper` — M9 Source Generator, write-DSL phase (landed; spec `.kiro/specs/source-generator-write-mapper`)
Source-generator **write-DSL phase** (**D121** generated write mapper, **D122** interim write-authoring
startup guards promoted to build-time analyzer diagnostics), filling the single write-path seam M12 left
open (the `GeneratedWriteMapperStore` was created but empty). Build green net8/9/10, **206 tests/TFM** in
`a2n.Vista.Tests` + **21 tests/TFM** in `a2n.Vista.SourceGenerators.Tests` (0 failed/skipped), Northwind
read + write self-tests PASS — the write self-test now reports `WriteMapper: GENERATED (source generator)`,
proving the generated mapper is live and parity-equivalent to the reflection oracle.

- **Second incremental generator** (`a2n.Vista.SourceGenerators`, `netstandard2.0`, no Vista project ref,
  FQN recognition): `WriteMapperGenerator` (`IIncrementalGenerator`) — an independent pipeline from the
  Phase 1/2 `ViewAccessorGenerator` that happens to recognize the same base type, keeping each emitter's
  equatable model small and its snapshot tests isolated. A cheap syntax predicate (partial class with a
  base list) + a semantic transform that walks base types to `a2n.Vista.Authoring.View<TQuery,TCrud>`
  (**arity-2 only** — the write facet requires a typed `TCrud`) and recognizes the CRUD facet
  (`CrudOn`/`MapWritable`/`WithConcurrencyToken`). Produces a fully equatable `WriteMapperModel` (+
  `WriteMappingModel`) reusing `EquatableArray<T>`/`LocationInfo` so an unrelated edit does not regenerate
  every view (R2.1, R2.2, R5.3, R11.*).
- **`MapWritable` analyzer**: extracts the ordered `(CrudMember, EntityMember)` pairs from the fluent
  chain in textual declaration order, unwrapping compiler-inserted `Convert`/`ConvertChecked` to the
  innermost member; recognizes a `Simple_Member_Selector` (single-parameter lambda whose stripped body is
  a member access on the parameter). Captures the concurrency-token member (`WithConcurrencyToken`) and
  statically declared key members (`.Key(...)`/`.PrimaryKey()`), and each target's scalar-ness (value type
  with `Nullable<T>` unwrapped, `string`, or `byte[]`). Marks a view **unanalyzable** when any selector is
  not simple after unwrapping or `TCrud` is not a named type.
- **Emitter**: for an analyzable, non-erroring view, emits a `file static` `<View>_VistaWriteMapper.g.cs`
  holding a `WriteMapper` (`Action<object,object>`) that casts `model`→`TCrud`, `entity`→`TEntity`, and
  emits **exactly one** `entity.<EntityMember> = model.<CrudMember>;` per **safe** mapping (target is
  neither a declared key nor the concurrency token, and is scalar) in declaration order — deterministic,
  byte-for-byte, defense-in-depth matching the reflection oracle. An empty safe subset yields a conforming
  no-op mapper. No `Activator.CreateInstance`, `PropertyInfo` Get/SetValue, or `Expression.Compile` —
  trim/AOT-clean. Plus one `[ModuleInitializer]` per view that constructs the view via its public
  parameterless ctor, reads `.Name`, and calls `GeneratedWriteMapperStore.Add(name, Mapper)` (mirrors
  Phase 1/2); no ctor → nothing emitted, store untouched (R3.*, R4.*, R5.*, R6.*, R11.*).
- **Build-time diagnostics (D122)**: `VISTA0030` (error, zero declared mappings), `VISTA0031` (error, once
  per non-scalar/navigation target), `VISTA0032` (error, once per key-field/concurrency-token target) —
  all **gate emission** (no mapper when any error fires for a view); `VISTA0033` (warning) names an
  unanalyzable `MapWritable` chain + the offending expression → build succeeds, view falls back to
  reflection. These replace the interim `ViewBuilderOfTCrud.ValidateWriteFacet` startup fail-fast guards
  (retired; the primary-key executability guard is retained). Category `a2n.Vista.SourceGenerators`,
  help-links under `docs/diagnostics/`, analyzer release tracking updated.
- **Runtime wiring unchanged**: no change to `EfViewExecutor`, `WriteMapperResolver`, or
  `GeneratedWriteMapperStore` (built in M12 to be filled by exactly this generator); `Resolve(view)`
  prefers the store (generated) over `ReflectionWriteMapper` (RUC fallback), and the executor never
  branches on mapper origin.
- **AOT + samples**: the AOT probe (`a2n.Vista.AotProbe`, IL2026/IL3050-as-errors) now drives a generated
  write mapper end-to-end (bind → generated mapper → executor) and asserts the generated type/members carry
  no `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`; new `src/Examples/a2n.Vista.GeneratorWriteMapperSample`
  is a real consumer assembly with representative views (one/many mappings, aliasing for R4.6 ordering,
  empty/no-op whitelist, nullable + `byte[]` scalars). The Northwind `vWritableMemo` view now gains a
  generated mapper; its unchanged Create → Update → Delete self-test runs through it (live coexistence +
  parity).

**Test-coverage caveat:** the emitter, analyzer, diagnostics, guard retirement, AOT probe, and the
Northwind generated-mapper self-test all landed and pass; some of the spec's **optional CsCheck property
tests** (oracle-parity master, store/resolver coexistence, deterministic-emission) remain deferred
(`.kiro/specs/source-generator-write-mapper/tasks.md`). Live parity is currently proven by the Northwind
write self-test running through the generated mapper plus the generator recognition/packaging tests.

**Still deferred after the write-DSL phase:** ~~`JsonSerializerContext`/serialization seam, OpenAPI (M18),
Style A (anonymous) accessor/serialization generation~~ — **the HTTP dispatch + serialization seam DONE
(2026-07-12, D123/D124; see §2.14).** Remaining: per-view `JsonTypeInfo` auto-generation, OpenAPI, Style A
coverage, and **bulk** write (v1.x; array body → 400). Style A write is out of scope by construction
(write requires a typed `TCrud`).

### 2.14 `source-generator-http-surface` — M9 Source Generator, HTTP-surface phase (landed; spec `.kiro/specs/source-generator-http-surface`)
Source-generator **HTTP-surface phase** (**D123** the generated typed HTTP dispatch invoker + Core-resident
`ViewInvokerStore`, **D124** the AOT-clean serialization seam), closing the last large reflection surface
for typed Style B — the full `request → authorize → execute → serialize` path is now IL2026/IL3050-clean.
Build green net8/9/10 (0 warnings besides a pre-existing unrelated CS8619 in the DataTablesNet adapter),
**267 tests/TFM** in `a2n.Vista.Tests` + **64 tests/TFM** in `a2n.Vista.SourceGenerators.Tests` (0
failed/skipped), AOT probe clean (zero IL2026/IL3050 on the full generated Style B HTTP round-trip),
Northwind **read + write** self-tests PASS — the write self-test now reports both `WriteMapper: GENERATED`
and `ViewInvoker: GENERATED`. The reflection path is the **behavioral oracle**; the master model-based
property (Property 1) proves the generated dispatch + serialization is byte-for-byte equivalent to it for
every request shape.

- **Core dispatch port + store (D123, Core stays EF-/HTTP-/STJ-free)** — `a2n.Vista.Core/Ports/`: the
  type-erased `IViewInvoker` port (`IsWritable` + `ListAsync`/`DetailAsync`/`CreateAsync`/`UpdateAsync`
  returning boxed shapes; `Delete` intentionally absent — the executor's `DeleteAsync` is non-generic),
  the `ViewInvocationListResult(BoxedResult, Rows, TotalRowsFiltered, TotalRowsUnfiltered)` record, and the
  process-wide, thread-safe, first-wins `ViewInvokerStore` (`Register`/`TryGet`), mirroring
  `ViewAccessorRegistry`/`GeneratedExecutionPlanStore`/`GeneratedWriteMapperStore`. Uses only Core + BCL —
  no STJ/EF/ASP.NET Core dependency added.
- **RUC boundary relaxed on the executor read facets** — `IViewExecutor.List/Detail/Create/Update<T>` lost
  their unconditional `[RequiresUnreferencedCode]`; the reflection fallback moved into private
  `*ReflectionAsync` helpers reached via a justified `[UnconditionalSuppressMessage]` (mirroring
  `WriteMapperResolver`), so the generated invoker rides the non-RUC compiled/write-mapper branches. The
  operator-visible RUC boundary stays at the `ViewRequestExecutor` HTTP entry point (D96 asymmetry
  preserved).
- **Third incremental generator** (`a2n.Vista.SourceGenerators`, `netstandard2.0`, no Vista project ref,
  FQN recognition): `ViewInvokerGenerator` (`IIncrementalGenerator`) — an independent pipeline recognizing
  typed Style B views, producing an equatable `ViewInvokerModel` (coverage flags: named `TQuery`, named
  writable `TCrud`, public parameterless ctor) tracked via `TrackingNames.ViewInvokerModel`. Emits, per
  covered view with a public parameterless ctor, a `file sealed` `<View>_VistaViewInvoker.g.cs`
  implementing `IViewInvoker` (closed generics, direct `await`, totals/rows by direct member access — no
  reflection) + one `[ModuleInitializer]` registering it into `ViewInvokerStore` keyed by the view's
  runtime `Name`. Read-only view → `IsWritable => false`, write members throw; no public parameterless
  ctor → nothing emitted (store untouched). Deterministic, byte-for-byte, Core + BCL only.
- **Non-blocking diagnostics (Info)**: `VISTA0040` (a recognized candidate with anonymous/`object`
  `TQuery` cannot receive dispatch → reflection fallback, build green) and `VISTA0041` (per covered view,
  naming the exact `[JsonSerializable]` set — `{ TRow, ViewListResult<TRow>, PagedResult<TRow> }` + `TCrud`
  iff writable with a named `TCrud` — to register via `AddVistaJsonContext(...)`). Category
  `a2n.Vista.SourceGenerators`, help-links under `docs/diagnostics/`, analyzer release tracking updated.
- **AOT-clean serialization seam (D124, `a2n.Vista.AspNetCore`)** — `VistaJson.Options` now installs a
  `TypeInfoResolverChain`: the shipped `VistaStaticJsonContext` (a real source-gen `JsonSerializerContext`
  over the fixed request/response envelopes + the now reflection-free polymorphic `FilterNode`) → any
  developer `App_Json_Context`(s) inserted by `AddVistaJsonContext(...)` → the opt-out
  `DefaultJsonTypeInfoResolver` reflection fallback (the only RUC serialization branch, removable via
  `DisableVistaReflectionSerializationFallback()`). A shared `VistaJsonWriter` resolves the runtime
  `JsonTypeInfo` and serializes with the AOT-safe overloads (byte-for-byte parity — same options config).
  `FilterNodeJsonConverter` write-side is now a manual `WriteValue` switch (reflection-free,
  source-gen-context compatible). Core gains no STJ dependency.
- **HTTP wiring (coexistence, parity)** — `ViewRequestExecutor` resolves an `IViewInvoker` from
  `ViewInvokerStore` after the unchanged one-door `AuthorizeAndShapeAsync` step; store hit → generated
  dispatch (adapter/export consume `ViewInvocationListResult.Rows`/totals, replacing `ToAdapterResult`),
  store miss → the private RUC `*ReflectionAsync` fallback; `DeleteAsync` stays a direct executor call.
  `VistaWriteBinding.BindModel` and the List/Detail/Export handlers (de)serialize through the seam
  (`VistaJsonWriter`), preserving status codes (200 / 404 for a null Detail) and byte-for-byte bodies. The
  one-door pipeline, auth, scope, hard limits, D120 write gates, and RFC 7807 error mapping are unchanged.
- **Verification** — the master oracle-parity property (Property 1: generated vs reflection, byte-for-byte
  over List/Detail/Export/Create/Update/Delete across the representative fixtures) plus supporting
  properties (store idempotence P6, resolver preference/coexistence P7, reflection-free/Core-only emission
  P4, deterministic emission P5, FilterNode round-trip P2, seam resolution order P3, VISTA0041 type-set P8,
  diagnostic conformance P9); the AOT probe extended to the full typed Style B round-trip with the
  reflection fallback removed; the Northwind self-test wired through the generated surface via a
  `NorthwindJsonContext`; and packaging/layering + RUC-confinement guard tests. Representative fixtures
  live in `src/Examples/a2n.Vista.GeneratorHttpSurfaceSample` (read-only single-key, composite-key, and
  writable-with-token views, each with a sample `App_Json_Context`).

**Two latent bugs found by the property tests and fixed:** (1) `FilterNodeJsonConverter.ReadValue` boxed
every JSON number as `double` (ternary type-unification) — losing int64 precision; fixed with an `(object)`
cast so int64 values round-trip as `long`. (2) The write-envelope deserialize briefly used the static
context's own case-sensitive options, dropping `model`/`key` and turning a denied write into 400 instead
of 403; fixed by routing the envelope deserialize through the case-insensitive seam (`VistaJson.Options`).

**Still deferred after the HTTP-surface phase (now partly closed):** per-view `JsonTypeInfo`
auto-generation (a `JsonSerializerContext`-equivalent via `JsonMetadataServices` — precluded from the clean
STJ route by the generator-of-generator constraint, so it is its own phase) is **DONE (2026-07-12,
D125/D126; see §2.15)** — the door D124 left open has been walked through, making the developer
`App_Json_Context` optional without changing the seam or the dispatch invoker. Remaining: OpenAPI (M18),
Style A (anonymous) serialization coverage (permanently RUC by D96), and **bulk** write (v1.x).

### 2.15 `source-generator-json-typeinfo` — M9 Source Generator, per-view `JsonTypeInfo` phase (landed; spec `.kiro/specs/source-generator-json-typeinfo`)
Source-generator **per-view `JsonTypeInfo` phase** (**D125** the generated per-view `JsonTypeInfo` provider
+ Core-resident, serializer-neutral `GeneratedJsonContextStore`, **D126** the seam integration that
auto-chains the generated contexts ahead of the reflection fallback), walking through the door D124 left
open: it makes the developer `App_Json_Context` **optional** for typed Style B without changing the seam or
the dispatch invoker. Build green net8/9/10, **281 tests/TFM** in `a2n.Vista.Tests` + **89 tests/TFM** in
`a2n.Vista.SourceGenerators.Tests` (0 failed/skipped), AOT probe clean on the full typed Style B round-trip
with **no developer context and the reflection fallback removed**, Northwind **read + write** self-tests
PASS with its `NorthwindJsonContext` **removed**. The reflection serializer is the **behavioral oracle**;
the master model-based property (Property 1) + the mandatory round-trip (Property 2) prove the generated
`JsonTypeInfo` (de)serializes byte-for-byte / value-equivalently to it.

- **Core store (D125, Core stays EF-/HTTP-/STJ-free)** — `a2n.Vista.Core/Metadata/GeneratedJsonContextStore`:
  a process-wide, thread-safe, first-wins idempotent store keyed by the view's runtime `Name`
  (`Register(string, object)` / `TryGet` / `All`), holding each generated context as an **opaque `object`
  handle** so Core references no `System.Text.Json` type (preserving the pluggable-serializer boundary
  `a2n.Vista.Newtonsoft` relies on). Mirrors `ViewAccessorRegistry`/`ViewInvokerStore`.
- **The generator-of-generator constraint** — a Roslyn generator cannot consume another generator's output,
  so Vista cannot emit a `[JsonSerializable]` `JsonSerializerContext` the built-in STJ generator would
  process. This phase resolves it the only clean way: it emits `JsonTypeInfo<T>` **by hand via**
  `System.Text.Json.Serialization.Metadata.JsonMetadataServices` — the same metadata factory the built-in
  generator emits into — which is why it is a standalone, higher-risk phase rather than a rider on D124.
- **Fourth incremental generator** (`a2n.Vista.SourceGenerators`, `netstandard2.0`, no Vista project ref,
  FQN recognition): `ViewJsonContextGenerator` (`IIncrementalGenerator`) — an independent pipeline with a
  fully equatable `ViewJsonContextModel` (`DtoTypeModel`/`DtoMemberModel` + the Emittable_Shape analysis)
  tracked via `TrackingNames.ViewJsonContextModel`. Per covered view with a public parameterless ctor it
  emits a `file sealed` `<View>_VistaJsonContext.g.cs` implementing `IJsonTypeInfoResolver`: `GetTypeInfo`
  dispatches to `JsonMetadataServices.CreateObjectInfo` + `CreatePropertyInfo<TMember>` factories for the
  Serializable_DTO_Set, **plus** auxiliary arms (collection-info helpers for the envelope `Items`
  `IReadOnlyList<TRow>` and collection members, `GetNullableConverter` for nullables, `CreateValueInfo` for
  scalar/string/`byte[]` leaves, and the AOT-safe generic `JsonStringEnumConverter<TEnum>` for enums) so the
  DTOs resolve with **no reflection fallback in the chain**; records / init-only / required members
  round-trip via the parameterized/`init` creator path; JSON property names honor the seam's naming policy
  for parity. One `[ModuleInitializer]` registers a singleton into `GeneratedJsonContextStore` keyed by
  `new View().Name`; a view without a public parameterless ctor emits nothing. Reflection-free,
  attribute-free, deterministic byte-for-byte, Core + BCL/shared-framework STJ only (no NuGet package, no
  ASP.NET Core dependency in the view assembly).
- **Emittable_Shape analysis + coverage** — walks each DTO's public serializable members and classifies
  each against the emittable set (BCL scalars, `string`, nullable value types, enums, `byte[]`, collections
  of an emittable element, the Vista `ViewListResult<TRow>`/`PagedResult<TRow>` envelopes, single-level
  nested emittable POCOs). Any member the analyzer cannot fully resolve → the view is **not covered**
  (safe default: parity over coverage) and falls back to the developer context / reflection.
- **Non-blocking diagnostics (never Error)**: `VISTA0050` (Info — covered view, per-view `JsonTypeInfo`
  generated; names the exact Serializable_DTO_Set now served so the `App_Json_Context` entry is optional)
  and `VISTA0051` (Warning — a candidate DTO member cannot be emitted reflection-free; no context emitted,
  the view falls back). Category `a2n.Vista.SourceGenerators`, help-links under `docs/diagnostics/`,
  analyzer release tracking updated. New diagnostic family begins at `VISTA0050`.
- **Seam integration (D126, `a2n.Vista.AspNetCore`)** — at the D124 seam-init site, `VistaJson` drains
  `GeneratedJsonContextStore.All`, casts each opaque handle to `IJsonTypeInfoResolver` (the single unchecked
  cast is the contract boundary), and inserts each into the `TypeInfoResolverChain` ahead of the developer
  `App_Json_Context`(s) and the reflection fallback, keeping `VistaStaticJsonContext` first (envelope
  precedence). No `JsonSerializerOptions` value, dispatch invoker, or public API
  (`AddVistaJsonContext(...)`/`DisableVistaReflectionSerializationFallback()`) changed;
  `VistaWriteBinding.BindModel` picks up the generated `TCrud` context automatically.
- **AOT + Northwind** — the AOT probe (`a2n.Vista.AotProbe`, IL2026/IL3050-as-errors) drives a full typed
  Style B round-trip (bind + List/Detail + write dispatch + serialize) using only `VistaStaticJsonContext`
  + the Generated_View_Context(s), with **no developer `App_Json_Context` and the reflection fallback
  removed**, and builds green; a Style A view stays RUC (D96 coexistence boundary). The Northwind example's
  `NorthwindJsonContext` registration was deleted and its read + write self-tests still pass on the
  generated per-view serialization.
- **Representative fixtures + properties** — `src/Examples/a2n.Vista.GeneratorJsonContextSample` (a
  read-only single-key view with scalar + nullable + enum + collection + `byte[]` members, a composite-key
  view, and a writable view whose `TCrud` is a record with required + init-only members). Properties:
  master oracle-parity (P1), round-trip (P2), seam resolution / context-optionality (P3), reflection-free
  attribute-free `JsonMetadataServices`-based source (P4), deterministic emission (P5), store first-wins
  idempotence (P6), and VISTA0050 coverage-set + diagnostic conformance (P7) — plus generator-driver
  recognition/shape-matrix and layering/cast-guard examples.

**One assumption to confirm in a later review:** `VISTA0051` was set to **Warning** severity (matching the
precedent of `VISTA0033`, the other "falls back to reflection" diagnostic); the Info-vs-Warning choice is
open for finalization. The opaque-handle Core store was chosen over a new `a2n.Vista.SystemTextJson`
package (keeps the one-Core-store-per-phase pattern and the view assembly free of a new Vista reference; the
cost is one unchecked cast at the AspNetCore drain, covered by a layering test).

**Still deferred after this phase:** ~~OpenAPI/Swagger (M18)~~ — **DONE (2026-07-13, D127/D128; see
§2.16).** Remaining: TypeScript client (M17) — a downstream consumer of the metadata + generated JSON
contexts + the OpenAPI document, now fully unblocked; Style A (anonymous) AOT serialization (permanently
RUC by D96); custom-converter synthesis for arbitrary member types; and **bulk** write (v1.x).

### 2.16 `openapi-emitter` — M18 OpenAPI emitter (landed; spec `.kiro/specs/openapi-emitter`)
The OpenAPI emitter (**D127** the runtime, metadata-driven document builder + the new opt-in
`a2n.Vista.OpenApi` package with its own deterministically serializable OpenAPI object model, **D128** the
opt-in serve endpoint + the optional ASP.NET Core OpenAPI pipeline provider). It emits an accurate,
complete, standards-conformant **OpenAPI v3.x** document for every Vista View mapped to HTTP, unblocking
Swagger UI, OpenAPI codegen, and the M17 TypeScript client. It is a pure **downstream consumer** of two
already-landed foundations it never modifies — the metadata model (`ViewMetadata`/`IViewRegistry`) and the
serialization seam (`VistaJson.Options`, D124/D126) — and is **off by default**. Build green net8/9/10
(0 warnings), **431 tests/TFM (net8) / 433 tests/TFM (net9/net10)** in `a2n.Vista.Tests` (0 failed/skipped;
the +2 on net9/net10 are the ASP.NET Core OpenAPI pipeline-provider tests) + **89 generator tests**
unchanged, AOT probe clean on an envelopes+`FilterNode`-only document, Northwind read + write + **OpenAPI**
self-tests PASS. The reflection serializer + the live route table are the **behavioral oracles**;
correctness rests on two parity disciplines (endpoint parity + schema/wire parity) with determinism as the
stabilizer.

- **New opt-in package (D48 layering)** — `src/a2n.Vista.OpenApi/` multi-targets net8.0/9.0/10.0 and
  references only `a2n.Vista.AspNetCore`; on net9.0/net10.0 it additionally references
  `Microsoft.AspNetCore.OpenApi` (TFM-guarded — the package does not exist for net8.0). No other Vista
  package references it (`a2n.Vista.Core`/`EntityFrameworkCore`/`AspNetCore` gain no OpenAPI dependency); an
  app opts in explicitly.
- **The two parity oracles.** (1) **Endpoint parity** — the live route table (`IViewRegistry`) is the
  oracle: the emitted operation set for a view equals exactly the HTTP endpoints Vista maps for it
  (`list`/`detail`/`metadata`/`export` always + `create`/`update`/`delete` iff `!IsReadOnly`), each on
  `ViewMetadata.Route`, correct method, unique `operationId = {view}_{facet}`, no path parameters (key/query
  ride in the body). (2) **Schema/wire parity** — the live serializer (the seam) is the oracle: every
  component schema describes the JSON the seam actually emits (camelCase names, enum-as-string, correct
  nullability, BCL type/format), verified **instance-against-schema**. Determinism (byte-for-byte, order
  independent of registration) is the stabilizer.
- **D127 — runtime, metadata-driven builder** (`VistaOpenApiDocumentBuilder`): builds from
  `IViewRegistry`/`ViewMetadata`, not by reflecting endpoint delegates (the Vista surface is a small fixed
  set of envelope-bodied actions whose meaning lives in metadata, so metadata is *more accurate*). A fixed
  facet→(method, path, request, success, errors, when) table is the single endpoint-parity source; structure
  (paths/operationIds/parameters/security/error responses/`$ref`s) is **AOT-clean and reflection-free**. The
  hand-authored `OpenApiDocument` object model (records under `Model/`) uses ordinal-ordered collections and
  its own source-gen `JsonSerializerContext` for AOT-clean, byte-stable output (chosen over a
  `Microsoft.OpenApi` dependency).
- **Reflection-free descriptors** — hand-authored `OpenApiSchema` values for the fixed Vista envelopes
  (`VistaListRequestBody`/`VistaDetailRequestBody`/`VistaWriteRequestBody`/`VistaWriteResponse`/
  `VistaMetadataResponse`/`VistaFieldMetadataResponse`/`ViewListResult<TRow>`/`PagedResult<TRow>`) +
  `ProblemDetails`, and the polymorphic `FilterNode` as a `oneOf` of the leaf/and/or/not schemas with a
  discriminator + recursive `$ref`, matching `FilterNodeJsonConverter`. Property names are authored under
  (and parity-checked against) the seam's naming policy.
- **The one RUC branch (D96 asymmetry)** — `DtoSchemaGenerator` reflects over per-view `TRow`/`TCrud`/
  nested-POCO CLR types under the seam options to produce wire-matching schemas (enum→string+members,
  nullable→`nullable`, BCL scalar→`type`/`format`, collection→`array`+`items`, single-level nested POCO→its
  own component + `$ref`). An unresolvable member yields a permissive `{}` schema + a **non-fatal notice**
  (via `ILogger`) — never omitted, never thrown — so document validity beats one member's completeness. All
  reflection is confined to this `[RequiresUnreferencedCode]` branch. **No new `VISTA####` diagnostics** —
  the emitter is not a source generator.
- **Security + errors** — when not anonymous (`VistaEndpointOptions.AllowAnonymous == false`) the configured
  (or default HTTP `bearer`) scheme is emitted and attached to every operation; when anonymous, none. Every
  body operation documents `400`, every operation `403` (when not anonymous), detail/update/delete `404`,
  and update/delete `428`/`409` for a token-bearing writable view — all referencing the single
  `ProblemDetails` schema with media type `application/problem+json`; metadata `If-None-Match`/`ETag`/`304`
  is documented when caching is enabled, and write `ETag` headers on update.
- **D128 — opt-in serving** — `AddVistaOpenApi(configure?)` registers the builder, validated
  `VistaOpenApiOptions` (title; `DocumentVersion` defaulting to the assembly informational version;
  `OpenApiVersion` default `3.0.4`; `EndpointPath` default `/openapi/v1.json`; `Security`;
  `IncludeAdapterEndpoints=false`), and a build-once `VistaOpenApiDocumentCache` (all singletons); options
  are validated **at registration** (fail-fast `ArgumentException`). `MapVistaOpenApi()` maps
  `GET {EndpointPath}` returning the cached JSON as `application/json` **inside** the host auth pipeline
  (bypasses nothing; does not call `AllowAnonymous()`). Both APIs carry `[RequiresUnreferencedCode]` (honest
  RUC propagation; the build is deferred to first request). On net9.0/net10.0 an optional
  `VistaOpenApiDocumentTransformer` merges the Vista `paths`/`components` into an app's built-in
  `Microsoft.AspNetCore.OpenApi` pipeline document; net8.0 keeps only the Vista serve endpoint.
- **Additive-only, no wire change (R10)** — only the serve endpoint is added; every existing response is
  byte-for-byte unchanged; with neither call made, nothing is added. Adapter-endpoint documentation
  (D111–D116) is **out of scope for v1** (extension hook only) — no adapter path appears in the document.
- **Verification** — the two master parity properties (endpoint parity over random registries; DTO +
  envelope schema/wire parity instance-against-schema) plus referential-integrity, OpenAPI-3.x-validity,
  determinism/order-independence, security-posture, error-response, and adapter-endpoint-absence properties
  (CsCheck via TUnit, ≥100 iterations); the AOT probe extended to an envelopes+`FilterNode`-only document
  (the RUC `DtoSchemaGenerator` is not reached on that path); layering/packaging guards; and the Northwind
  net8.0 OpenAPI self-test (`GET /openapi/v1.json` → 200, `openapi 3.0.4`, 15 paths, endpoint parity with 0
  missing/phantom, byte-for-byte coexistence). Representative fixtures live in the test project
  (`src/Tests/a2n.Vista.Tests/OpenApi/EmitterFixtures.cs` + `RegistryGenerators.cs`).

**One assumption to confirm in a later review:** decision numbers **D127/D128**; the default OpenAPI version
`3.0.4` (vs 3.1); the default HTTP `bearer` security scheme and `/openapi/v1.json` serve endpoint; the
hand-authored object model (vs a `Microsoft.OpenApi` dependency); DTO schema generation being reflection-
based/RUC in v1 (a `JsonTypeInfo`-driven reflection-free path deferred); adapter endpoints out of scope for
v1; and single-level nested-POCO schema depth (matching the D125 posture).

**Still deferred after this phase:** Style A (anonymous) serialization coverage (permanently RUC by D96;
spec `style-a-coverage`), the M17 **TypeScript client** (now fully unblocked on this document + the generated
per-view contexts), reflection-free DTO schemas via `JsonTypeInfo` (v1 does not claim them), grid-adapter
endpoint documentation, and **bulk** write (v1.x).

### 2.17 `style-a-coverage` — M9 Source Generator, Style A coverage (landed; spec `.kiro/specs/style-a-coverage`)
The **final planned M9 Source Generator phase** (**D129** Style A recognition + shape-driven emission for the
nameable subset, **D130** the reaffirmed permanent by-design RUC boundary + the coverage diagnostics + the
AOT-probe asymmetry demonstration). Build green net8/9/10, **448 tests/TFM (net8) / 450 tests/TFM
(net9/net10)** in `a2n.Vista.Tests` + **112 generator tests** (0 failed/skipped), AOT probe clean on the
covered slice, Northwind read + write + OpenAPI self-tests PASS unchanged. The reflection path is the
**Behavioral_Oracle**; byte-for-byte / value-for-value parity is the master guard.

- **The wall that bounds scope (why coverage is narrow, not broad).** A C# **anonymous type has no
  source-writable name** (`<>f__AnonymousType0` is not valid C# and is not stable across assemblies), so a
  generator cannot emit `((AnonymousRow)row).Field` accessors, `Expression<Func<AnonymousRow,T>>`, or
  `JsonTypeInfo<AnonymousRow>`. This is the exact wall that made Phases 1–5 skip Style A; **D96 keeps it
  permanent.** This phase covers only the parts of Style A that *are* nameable and surfaces the rest as an
  explicit, non-blocking, by-design RUC boundary.
- **What is nameable (the honest, bounded feasibility).** Reading `src/a2n.Vista.Core/Authoring/`: the read
  `TRow` of `AddView<TRow>(name, projection)` may be **named** (generatable) or **anonymous** (not); the
  write `TCrud` of a chained `.WithCrud<TCrud, TEntity>()` is **always named** (the authoring surface forbids
  an anonymous write model, D38), so **the write model of *every* writable Style A view is nameable** — the
  broadest, most valuable win. The view name must be a **compile-time constant** to key artifacts statically.
- **The fifth incremental generator** (`StyleAShapeGenerator`, `a2n.Vista.SourceGenerators`, `netstandard2.0`,
  references no a2n.Vista project, FQN recognition): the first generator to key off an
  **`InvocationExpressionSyntax`** (an `AddView` call) rather than a class declaration. Semantic transform
  keeps invocations resolving to `a2n.Vista.Authoring.IViewTemplateBuilder<TDbContext>.AddView<TRow>` whose
  enclosing type derives `ViewTemplate<TDbContext>`, walks a chained `WithCrud<TCrud, TEntity>()`, and
  produces a fully equatable `StyleAViewModel` (reusing `EquatableArray<T>`/`LocationInfo`, tracked via
  `TrackingNames.StyleAViewModel`) so an unrelated edit does not regenerate every view's artifacts.
- **Shape-only artifacts (no projection reconstruction), reusing the prior emitters.** For a covered view it
  emits — into the **template's own assembly**, keyed by the **constant** `AddView` name (the D129 difference
  from D125's `new View().Name` keying): (a) a `file static` **export accessor map** (cast + member read) +
  `[ModuleInitializer]` → `ViewAccessorRegistry` (D117 shape) for a **named** `TRow`; (b) a `file sealed`
  per-view **`IJsonTypeInfoResolver`** built via `JsonMetadataServices` (NOT `[JsonSerializable]`) covering
  `TRow`/`ViewListResult<TRow>`/`PagedResult<TRow>` **when `TRow` is named + emittable** and `TCrud`
  **when writable + emittable**, + `[ModuleInitializer]` → `GeneratedJsonContextStore` (D125 shape). The
  Emittable_Shape set is inherited verbatim from D125 (single-level nested POCO depth); a non-emittable member
  falls back (`VISTA0063`) rather than emitting a best-effort context that could drift from the oracle —
  correctness beats coverage.
- **No new store, no new seam.** Only new store *entries* (keyed by Style A view names) are added: the
  existing `a2n.Vista.AspNetCore` D126 drain chains a Style A context unchanged, and
  `ExportColumns.Value(view.Name, row, field)` prefers a Style A accessor unchanged. `a2n.Vista.Core` gains
  no `System.Text.Json`/EF/ASP.NET Core dependency; generated code is emitted into the template assembly with
  no ASP.NET Core dependency.
- **The D96 asymmetry, demonstrated within one view.** For a writable view with an **anonymous** read row and
  a named `TCrud`, the write body binds **AOT-clean** through the generated `TCrud` `JsonTypeInfo` while the
  anonymous read row can only serialize through the RUC reflection path — no read-side artifact is generated
  for it (`VISTA0061`). The AOT probe (`a2n.Vista.AotProbe`, IL2026/IL3050-as-errors) drives exactly this:
  named-row export + read-DTO serialization AOT-clean, the writable anonymous-row `TCrud` write AOT-clean,
  and the anonymous read row isolated behind a narrowly-scoped suppression whose sole purpose is to
  *demonstrate* (not remove) the boundary.
- **Diagnostics (all non-blocking, category `a2n.Vista.SourceGenerators`, help pages under
  `docs/diagnostics/`, recorded in `AnalyzerReleases.Unshipped.md`):** `VISTA0060` (Info — covered view,
  naming the exact artifact set), `VISTA0061` (Info — anonymous/`object` read row → read stays RUC by design,
  D96), `VISTA0062` (Info — non-constant `AddView` name → cannot key statically), `VISTA0063` (Warning —
  non-emittable DTO member → no `JsonTypeInfo` for that DTO, view falls back, build succeeds).
- **Verification** — the master oracle-parity property (Property 1: serialize + deserialize byte-for-byte vs
  the reflection oracle), the DTO round-trip (Property 2), export-accessor value parity (Property 3), seam
  resolution + developer-context optionality (Property 4), reflection-free/attribute-free/`JsonMetadataServices`
  source (Property 5), deterministic emission (Property 6), store first-wins idempotence for Style A keys
  (Property 7), and diagnostic conformance + the `VISTA0060` coverage set (Property 8) — CsCheck via TUnit,
  ≥100 iterations over compile-once representative fixtures (`a2n.Vista.GeneratorStyleASample`: a read-only
  named-row view, a writable named-row view whose `TCrud` uses record/init-only/required members, and a
  writable anonymous-row view with a named `TCrud`); generator-driver/snapshot tests for the recognition +
  coverage matrix; generator + runtime packaging/layering guards; and the AOT probe + non-regression run.

**Assumptions confirmed for this spec (per "code is the source of truth"):** decision numbers **D129/D130**;
diagnostic ids **VISTA0060–VISTA0063** (with `VISTA0063` = Warning, the rest Info). Deferred (not requirements
here): making anonymous Style A serialization AOT-clean (impossible + D96); Style A filter/sort member-access
/ executable-plan generation (the Phase-2 plan bundles a compile-time projection Style A expresses as a
runtime delegate); anonymous-projection promotion to a generated named type; custom-converter synthesis. With
this phase landed, **M9 (the Source Generator, Pillar 3) is complete** — every planned generator phase has
shipped.

### 2.18 `typescript-client` — M17 TypeScript client generator (landed; spec `.kiro/specs/typescript-client`)
The TypeScript client generator (**D131** the OpenAPI document as the single generation source over a
one-way buffered pure pipeline, **D132** the secure-by-default read-first / write-gated client posture), a
standalone **.NET CLI executable** at `src/a2n.Vista.Client.TypeScript`. It is a pure downstream consumer of
the M18 OpenAPI surface — it references **no** Vista package and changes no server route, envelope, header,
error shape, or behavior (additive-only). Build green net8/9/10, a **new `a2n.Vista.Client.TypeScript.Tests`
= 136 tests/TFM** (0 failed/skipped); the existing suites are unchanged (**448/450 tests/TFM** in
`a2n.Vista.Tests` + **112** in `a2n.Vista.SourceGenerators.Tests`), and the Northwind read + write + OpenAPI
self-tests PASS unchanged. The emitted OpenAPI document is the **authoritative oracle** for all parity checks.

- **Pipeline (D131)** — a one-way, buffered, pure pipeline **acquire → parse → resolve → model → emit →
  write**; no stage mutates an earlier stage's output, which makes determinism (Requirement 9) and
  all-or-nothing failure (Requirements 1/9/10) structural rather than bolted on. Every `GeneratedFile` is
  buffered in memory before the write stage; any failure aborts with a nonzero exit and leaves prior output
  untouched.
  - **Acquire** — `FileSource` (local path; unreadable → typed `AcquireError.FileUnreadable`) and
    `HttpsSource` (HTTPS-only GET, 30s timeout; failure/non-success → typed `AcquireError.Fetch`).
  - **Parse** — `OpenApiParser`: JSON → internal `OpenApiDocument`; rejects an `openapi` version outside
    3.0.x–3.1.x (`ParseError.UnsupportedVersion`) and malformed docs (`ParseError.Malformed(location, …)`).
  - **Resolve** — `RefResolver`: resolves every local `#/components/{schemas|securitySchemes}` `$ref` to a
    name-keyed graph, ignores `$ref` siblings (3.0 semantics), preserves cyclic refs (`FilterNode`) as
    by-name edges; dangling ref → `ResolveError.Dangling`.
  - **Model** — the client model builder: `EnvelopeCatalog` (locates the fixed Vista envelopes; a missing
    required envelope → fatal `MissingSchema(name)`), `EnvelopeReLifter` (structurally matches each
    monomorphized `ViewListResult_*` against the template and collapses it back to one generic
    `ViewListResult<TRow>`/`PagedResult<TRow>` per view), the presence-discriminated `FilterNode` model
    (bare `oneOf`, no `discriminator` — narrowed by required members), per-view `TRow`/`TCrud` DTOs via
    `TypeMapper`, the operation graph (facets present per view + `ConcurrencyMode` from 428/409), and the
    per-operation security posture.
  - **Emit** — deterministic emitters keyed by `DeterministicOrder` (ordinal, case-sensitive by declared
    name): `types.ts`, `filter-node.ts`, the framework-agnostic runtime (`runtime/http-transport.ts`,
    `auth.ts`, `result.ts`, `url.ts`, `client-context.ts`), one `views/{view}.ts` per view (read facets
    always; write facets only when the opt-in flag is set **and** the view is writable), plus the `index.ts`
    barrel and an English `README.md`.
  - **Write** — `OutputWriter`: creates the output dir if absent, pre-checks a writable directory, stages to
    a temp area then moves/replaces atomically, fixed UTF-8 (no BOM) + `\n` for every file; a write failure
    aborts, removes staging, and leaves prior output intact.
- **Type mapping (D131)** — `TypeMapper` maps the OpenAPI scalar `type`/`format` table to TS
  (`integer`/`number`→`number`, `boolean`→`boolean`, `string`(+`uuid`/`date-time`/`byte`)→`string`),
  string-enum → literal union in document order, `nullable` → `| null`, not-required → optional `?`,
  verbatim case-sensitive property names, array → `T[]`; a permissive `{}`/unknown scalar → `unknown` + a
  non-fatal notice (never omitted, never fatal).
- **Client posture (D132)** — the emitted client routes every request through an injectable `HttpTransport`
  (default `fetch`; construction fails if none and `fetch` is unavailable), joins base URL + operation path
  with exactly one `/`, and never embeds a credential (bearer via an injectable `AuthProvider`); a secured
  operation with no provider short-circuits to a typed `unauthorized` without sending. Base-URL validation:
  absent/empty/invalid → fail construction; non-HTTPS loopback → warn + continue; non-HTTPS non-loopback →
  typed config failure. Every outcome is one discriminated `ClientResult<T>`
  (`success`/`problem`/`unauthorized`/`not-found`/`precondition-required`/`precondition-failed`/
  `transport-error`/`unexpected`) — total, never throwing; write ops surface the 428/409 concurrency
  outcomes distinctly.
- **CLI** — `CommandLine`/`Program`: args → `GenerationConfig` (source location, output dir, write-facet
  flag defaulting **off**); missing required value → usage + nonzero exit; success → zero exit reporting the
  output dir + view count + notices.
- **Two reconciliations against the live emitter (code is the oracle):** (1) M18 emits `FilterNode` as a
  bare `oneOf` with **no** `discriminator` (the server discriminates by member presence), so Requirement
  2.2's literal "using the document's `discriminator`" cannot apply — the generator emits a
  **presence-discriminated** union honoring the *intent* (a value narrows to one member). (2) M18
  **monomorphizes** row-parameterized envelopes (`ViewListResult_{Row}`) rather than emitting a generic —
  the generator **re-lifts** them into one generic TS type per view (the single most important modeling
  step). Both are recorded in the spec's design as deviations confirmed at review.
- **Verification** — the C# generator side uses **CsCheck** on the TUnit runner (the repo convention; the
  design's FsCheck mention is superseded), run via `.kiro/tools/run-tests.ps1` across net8/9/10; the
  generated-runtime side uses **fast-check** under Node against the emitted client. The 20 design
  correctness properties are each their own property test (determinism/idempotence, type-mapping fidelity,
  `$ref` soundness, generic re-lifting, write-facet gating, missing-envelope/unsupported-version aborts,
  response-classification totality, authorization enforcement, generated-type round-trip, request fidelity,
  base-URL posture, transport routing/no-retry, no-UI/grid-dependency, no-embedded-credential, and the two
  headline parity harnesses — round-trip + schema parity — with the document as the oracle). Test fixtures
  under `Fixtures/` (valid Vista document, malformed, unsupported version, dangling `$ref`, missing
  envelope); the TS runtime harness under `src/a2n.Vista.Client.TypeScript/tests/ts-runtime`.

**Deferred (recorded, not requirements here):** grid-adapter endpoints (`/datatable`, `/querybuilder`, …;
M18 does not document them in v1); a metadata-driven (in-process `ViewMetadata`) generation mode; framework
bindings (React/Vue/Angular/grid data sources); runtime request/response validation beyond TS compile-time
types; npm packaging/publishing; and wire versioning (D99 — the document is unversioned = latest).

### 2.19 M19 — CI + NuGet publish workflows (landed; no spec)

Two GitHub Actions workflows under `.github/workflows/` (additive-only; no source, wire, route, or
package-content change). No decision numbers — pure operational tooling.

- **`ci.yml`** — `name: CI`, triggered on `push`/`pull_request` to `main` + `workflow_dispatch`.
  - Job **build**: `actions/setup-dotnet@v4` (8.0.x/9.0.x/10.0.x) → `dotnet restore` → `dotnet build
    src/a2n.Vista.slnx -c Release` — a full-solution compile check across every project/adapter/example on
    all TFMs.
  - Job **test**: a `fail-fast: false` matrix over `net8.0`/`net9.0`/`net10.0`, running each of the three
    TUnit suites via `dotnet run --project <suite> -c Release --framework <tfm>` (**not** `dotnet test`, per
    the repo convention / §8; a non-zero exit fails the leg). No Node is needed — the TS-client suite's
    correctness gates are C#/CsCheck; the fast-check runtime harness is out of the automated `dotnet run`
    path.
- **`publish.yml`** — `name: publish`, triggered on `release: [published]` (the tag drives the version,
  leading `v` stripped) + `workflow_dispatch` (explicit `version` input). Restores, builds Release with
  `-p:Version=`, packs each shipping project `--no-build -o artifacts`, then pushes with `--skip-duplicate`.
  - **NuGet Trusted Publishing (OIDC)** — `permissions: id-token: write`; `NuGet/login@v1` exchanges the
    GitHub OIDC token for a short-lived (~1 h) nuget.org API key (`steps.login.outputs.NUGET_API_KEY`); the
    `user:` input is the `NUGET_USER` secret (the nuget.org **account/profile name**, not an email). No
    long-lived API key is stored. The registered Trusted Publishing policy's **Workflow File** must be
    `publish.yml` (GitHub Actions only runs `.yml`/`.yaml`).
  - **Scope — 8 implemented libraries:** `a2n.Vista.Core`, `.EntityFrameworkCore`, `.AspNetCore`,
    `.OpenApi`, `.EntityFrameworkCore.Npgsql`, `.Adapters.DataTablesNet`, `.Adapters.AgGrid`,
    `.Client.TypeScript` (the last ships as a `dotnet tool`, command `vista-ts`).
    **Excluded:** the empty scaffolds (`a2n.Vista.Newtonsoft` + the MudBlazor/OData/GraphQL/PrimeNG/
    Syncfusion/TanStackTable/Telerik adapter shells — `AssemblyMarker.cs` only), and
    **`a2n.Vista.SourceGenerators`** — not shipped standalone; it is **bundled into `a2n.Vista.Core`**
    (packed under `analyzers/dotnet/cs`) so consumers get the generator transitively.

**Settled (was "open follow-ups"):** (1) **source-generator packaging** — bundled into `a2n.Vista.Core`
under `analyzers/dotnet/cs` (verified: a local-feed package consumer's build emits the accessor/invoker/
json-context generators); (2) **`a2n.Vista.Client.TypeScript`** now ships as a `dotnet tool`
(`PackAsTool`, command `vista-ts`); (3) `<Version>` is `0.0.1-beta.2` in `Directory.Build.props`, but a
real release still cuts the version from the Git tag. Every shipping package also carries the brand icon
and a per-package `README.md` for its nuget.org page. **Verification** is the first green Actions run
(workflows cannot be exercised locally).

### 2.20 `ag-grid-adapter` (landed; spec `.kiro/specs/ag-grid-adapter`) — M16

The **second** Pillar 2 client-half grid adapter (D133–D136), proving the neutral `IViewAdapter` contract
generalizes to a grid whose request shape differs substantially from DataTables. Purely additive at the
adapter + sample layer: **no Core/EF/AspNetCore type is added, changed, or forked**, and the AspNetCore
adapter glue is reused **verbatim** (only a new `RouteSuffix` and the JSON-body read path are new).

- **`a2n.Vista.Adapters.AgGrid`** (Core-only, D48): `AgGridModels.cs` (the `IServerSideGetRowsRequest`-shaped
  `AgGridRowsRequest`/`AgGridSortModel` + the `{rowData,rowCount}` `AgGridRowsResponse`),
  `AgGridJsonContext.cs` (source-gen STJ context, `PropertyNameCaseInsensitive`; AOT-clean — anonymous
  Style A rows ride the documented D96 RUC reflection path, **no new reflection path**),
  `AgGridFilterModelParser.cs` (the pure `filterModel` → `FilterNode` parser), and `AgGridAdapter.cs`.
- **D133 — the adapter surface.** `AgGridAdapter : ViewAdapter<AgGridRowsRequest, AgGridRowsResponse>`,
  `Id="aggrid"`, `RouteSuffix="aggrid"` → exposed at `POST {route}/aggrid` through the existing DataTables
  path (`AddVistaAdapter<AgGridAdapter>()`). Three pure, deterministic steps: `BindRequest` (guards a
  non-blank JSON body, deserializes via the source-gen context wrapping `JsonException`, validates the block
  range, defaults absent `sortModel`/`filterModel` to empty, reads the quick filter from `Values["q"]` capped
  at 1,024 chars), `ToQuery`, `ToResponse`.
- **D134 — `filterModel` → `FilterNode` (locked table).** text/number/date `type`s, `set` → `In`, `inRange`
  → `Between` (both bounds required else `AdapterBindException`), `blank`/`notBlank` → `IsNull`/`FilterNot`,
  and combined `AND`/`OR` of 2+ conditions → `FilterAnd`/`FilterOr` preserving order. **Advanced Filter is
  deferred for v1** — an Advanced-Filter payload is rejected **loudly** (`AdapterBindException` → 400
  `adapter-bind-failed`), never silently dropped (D67 posture).
- **D135 — block paging + response.** `PageSize = EndRow - StartRow`; `Page = StartRow / PageSize` when
  positive; a non-positive `PageSize` is **passed through unchanged** so the engine rejects it (no
  clamp/default). The response is `{rowData = result.Rows, rowCount = result.RecordsFiltered}` — `rowCount`
  is the **filtered** total (AG Grid's `LoadSuccessParams` uses it for last-block detection); `RecordsTotal`
  is not surfaced.
- **Channel isolation (D111).** `filterModel` lands only in the `Filter` slot (AND-of-columns); a non-empty
  quick filter becomes a `FilterOr` of `Contains` over `IsSearchable && string` fields in the `Search` slot
  (mirrors DataTables' `BuildGlobalSearch`); `Scope` stays `null`. The adapter never enforces the
  tri-whitelist and drops nothing beyond skipping a non-field `colId` — disallowed leaves reach the engine
  and are rejected per-channel with 400.
- **D136 — sample composition.** Quick-filter transport is `?q=` folded into `AdapterRequest.Values` (zero
  host change; keeps the JSON body a byte-faithful `IServerSideGetRowsRequest`). The sample's front-end is a
  **thin hand-written `IServerSideDatasource`** because the M17 generated TS client is OpenAPI-driven and
  adapter endpoints are not (yet) in the OpenAPI document (the generated client can still supply the row DTO
  types). `src/Examples/a2n.Vista.Examples.AgGridNorthwind` (net8.0-only): ASP.NET host reusing
  `NorthwindDbContext` + views, `client/` TypeScript (`tsc --noEmit` gate) emitting into `wwwroot`, and a
  guarded `dotnet run -- selftest` round-trip asserting the `{rowData, rowCount}` shape/values.
- **Verified (2026-07-14):** build green net8/9/10; **515 tests/TFM (net8) / 517 tests/TFM (net9/net10)** in
  `a2n.Vista.Tests` (+67/TFM — 8 CsCheck properties ≥100 iters covering purity/determinism, block paging,
  sort-model order, `filterModel` fidelity, channel isolation, response mapping, bind fidelity, and JSON
  round-trip, plus parser edge-case, `BindRequest`, `ToQuery`, and glue-integration unit tests) + **112
  generator tests** unchanged (0 failed/skipped); Northwind read + write + OpenAPI self-tests PASS unchanged;
  the new AG Grid sample self-test PASSES.

### 2.21 `northwind-sample-showcase` (landed; spec `.kiro/specs/northwind-sample-showcase`) — D137–D140

A multi-page runnable demo of the Pillar-2 client adapters, reaching feature parity with the legacy DynData
`Northwind.WebUI` "Table Browser" on the **read** surface — but on the landed Vista contracts and the
secure-by-default posture (only explicitly-registered views are browsable). **Purely additive at the
sample/example layer**: no Core/EF/AspNetCore/adapter contract, route, envelope, or error shape changes.

- **D137 — composition.** The single `a2n.Vista.Examples.AgGridNorthwind` host (net8.0-only) serves all
  three pages behind a shared nav and registers every adapter family it needs —
  `AddVistaAdapter<DataTablesAdapter>()` + `AddVistaMetadataAdapter<QueryBuilderSchemaAdapter>()` +
  `AddVistaAdapter<AgGridAdapter>()` + `AddVistaOpenApi()` — keeping `AllowAnonymousAccess()` (D94). The
  standalone `a2n.Vista.Examples.Northwind` host stays the separate DataTables-only single-view sample.
  (This **revised** the earlier draft of D137, which had proposed extending the `Northwind` host.)
- **D138 — view-catalog exposure.** A new app-level, additive read-only endpoint `GET /api/showcase/views`
  (a minimal-API `MapGet` inside the existing pipeline) returns `ShowcaseCatalog.Project(registry)`: a pure
  static `IReadOnlyList<ViewCatalogEntry> Project(IViewRegistry)` that maps `registry.All` one-to-one to
  `ViewCatalogEntry(Name, Route, Title)` (title humanized: strip a leading `v`, space before capitals),
  returning `[]` for an empty registry. Secure-by-default (only registered views); no arbitrary tables.
  There was no HTTP "list all views" endpoint before this.
- **D139 — page technology.** Static HTML + TypeScript compiled by `tsc` (no bundler), emitting ES modules
  into `wwwroot/js`; a `tsc --noEmit` typecheck gate over ambient CDN declarations (`globals.d.ts`) keeps
  it green with no heavyweight `@types`. Three pure transforms are property-tested with **fast-check** under
  Node (`columns.ts` metadata→columns bijection; `search.ts` min-length gate) and one with **CsCheck** in
  `a2n.Vista.Tests` (`ShowcaseCatalog.Project` catalog↔registry bijection, Property 2).
- **D140 — registered view set.** A third read-only view `vOrder` (over `db.Orders`, PK `OrderId`, date
  `OrderDate`/`RequiredDate`/`ShippedDate`, numeric `Freight`, string `ShipCountry`/`ShipCity`/`ShipName`,
  hidden FK columns) so `vProductCategory`/`vOrderDetail`/`vOrder` collectively span
  string/numeric/date/FK/composite-key. `vProductCategory` and `vOrderDetail` unchanged.
- **The three pages.** **Simple Wiring** — the preserved AG Grid community grid (infinite/server-side row
  model → `POST {route}/aggrid`, server-side paging/sort, `?q=` quick filter). **View Browser** (DynData
  parity) — DataTables.NET + jQuery-QueryBuilder: a `View_Selector` from the catalog, grid auto-rebuild with
  columns discovered from `GET {route}/metadata`, server-side paging + global search (min-length gated) +
  single/multi sort + a `GET {route}/querybuilder`-driven advanced filter, all combined into one
  `POST {route}/datatable` request (each in its own channel); a view switch disposes the prior
  DataTable/QueryBuilder (no state leak); a placeholder selection issues no request; an RFC 7807 error is
  surfaced with rows left unchanged. **Custom Renderer** — an AG Grid community page with ≥2 consumer-owned
  `cellRenderer`s (formatted price, Discontinued badge, link), presentation-only over a server-side view.
- **Self-test.** The old standalone `AgGridSelfTest` was removed when the showcase took over the host; the
  host self-test (`dotnet run -- selftest`) now runs read (incl. a **view-browser round-trip** combining
  paging + global search `'ch'` + multi-sort `CategoryName,ProductName` + `jsonQB UnitPrice>=20`) + write +
  OpenAPI, all PASS.
- **Verified (2026-07-14):** build green net8/9/10; **516 tests/TFM (net8) / 517 tests/TFM (net9/net10)** in
  `a2n.Vista.Tests` (+1 — the `ShowcaseCatalog` CsCheck Property 2; the two fast-check properties run under
  Node) + **112 generator tests** unchanged (0 failed/skipped); the showcase host read + write + OpenAPI
  self-tests PASS (19 OpenAPI paths, 4 views incl. the writable memo); the other example self-tests stay
  green and unchanged.

### 2.22 Audit remediation, tranche 1 (landed 2026-07-31; report `docs/audit/2026-07-31-full-code-audit.md`)

The 2026-07-31 full code audit reported 36 findings (6 security, 13 correctness, 9 dead code, 8 performance)
against a zero-error build. **Tranche 1 — every finding whose fix is self-contained and needs no new contract
decision — is fixed**, with a regression test per finding (`AuditRemediationTests`,
`PathTraversalContainmentTests`, plus new cases in `OpenApiServingCoexistenceTests` and
`WriteMapperDiagnosticsTests`). Two behaviour-visible defaults changed: **D141** (fail-closed Style A scope)
and **D142** (authorized-by-default OpenAPI document endpoint).

Fixed: `SEC-01` (D141), `SEC-02` posture (D142), `SEC-03` (hidden fields no longer published in
`components.schemas`; maskable fields annotated), `SEC-05` (`.Key(nameof(...))` now guarded — the key
recognizer resolves compile-time constants through the semantic model), `SEC-06` (view-name validation +
output-path containment in the TypeScript client generator), `BUG-01` (typed filter-value mismatch → 400, not
500), `BUG-06` (leak-free write bind messages), `BUG-08` (OpenAPI components keyed by type identity),
`BUG-09` (`Key(...)` satisfies the write facet's key requirement), `BUG-11` (CSV formula-injection defence +
XLSX XML-illegal character stripping), `BUG-12` (negated empty QueryBuilder group), `BUG-13` (net10
`additionalProperties`), `DEAD-04` (absolute export cap enforced), `PERF-01` (one full payload copy removed).

Still open, each needing a decision or a port change before code: `SEC-04` (masked fields sortable — needs a
D95-adjacent decision), `BUG-02` (adapter paging contract), `BUG-03` (authorize before bind), `BUG-04`/`BUG-05`
(atomic concurrency token + post-write token round-trip), `BUG-07` (`AsNoTracking` + non-destructive masking),
`BUG-10` (record equality vs mutable key state), the remaining `DEAD-*` (API removals) and `PERF-*` items.
The audit report carries the per-finding status table.

- **Verified (2026-07-31):** build green net8/9/10 (Release + Debug); **527 tests/TFM (net8) / 528 (net10)** in
  `a2n.Vista.Tests`, **143** in `a2n.Vista.Client.TypeScript.Tests`, **114** generator tests — 0 failed,
  0 skipped. Note: the audit's "0 warnings" claim does not hold on the current SDK — `DataTablesAdapter.cs:92`
  (CS8619) and three `TUnitAssertions0015` warnings pre-date this work (confirmed at `HEAD`).

### 2.23 Audit remediation, tranche 2 (landed 2026-07-31; D143–D146)

The findings that needed a **decision** before code. Each is now settled and implemented, with regression
tests (`AuditRemediationTests`, `ConcurrencyTokenGuardTests`, new cases in
`WriteEndpointAuthorizationExampleTests`, updated `AgGrid*` paging tests and `ConcurrencyAbortPropertyTests`).

- **D143 (`SEC-04`)** — masked fields default **non-sortable**, closing the `ORDER BY` + paging probing vector
  D95 left open. The `Sortable(...)` opt-in still wins. Mirrored in `ViewAccessorGenerator`, so generated
  metadata stays byte-identical to the reflection oracle (the masked field's member accessor is no longer
  emitted — the `PersonView_VistaExecutionPlan` golden was updated accordingly).
- **D144 (`BUG-02`, `DEAD-05`)** — `ViewQueryRequest.Offset` carries the absolute row offset; both adapters
  pass `start`/`startRow` verbatim. The engine's `ResolveWindow` honours the offset, so the page-size clamp
  can no longer move the window. DataTables now rejects `start < 0` and `search[regex]=true`, and honours
  per-column `searchable`/`orderable` (transport flags default to allow when absent; the engine whitelist
  still governs).
- **D145 (`BUG-03`)** — the mapper authorizes through `AuthorizeFacetAsync` **before** binding, for both the
  write handler and the adapter handler; the decision is memoized per request so the authorizer is consulted
  once per (view, facet).
- **D146 (`BUG-04`, `BUG-05`)** — a declared concurrency token must be model-backed (startup fail-closed), the
  original token value is pinned so the database performs the atomic check, and the update `ETag` is the
  post-write token published through the new request-scoped `IWriteTokenSink`; a delete emits no `ETag`.

**Fixture consequence worth knowing:** the D146 startup guard found five test fixtures that declared a token
their `DbContext` model never configured — exactly the defect the audit described. Each now calls
`IsConcurrencyToken()`. Both Northwind samples were already correct.

- **Verified (2026-07-31, tranche 2):** build green net8/9/10; **536 tests/TFM (net8) / 537 (net10)** in
  `a2n.Vista.Tests`, **143** in `a2n.Vista.Client.TypeScript.Tests`, **114** generator tests — 0 failed,
  0 skipped. Both sample self-tests (Northwind read + write + OpenAPI, AgGridNorthwind) PASS.

### 2.24 Audit remediation, tranche 3 (landed 2026-07-31) — `PERF-02`, `PERF-05`, `PERF-07`

The caching findings: three hot paths that recomputed data which is **immutable after registration**. All
three fixes are pure memoization — **no new decision, and no route, envelope, error shape, or public contract
change**. Deliberately taken as one tranche so the remaining audit work splits cleanly into "needs a design
decision" (`BUG-07`, `PERF-03`/`04`/`06`/`08`) and "one breaking batch" (the `DEAD-*` API removals).

- **`PERF-05`** — new `ViewFieldLookup.For(view)` (`src/a2n.Vista.Core/Metadata/ViewFieldLookup.cs`) is the
  single name → `FieldMetadata` lookup, memoized per metadata instance and returned as a `FrozenDictionary`.
  It replaces four independent per-call builders (`FilterCompiler` ×2 — a List request compiles up to three
  filter channels — plus a duplicated copy in each grid adapter). Ordinal, last-wins matching unchanged.
- **`PERF-02`** — `ExportColumns.Value(row, name)` memoizes the resolved `PropertyInfo` per
  `(row type, name)`, misses included. This ran **per exported cell**: ~1M `GetProperty` calls for a
  100,000-row × 10-column Style A export.
- **`PERF-07`** — `VistaMetadataResponse.From` memoizes the projection per view, and the mapper caches the
  serialized payload with its `ETag`; a 304 is now one string comparison instead of a full serialization plus
  a SHA-256 over the whole payload. Authorization is unchanged: the facet is still authorized (and
  `ShapeQuery` still runs) on every request.

**Cache-keying convention worth reusing:** every cache is a `ConditionalWeakTable` keyed **by reference**, not
by value. `ViewMetadata` is a record whose equality is not a dependable cache key (`BUG-10`, still open), a
`with`-derived clone correctly gets its own entry, and weak keying means a short-lived metadata instance (a
test fixture, a disposed host) leaks nothing. `PERF-07` keys on the *response instance* rather than the view
name for the same reason a name-keyed static cache is wrong here: it would be shared by every host in the
process, so two test hosts registering the same view name could serve each other's bytes.

- **Verified (2026-07-31, tranche 3):** build green net8/9/10 Release (warning set unchanged from §2.22);
  **539 tests/TFM (net8) / 540 (net10)** in `a2n.Vista.Tests`, **143** in
  `a2n.Vista.Client.TypeScript.Tests`, **114** generator tests — 0 failed, 0 skipped. Both sample self-tests
  PASS, including the OpenAPI step's byte-for-byte `/metadata` coexistence check (1537 bytes, unchanged),
  which is direct evidence the `PERF-07` cache did not alter the payload.

### 2.25 Audit remediation, tranche 4 (landed 2026-07-31; D147–D148) — `BUG-07`, `BUG-10`, `PERF-04`

The three findings that all live in the metadata/authoring/read-path area, taken together because they touch
the same code and reinforce each other.

- **D147 (`BUG-07`), two parts.** (1) **Reads are no-tracking.** There was no `AsNoTracking` anywhere on the
  read path, so an entity-bearing projection — an identity projection, or a Style A view registered as
  `(db, sp) => db.Set<Entity>()` — returned rows attached to the request-scoped `DbContext` the write path
  shares. The masking runtime writes the masked value into the materialized row, so a later `SaveChanges` on
  that context could **persist the mask over real data**. All three plans now apply `AsNoTracking()` to the
  source: `SplitViewExecutionPlan` and the generated plan as a direct generic call (AOT-clean; the
  `*_VistaExecutionPlan` goldens were updated), `ProjectedViewExecutionPlan` reflectively because its Style A
  delegate erases the element type — acceptable there, since that plan is already `[RequiresUnreferencedCode]`
  and the call runs once per request, never per row. (2) **The reflection mask is non-destructive.** It used
  to refuse any row whose masked member has no setter, so the path advertised as the Style A fallback could
  not mask an **anonymous** row at all — the one shape Style A is built around. It now rebuilds the row
  through a constructor that takes every readable property by name (the anonymous-type and positional-record
  shape), leaving the original untouched. Requiring full coverage is what makes the rebuild lossless; an
  ambiguous case-insensitive parameter match is treated as no match, so a wrong member can never be written.
  The `Apply` doc comment, which claimed a `with`-rebuild that did not exist, was corrected.
- **D148 (`BUG-10`).** `ViewMetadata.Equals`/`GetHashCode` are now hand-written over the declarative content.
  The synthesized record equality compared *every* instance field including a per-instance lock object, so
  two identical snapshots were **never** equal and the hash was an identity hash, unstable across runs; it
  also compared `Fields` by list reference, since `IReadOnlyList<T>` has no structural equality. The
  D105 startup-completed `KeyFields` is **excluded from both**, so neither can change during an instance's
  lifetime — the property that makes a type safe in a hash-based collection. The exclusion costs nothing:
  view names are globally unique (D101/D103) and `Name` is compared, so two snapshots that compare equal
  describe the same view and resolve the same key.
- **`PERF-04`** (no decision needed). `View<TQuery>`/`View<TQuery, TCrud>` run `Configure` **once** against a
  single cached builder, and metadata, mask specs, the write facet, and row filters are all read back from
  that one authoring result. Previously each member built its own builder — four or more full authoring
  passes per view — and, more importantly, the `ViewMetadata` published to the registry was a different
  instance from the one `Name` read. This also makes the already-documented "called once by the registry/DI
  at startup" contract literally true. The now-dead `BuildMetadataCore` virtual (its doc claimed an override
  by `View<TQuery, TCrud>`, which is deliberately *not* a subclass, D26) was removed.

- **Verified (2026-07-31, tranche 4) —** build green net8/9/10; **543 tests/TFM (net8) / 544 (net10)** in
  `a2n.Vista.Tests`, **143** in `a2n.Vista.Client.TypeScript.Tests`, **114** generator tests — 0 failed,
  0 skipped. Both sample self-tests PASS. Note: one `a2n.Vista.Tests` run failed with a transient
  `SQLite Error 5` on connection open and passed on the three following runs — flaky, not deterministic, and
  unrelated to these changes (no locking behaviour was touched).

### 2.26 Audit remediation, tranche 5 (landed 2026-07-31; D149) — the `DEAD-*` batch became a method correction

**The most important outcome of this tranche is not a fix, it is a correction to how the audit's dead-code
section was written.** That section established which members are *unreferenced*; it never cross-checked
`.kiro/specs/*/requirements.md`, and unreferenced is not the same as dead — an acceptance criterion can
require a member to exist as an extension point without anything in-tree calling it. Cross-checking
reclassified half the batch, and the audit report now carries a method-correction box with the three-way test
to apply before touching any `DEAD-*` item (implementation gap / deliberate skeleton / genuine leftover).

Landed here — the two items a requirement clearly backs:

- **D149 (`DEAD-02`) — display-format metadata.** `IFieldBuilder.Format(...)` is on the authored surface
  (`01-view.md` §5.2) and is the successor of DynData's `DataFormatString` (which D98 says Style A preserves),
  but the captured value was read by nothing, so `.Format("N2")` was silent data loss. What needed deciding
  was *who applies it*: **the server publishes, the client applies.** `FieldMetadata.Format` carries the hint,
  the metadata facet publishes it, and the emitted OpenAPI schema declares it optional. Vista never interprets
  it, so filter/sort/export keep operating on raw values — a presentation hint cannot change what a query
  matches or what an export contains, which keeps export data fidelity independent of display. The response
  member is omitted when unset, so a view that sets no hint has a byte-identical `/metadata` payload
  (verified: 1537 bytes before and after). The TypeScript client is untouched by design — it types wire DTOs,
  not metadata.
- **`DEAD-06` (no decision needed) — `RegisterAssembly` completed.** `pilar-1-hardening` R3.1 lists it as a
  peer of `RegisterTemplate`/`Register<TView>`; **no requirement says "metadata-only"** (that phrase was §4's
  description of a half-finished implementation, now corrected above). Scanning registered metadata only, so a
  scanned view became route-bearing and discoverable while staying permanently non-executable — no plan
  adopted, no mask specs, no write facet. `Register<TView>()` and `RegisterAssembly` now share one private
  `RegisterSource` body. It also had **zero test coverage**; it now has a test driven by a new deterministic
  scan-target assembly, `a2n.Vista.Examples.AssemblyScanTarget` (the main test assembly cannot be its own scan
  target — it holds fixtures that deliberately fail at metadata build time to exercise the startup guards).

Reverted after cross-check, now **open scope calls** rather than cleanups:

- **`DEAD-07` — finding withdrawn.** `openapi-emitter` **R12.2** requires an adapter-documentation extension
  point "without requiring a change to the core builder". A `bool` nothing reads is the wrong shape for that,
  so the real defect is that R12.2 is **unimplemented**. R12.1 *is* satisfied and is validated by Property 10.
  Recommendation: implement a real contribution point; do not remove the option.
- **`DEAD-01`, `DEAD-03`, `DEAD-08`.** Each is declared on a spec'd surface with no acceptance criterion behind
  its behaviour: `IViewRegistry.Register<TView>()` (`pilar-1-core` tasks 4.3, ticked `[x]` but only throws —
  and since superseded by D101/D103, which moved route composition to the registration layer),
  `CrudOn(projectionForRead)` (`01-view.md` §5.2, no read-back criterion anywhere in the write-path spec), and
  the TypeScript client's `DefaultBaseUrl` (absent from Requirement 10; `design.md` describes behaviour that
  contradicts R7.1/Property 20 and R6.3). Removal is defensible for all three, but each needs an owner scope
  call **and** a spec reconciliation in the same change.

- **Verified (2026-07-31, tranche 5) —** build green net8/9/10; **545 tests/TFM (net8) / 546 (net10)** in
  `a2n.Vista.Tests` (2 new), **143** in `a2n.Vista.Client.TypeScript.Tests`, **114** generator tests — 0 failed,
  0 skipped. Both sample self-tests PASS with an unchanged 1537-byte `/metadata` payload.

### 2.27 Audit remediation, tranche 6 (landed 2026-07-31) — `PERF-03`, `DEAD-09` (partial), `PERF-06` declined

- **`PERF-03` — the XLSX worksheet streams.** It used to be accumulated into one `StringBuilder`, returned as a
  single string, then converted with `Encoding.UTF8.GetBytes` — two large-object-heap buffers holding the whole
  document, the intermediate one UTF-16 at ~2× the byte size, on top of the builder's chunks. At the default
  100,000-row cap that was the dominant allocation of an export request. It now writes straight into the archive
  entry, composing one row into a reused builder that is flushed and cleared per row, so peak memory is one row
  plus the archive's compression buffer whatever the row count. The A1 column letters are resolved once per
  column instead of per cell (the related allocation the finding calls out). Byte output is unchanged; the
  writer is UTF-8 **without** a preamble to match the previous `GetBytes` encoding — the one regression a
  `StreamWriter` rewrite invites, now pinned by a test that also covers the row count and the A1 references
  across flush boundaries.
- **`DEAD-09` — the concrete drift is closed, the wider dedup deferred.** `StyleAShapeGenerator` escaped
  accessor-map keys through `Literal(...)`; `ViewAccessorGenerator` concatenated them raw, in two places. One
  writer (`SourceLiterals.Literal`) now serves all three emitters. **Impact was lower than the finding
  implies:** a CLR member name cannot contain a quote or backslash, so escaping an identifier emits identical
  bytes — goldens and the reflection-oracle parity guard are untouched (114/114). It was a latent inconsistency,
  not a live defect. The remaining dedup (4× `FindViewBase`, 3× `IsNamedContractType`, 2× `Unwrap`, 5× hint-name
  builder) is a cross-generator refactor with maintainability-only payoff and real regression risk across five
  incremental pipelines; tracked, not swept in. The unreferenced generator model members the finding also lists
  were deliberately **not** touched — after the §2.26 method correction each needs a requirements cross-check
  first.
- **`PERF-06` — declined as specified.** The proposed fix (resolve well-known types from `CompilationProvider`
  and combine that into the transform) is the documented incremental-generator anti-pattern:
  `CompilationProvider` produces a new value on *every* compilation change, so it would invalidate the cached
  transform for *every* candidate on *every* keystroke, while today the transform re-runs only for candidates
  whose syntax changed. The real cost is one `GetTypeByMetadataName` per candidate class (immediately reduced to
  a `bool`) plus one envelope lookup during shape analysis — cached dictionary lookups on a handful of
  declarations. A safe variant is recorded in the report (`CompilationProvider.Select(... is not null)` yields an
  equatable `bool` that combines without breaking caching); it is not taken because the payoff is negligible
  against perturbing a pipeline whose incrementality, model hygiene, tracking names, and determinism are
  currently guaranteed and golden-tested.

- **Verified (2026-07-31, tranche 6) —** build green net8/9/10 (Debug + Release); **546 tests/TFM (net8) / 547
  (net10)** in `a2n.Vista.Tests` (1 new), **114** generator tests with **unchanged goldens**, **143** in
  `a2n.Vista.Client.TypeScript.Tests` — 0 failed, 0 skipped. Both sample self-tests PASS.

---

## 3. Documentation map (authoritative)

Under `docs/spec/` (all **English** after the 2026-06-20 migration; see §4 language policy):
- `01-view.md` — **foundation**; View concept, public contract, full Decision Log (D1–D50, §13.1
  DR1–DR10, §13.2 D94–D103). Status: IMPLEMENTED, reconciled with code.
- `02-filter-and-query.md` — query engine (Pillar 2 server half). Status: IMPLEMENTED & **hardened**
  (D104–D109 via `query-engine-hardening`); **prose lags the code** for §10 (dialect port) — the code is
  authoritative (see §2.4/§6).
- `03-source-generator.md` — Pillar 3. Status: **DESIGN INTENT (frozen; D71–D81)** — remains the
  authoritative intent; **Phase 1 has landed** (M9/D117, shape-driven export accessors — see §2.10),
  **Phase 2 has landed** (D118, `style-b-executable` — executable typed Style B + D105 + masking runtime;
  see §2.11), **the write-DSL phase has landed** (D121/D122, `source-generator-write-mapper` — the
  generated write mapper + build-time diagnostics; see §2.13), and **the HTTP-surface phase has landed**
  (D123/D124, `source-generator-http-surface` — the generated dispatch invoker + AOT-clean serialization
  seam; see §2.14), and **the per-view `JsonTypeInfo` phase has landed** (D125/D126,
  `source-generator-json-typeinfo`; see §2.15). The **OpenAPI emitter** shipped separately as the opt-in
  `a2n.Vista.OpenApi` package (M18, D127/D128, `openapi-emitter` — a metadata/seam consumer, not a source
  generator; see §2.16). The rest (Style A coverage) is still forward-looking. Note: this spec's §13
  diagnostic catalog is **superseded** on the generator diagnostic
  numbering — the landed code uses `VISTA0030`–`VISTA0033` (write-DSL) and `VISTA0040`/`VISTA0041`
  (HTTP-surface) per §2.13/§2.14 (code is the source of truth).
- `04-adapter-contract.md` — Pillar 2 adapters. Status: **DESIGN INTENT (frozen)**.
- `05-aspnetcore-mapping.md` — HTTP composition. Status: IMPLEMENTED as the **action-style surface**
  (D110 via `http-surface-redesign`, supersedes DR3); **prose lags the code** — the code + §2.5 are
  authoritative.
- `10-operations-and-observability.md` — vendor-neutral observability + health + startup validation.
  Status: DESIGN INTENT (not built).
- `11-versioning-and-deprecation.md` — public surfaces, versioning scheme, deprecation policy.
  Status: DESIGN INTENT.

Kiro specs under `.kiro/specs/`: `pilar-1-core` (done), `pilar-1-hardening` (done). These are
**Indonesian** (legacy); see language note in §4.

> Important reconciliation history: `02`/`05` were authored after Pillar 1 was implemented and had
> drifted; they now carry "reconciliation" banners and the **code-accurate** contracts. `03`/`04` are
> forward-looking design intent and intentionally not pinned to code yet.

---

## 4. Settled decisions — DO NOT re-litigate

Each is final for this release; full rationale at the cited location. Reopen only with explicit owner
approval.

### Security & defaults
- **Auth posture (D94, revises D43).** Two-level model: *switch* (authorizer present/absent) + *policy*
  (handler). Without an authorizer: **Development** → allow-all + startup warning; **non-Development**
  (Production/Staging/UAT/unset env) → **fail-closed startup throw** unless `AllowAnonymousAccess()` is
  called explicitly. Rationale: operator ≠ author and may lack source; a security omission must
  fail-safe; "open" must be a deliberate, reviewed choice. Impl: `VistaStartupValidator` (injects
  `IHostEnvironment`), `VistaEndpointOptions.AllowAnonymous`, `IVistaEndpointBuilder.AllowAnonymousAccess()`.
  (`01-view.md` §5.6, §13.2 D94.)
- **Masked ⇒ non-probeable (D95).** A `MaskField`'d field defaults to `IsFilterable=false` and (string)
  `IsSearchable=false`, unless an explicit `Filterable(true)`/`Searchable(true)`/`Operators(...)` opt-in
  is given. Closes binary-search probing of masked values. Impl: `FieldBuilder` tracks explicit-set;
  `ViewBuilder.Build` applies the masked default. (`01-view.md` §5.2/§7, §13.2 D95.)
  **Note:** the masking *transform* itself is not yet applied at runtime (see §7 backlog).
- **Filter/Sort/Search default-allow (D42, supersedes D3/D14).** All projection fields are
  filter/sort/searchable by default (search = string only); opt out per field. Security boundary = the
  curated projection.
- **Every Vista read is no-tracking (D147).** All three execution plans (`SplitViewExecutionPlan`,
  `ProjectedViewExecutionPlan`, and the source-generated compiled plan) apply `AsNoTracking()` to the
  source query, so a read never hands the caller entities attached to the request-scoped `DbContext` the
  write path shares. Rationale: the masking runtime writes the masked value into the materialized row, so a
  tracked row let a later `SaveChanges` persist the mask over real data. Applies to List, Detail, Export,
  and the count queries. Same decision made the reflection mask **non-destructive** for a get-only row.
  (Audit `BUG-07`; §2.25.)

### Authoring & routing
- **Two authoring styles are permanent (D96).** Style A (central template, anonymous) **and** Style B
  (class-per-view, typed) are first-class forever — no deprecation of Style A. **AOT asymmetry is
  permanent & explicit:** Style A serialization stays `[RequiresUnreferencedCode]` (anonymous);
  filter/sort/paging stays AOT-clean. Use-case guidance: monolith→A, modular monolith→B,
  microservices→either. (`01-view.md` §4.5, §13.2 D96.)
- **Route model = registration owns the route (model R) (D101, done).** A view's full route is composed
  at registration (default root `/api/views`, or a `RouteGroup` prefix) and baked into
  `ViewMetadata.Route`; the AspNetCore mapper is a **dumb mapper** that maps each view at its
  `ViewMetadata.Route`. The AspNetCore-owned `RouteRoot` setter was **removed**. (`01-view.md` §13.2 D101.)
- **Route groups + one view = one endpoint (D103, done).** `IVistaBuilder.RouteGroup(prefix, g => {...})`
  scoping (nested groups combine), `RegisterAssembly(...)` (`[RequiresUnreferencedCode]`; registers on the
  **same terms as `Register<TView>()`** — corrected 2026-07-31: the former "metadata-only" wording here
  described a half-finished implementation, not a decision, and `pilar-1-hardening` R3.1 lists the two as
  peers), default root for ungrouped. View names are **globally unique**; a view maps to **exactly one** endpoint
  (registering the same view in two groups fails fast). (`01-view.md` §13.2 D103.)
- **Cross-assembly view discovery (D97)** is a **committed Pillar 3 requirement** (needed by the
  modular-monolith Style B use case), not an open question.

### Migration & versioning
- **No DynData compatibility layer (D98, revises D20).** Migration is manual. DynData ergonomics are
  preserved via Style A (its spiritual successor), and `08-migration-from-dyndata.md` is the primary
  migration tool. No `/dyndata/*` aliases, no wire shim.
- **Wire versioning deferred (D99).** No `/api/v{n}/` routes this release; unversioned = latest; a
  `VistaEndpointOptions.CurrentWireVersion` seam exists for additive future versioning; **route groups
  are the intended versioning vehicle**. Avoids premature abstraction. (`11-versioning-and-deprecation.md`.)

### Operations & observability (designed, NOT built)
- **Vendor-neutral observability (D100).** Instrument via OpenTelemetry-native `ActivitySource`/`Meter`/
  `ILogger` (no APM dependency — Instana/Datadog/Jaeger/Prometheus/Serilog all consume OTel). Enrich
  auto-instrumented HTTP/EF spans with View semantics; expose operational status (e.g. authorizer) via
  standard health checks; opt-in & zero-cost when unused. Observability names = an operational contract,
  subject to the deprecation policy. (`10-operations-and-observability.md`.)
- **Operating-model assumption (org-neutral).** The operator is often not the code author and may lack
  source access; therefore defaults fail safe, failures are operator-actionable via standard signals
  (health/logs/telemetry), and breaking changes must be deploy-time-detectable (startup config
  validation). This generalizes the original enterprise Dev/Ops-separation context — it is **not**
  company-specific.

### Pillar 1 implementation reconciliation (DR1–DR10, `01-view.md` §13.1)
These record where the code intentionally differs from the early spec sketches. **Code wins.**
- **DR1** `IViewRegistry`: `Add(ViewMetadata)`, `Register<TView>()` (RUC), `Get` **nullable** (miss→404),
  `All`. No `Register(Type)`/`RegisterAssembly` on the Core registry.
- **DR2** DI is **two doors**: `AddVista` (EF — registration) + `AddVistaEndpoints` (AspNetCore — auth).
- **DR3** ~~Pillar 1 List = `GET {root}/{viewName}` (query string).~~ **Superseded by D110**
  (`http-surface-redesign`): the surface is action-style — List = `POST {route}/list` (query in the JSON
  body); the Pillar 2 `POST .../query` form is unified into `list`. See §2.5, §5, and Spec 05 §5.2.
- **DR4** `WithValidator`/`WithInterceptor` deferred (not in code).
- **DR5** Style B `Register<TView>()` is **metadata-only** (not executable without an `IViewExecutionPlan`
  via `Register<TView>(plan)` or source-gen).
- **DR6** List result = **`ViewListResult<TRow>`** (`PagedResult<TRow> Page` + `long TotalRowsUnfiltered`).
  **Supersedes** the proposed `ViewQueryResult<T>` (Spec 02 §6.2, D51).
- **DR7** ~~Write endpoints mapped but EF write **not implemented** → **501** (writable) / **404**
  (read-only).~~ **Superseded by M12 (D119/D120):** the EF write facet is now implemented; writable views
  execute Create/Update/Delete (200/404/409/428/4xx), read-only/unregistered/no-plan views produce an
  indistinguishable **404**. The 501 stub is gone. See §2.12.
- **DR8** Write is **merged into `IViewExecutor`** (`CreateAsync<TCrud>`/`UpdateAsync<TCrud>`/`DeleteAsync`),
  **not** a separate `IViewWriter` (contra Spec 05 §7.1 D82). `IViewExecutor` is **generic**
  (`ListAsync<TRow>`/`DetailAsync<TRow>`), not erased-to-`object` `QueryAsync(ViewQueryExecution)`.
- **DR9** `FilterOrigin` is a **public 3-value enum** (`Filter`/`Search`/`Scope`) passed as a **parameter**
  to `FilterCompiler.Compile(node, origin, view)`. **Not** a field on `FilterLeaf`; no `Trusted` value
  (trusted scope flows via `IViewScope`, unvalidated). **Supersedes** Spec 02 §6.1 refinement (D52) and
  Spec 02 D53 (erased port). Consequence: the executor receives one **merged** tree; per-channel
  Search/Scope separation must be done by the **adapter** (Spec 04) — see §6 coupling.
- **DR10** `app.MapView(string viewName)` + `app.MapVistaViews()`. `MapView<TView>()` deferred (needs
  source-gen type→name resolution).

### Language policy
- **Published artifacts are English** (code, comments, **everything under `/docs`**, commits, PRs,
  GitHub-visible content) — per `.kiro/steering/persona-and-language.md`. Rule of thumb: if it ships to
  GitHub, it must be English.
- **Git-ignored local tooling may be Bahasa Indonesia.** `.kiro/` (specs, steering, agent tools) is in
  `.gitignore`, so English is allowed but **not** required there. The legacy
  `.kiro/specs/{pilar-1-core,pilar-1-hardening}` Indonesian docs are therefore fine as-is — no migration
  needed.
- `docs/spec/*.md` were migrated to English on 2026-06-20. **New `docs/` artifacts must be English.**

---

## 5. Decision-number map (avoid collisions)

| Range | Owner | Notes |
|---|---|---|
| D1–D50 | `01-view.md` §13 | Foundation. Superseded: D3/D14→D42; D4→D43→D94; D9→D9-revised; D20→D98; D36→D42/D45. |
| D51–D62 | `02-filter-and-query.md` §16 | Engine. **Overridden by reconciliation:** D51→DR6, D52→DR9, D53→DR8, D58→(two counts via `ViewListResult`). Others (coercion/dialect/paging/guards) stand as targets. |
| D63–D70 | `04-adapter-contract.md` §11 | Adapters (design intent). |
| D71–D81 | `03-source-generator.md` §15 | Source gen (design intent). |
| D82–D93 | `05-aspnetcore-mapping.md` §12 | HTTP. **D82 (`IViewWriter`) overridden by DR8.** |
| D94–D103 | `01-view.md` §13.2 (+ docs 10/11) | Cross-cutting (this session). |
| DR1–DR10 | `01-view.md` §13.1 | Pillar 1 code reconciliation. |
| D104–D109 | `query-engine-hardening` spec / `02` §16 + `01` §5.4 | Engine hardening (key model, PK derivation, deterministic paging, `IQueryDialect` port, DoS guards, composite key). **D107 supersedes the old §6.3 P2 doc-only recommendation.** |
| D110 | `http-surface-redesign` spec / `05` | Action-style POST endpoints + `GET .../metadata`; **supersedes DR3**. |
| D111–D114 | `datatables-adapter` spec / `04` (+ `02` §7 for D111) | D111 multi-channel request (Search/Scope slots; closes DR9 per-channel enforcement); D112 adapter endpoint (`POST {route}/{suffix}`); D113 QueryBuilder schema emitter (**done in D116**); D114 `jsonQB` parser in the DataTablesNet package. |
| D115 | `export-pipeline` spec / `01` §11 | Pluggable export pipeline: `IViewExportWriter` + built-in CSV/XLSX; `AddVistaExportWriter<T>()` override; format-by-request, JSON-compatible when omitted. |
| D116 | `metadata-schema-adapters` spec / `04` §5.2/§8.2 | Per-grid metadata schema: `IViewMetadataAdapter` + QueryBuilder `metadataQB` emitter; `GET {route}/{RouteSuffix}`. Supersedes the D113 deferral. |
| D117 | `source-generator` spec / `03` §15 | **Landed (M9, Phase 1).** Phased source generator: `IIncrementalGenerator` (`netstandard2.0`, FQN recognition, no Vista project ref) recognizing typed Style B views; shape-driven read-accessor generation; `[ModuleInitializer]` registration into a Core `ViewAccessorRegistry`; export pipeline prefers generated accessors with reflection fallback (coexistence); `VISTA0001`/`VISTA0002` diagnostics; snapshot + AOT test harness. **Deferred to later phases:** executable plans/`CompiledView`, member-access for filter/sort, `JsonSerializerContext`, OpenAPI, projection/`MapWritable` DSL analysis, Style A. Builds on Spec 03 D71–D81. See §2.10. |
| D118 | `style-b-executable` spec / `03` §15 | **Landed (M9 Phase 2 = M10 + M11 + M13).** A second generator emitter produces, per typed Style B view, an AOT-clean `ICompiledViewExecutionPlan` (compile-time projection, per-field member-access, strongly-typed sort appliers, masked-field accessors, single-source marker) published via `[ModuleInitializer]` into a static `GeneratedExecutionPlanStore` that `AddVista` drains → executable List/Detail (**DR5 closed for typed views**); the contract does not inherit the RUC `IViewExecutionPlan` (DR8 seam split). Adds non-RUC `EfViewExecutor.ListCompiledAsync`/`DetailCompiledAsync`; the masking runtime (`MaskApplier`, fail-closed, post-projection in memory, SQL unchanged); D105 single-source PK auto-derivation (`VistaModelKeyDerivationService : IHostedService`); `VISTA0003`/`VISTA0020` diagnostics. Write path stays **501** (M12, separate spec). Build green net8/9/10, 156 tests/TFM, Northwind self-test PASS, AOT probe clean. Builds on D117 / Spec 03 D71–D81. See §2.11. |
| D119 | `write-path` spec / `01` §7 | **Landed (M12).** Write mapping seam: fixed-signature `WriteMapper` delegate (`(object,object)→void`) resolved once per write via `WriteMapperResolver` (generated-preferred `GeneratedWriteMapperStore`, RUC `ReflectionWriteMapper` fallback — `[RequiresUnreferencedCode]` confined to the fallback); Style B `CrudBuilder` captures `MapWritable`/token/`AllowBulk` into a `CrudFacetDefinition` delivered via the Core `IWriteFacetRegistry`. Zero executor changes when the future M9 write-DSL mapper lands. See §2.12. |
| D120 | `write-path` spec / `01` §7 | **Landed (M12).** Write error-code vocabulary + concurrency signalling: `WriteErrorCode`/`WriteErrorCodes` on the shared RFC 7807 envelope (`extensions["code"]`), typed write exceptions mapped by `VistaProblemResults`; optimistic concurrency via `If-Match`/`ETag` (428 precondition gate, 409 mismatch/`SaveChanges` conflict). Bulk deferred (array → 400; `AllowBulk` enables no path). See §2.12. |
| D121 | `source-generator-write-mapper` spec / `03` §15 | **Landed (M9 write-DSL phase).** The generated write mapper: a second `IIncrementalGenerator` (`WriteMapperGenerator`) emits, per analyzable typed Style B writable view, a reflection-free `WriteMapper` (`Action<object,object>` = casts + one whitelisted scalar assignment per safe `MapWritable` mapping, declaration-ordered, defense-in-depth) as `file static` C# + a `[ModuleInitializer]` filling the M12 `GeneratedWriteMapperStore` keyed by the view's runtime `Name`; `WriteMapperResolver` prefers it over `ReflectionWriteMapper` (RUC fallback) with **zero executor changes** → the typed Style B write path is now AOT-clean. `VISTA0033` (warning) marks an unanalyzable chain → silent reflection fallback. Builds on D117/D118. See §2.13. |
| D122 | `source-generator-write-mapper` spec / `03` §13 | **Landed (M9 write-DSL phase).** Interim write-authoring startup guards promoted to **build-time** analyzer diagnostics: `VISTA0030` (zero mappings), `VISTA0031` (non-scalar/navigation target), `VISTA0032` (key-field/concurrency-token target) — all **errors** that gate emission; the mirroring fail-fast guards in `ViewBuilderOfTCrud.ValidateWriteFacet` were retired (the primary-key executability guard is retained). Adopts the code/PROJECT-STATUS `VISTA0030`–`VISTA0033` numbering; the frozen `03` §13 catalog's conflicting assignment is superseded. See §2.13. |
| D123 | `source-generator-http-surface` spec / `03` §15 | **Landed (M9 HTTP-surface phase, 2026-07-12).** The generated typed HTTP **dispatch invoker**: a per-view, reflection-free `IViewInvoker` (Core port) that closes `IViewExecutor.List/Detail/Create/Update<T>` at compile time (no `MakeGenericMethod`, no `Task<TResult>.Result`/`ViewListResult<TRow>` reflection), registered via `[ModuleInitializer]` into a Core-resident, first-wins `ViewInvokerStore` keyed by the view's runtime `Name`; `ViewRequestExecutor` prefers it with the existing `MakeGenericMethod` path confined to private `*ReflectionAsync` RUC fallbacks (coexistence — Style A / uncovered views unchanged). The executor read-facet RUC was relaxed to match (mirroring `WriteMapperResolver`). Mechanism-only: no wire change; byte-for-byte parity with the reflection oracle is the guard (master Property 1). Builds on D117/D118/D121. See §2.14. |
| D124 | `source-generator-http-surface` spec / `03` §15 | **Landed (M9 HTTP-surface phase, 2026-07-12).** The AOT-clean **serialization seam** (in `a2n.Vista.AspNetCore`, Core stays STJ-free): a single `TypeInfoResolverChain` over `VistaJson.Options` = a shipped hand-authored `VistaStaticJsonContext` (fixed request/response envelopes + the now reflection-free polymorphic `FilterNode`) → developer-authored `App_Json_Context`(s) chained via `AddVistaJsonContext(...)` → an opt-out reflection fallback (`DefaultJsonTypeInfoResolver`, the only RUC serialization branch, removable via `DisableVistaReflectionSerializationFallback()`). Every Vista response is written via the shared `VistaJsonWriter`/`JsonTypeInfo` overloads (replacing `Results.Ok(obj)` for List/Detail/Export) and `VistaWriteBinding` deserializes through the seam (byte-for-byte parity). Per-view `JsonTypeInfo` is **not** auto-generated (generator-of-generator constraint); the generator emits `VISTA0041` guidance naming the exact `[JsonSerializable]` types, and `VISTA0040` flags an uncovered candidate. New diagnostic family begins at `VISTA0040`. See §2.14. |
| D125 | `source-generator-json-typeinfo` spec / `03` §15 | **Landed (M9 per-view `JsonTypeInfo` phase, 2026-07-12).** The generated **per-view `JsonTypeInfo` provider**: a fourth `IIncrementalGenerator` (`ViewJsonContextGenerator`) emits, per covered typed Style B view, a reflection-free `file sealed IJsonTypeInfoResolver` built by hand via `System.Text.Json.Serialization.Metadata.JsonMetadataServices` (NOT the `[JsonSerializable]` attribute route — the generator-of-generator constraint) providing the `JsonTypeInfo` for `TRow`, `ViewListResult<TRow>`, `PagedResult<TRow>`, and — when writable — `TCrud`, plus the collection/nullable/enum/leaf metadata those DTOs reach (so they resolve with no reflection fallback); a `[ModuleInitializer]` fills the new Core-resident, serializer-neutral `GeneratedJsonContextStore` (opaque `object` handles → `a2n.Vista.Core` gains no System.Text.Json dependency), keyed by the view's runtime `Name`. Non-blocking diagnostic `VISTA0050` (covered) + `VISTA0051` (non-emittable member → fallback). Builds on D117/D118/D121/D123/D124. See §2.15. |
| D126 | `source-generator-json-typeinfo` spec / `03` §15 | **Landed (M9 per-view `JsonTypeInfo` phase, 2026-07-12).** The **seam integration**: `a2n.Vista.AspNetCore` drains `GeneratedJsonContextStore` and chains each generated context into the existing `TypeInfoResolverChain` ahead of the developer `App_Json_Context`(s) and the opt-out reflection fallback (keeping `VistaStaticJsonContext` first), making the developer `App_Json_Context` **optional** without changing the seam config, the dispatch invoker (D123), or the `AddVistaJsonContext(...)`/`DisableVistaReflectionSerializationFallback()` APIs. The single unchecked opaque-handle → `IJsonTypeInfoResolver` cast at the drain is the contract boundary (layering-tested). Mechanism-only: no wire change; byte-for-byte parity with the reflection oracle (Property 1 + round-trip Property 2). See §2.15. |
| D127 | `openapi-emitter` spec / M18 | **Landed (M18 OpenAPI emitter, 2026-07-13).** The runtime, metadata-driven OpenAPI v3.x document builder + the new opt-in `a2n.Vista.OpenApi` package (references `a2n.Vista.AspNetCore`; multi-targets net8/9/10) with its own hand-authored, deterministically serializable `OpenApiDocument` object model (source-gen `JsonSerializerContext`). `VistaOpenApiDocumentBuilder` emits, per registered `ViewMetadata`, the fixed operation set (`list`/`detail`/`metadata`/`export` + `create`/`update`/`delete` iff `!IsReadOnly`) via a fixed facet→operation table; structure + envelope/`FilterNode`/`ProblemDetails` descriptors are reflection-free (AOT-clean), and only per-view DTO schemas come from the one `[RequiresUnreferencedCode]` `DtoSchemaGenerator` branch (D96 asymmetry; unresolvable member → permissive `{}` + `ILogger` notice). Two oracles (route table = endpoint parity; seam = schema/wire parity) with determinism the stabilizer. Additive-only, off by default; Core/EF/AspNetCore gain no dependency. **No new `VISTA####` diagnostics.** See §2.16. |
| D128 | `openapi-emitter` spec / M18 | **Landed (M18 OpenAPI emitter, 2026-07-13).** The opt-in serve endpoint + the optional ASP.NET Core OpenAPI pipeline provider: `AddVistaOpenApi(configure?)` (validated `VistaOpenApiOptions` + build-once `VistaOpenApiDocumentCache`, all singletons; fail-fast validation) and `MapVistaOpenApi()` (`GET /openapi/v1.json` by default, returning the cached document inside the host auth pipeline — bypasses nothing). On net9.0/net10.0 a TFM-guarded `VistaOpenApiDocumentTransformer` merges the Vista `paths`/`components` into an app's built-in `Microsoft.AspNetCore.OpenApi` document; net8.0 keeps only the Vista serve endpoint. Both public APIs carry `[RequiresUnreferencedCode]`. See §2.16. |
| D129 | `style-a-coverage` spec / `03` §15 | **Landed (M9 Style A coverage, 2026-07-13).** Style A recognition + shape-driven emission for the nameable subset: a fifth `IIncrementalGenerator` (`StyleAShapeGenerator`, `netstandard2.0`, FQN recognition, no Vista project ref) — the first to key off an **`InvocationExpressionSyntax`** — recognizing `ViewTemplate<TDbContext>.AddView<TRow>(...)` call sites (walking a chained `WithCrud<TCrud, TEntity>()`). For a covered view it emits, into the template's own assembly keyed by the **constant** `AddView` name: (a) export accessors for a **named** `TRow` → `ViewAccessorRegistry` (D117); (b) read-DTO `JsonTypeInfo` (`TRow`/`ViewListResult<TRow>`/`PagedResult<TRow>`) for a named `TRow`, and (c) write-model `TCrud` `JsonTypeInfo` for **any** writable view (`TCrud` always named, D38) → `GeneratedJsonContextStore` (D125), both via `JsonMetadataServices`. All **shape-only** (no projection reconstruction); Emittable_Shape inherited from D125. **No new store, no new seam** — the D126 drain and the `ExportColumns.Value(...)` export seam pick up Style A entries unchanged. Mechanism-only; byte-for-byte parity with the reflection oracle is the guard. Builds on D117/D125/D126. See §2.17. |
| D130 | `style-a-coverage` spec / `03` §15 | **Landed (M9 Style A coverage, 2026-07-13).** The reaffirmed permanent by-design RUC boundary: an **anonymous** read `TRow` is unnameable in generated source, so its read serialization/export stay `[RequiresUnreferencedCode]` **forever** (reaffirms D96) — surfaced by non-blocking `VISTA0061`; `VISTA0060` (covered, Info), `VISTA0062` (non-constant name, Info), `VISTA0063` (non-emittable member, Warning) complete the family. The AOT probe **demonstrates** the asymmetry (anonymous read RUC vs named-row / `TCrud` / Style B AOT-clean) within one view rather than removing it. Diagnostic family begins at `VISTA0060`. See §2.17. |
| D131 | `typescript-client` spec | **Landed (M17 TypeScript client, 2026-07-14).** The **OpenAPI document is the single generation source**, consumed over a one-way, buffered, pure pipeline (**acquire → parse → resolve → model → emit → write**) that makes determinism + all-or-nothing failure structural. The generator (`src/a2n.Vista.Client.TypeScript`, a .NET CLI, multi-target net8/9/10) holds **no** `a2n.Vista` project reference (Core/EF/AspNetCore/OpenApi all absent) — a pure document consumer. It emits framework-agnostic TypeScript: per-view `TRow`/`TCrud` DTOs, the fixed Vista envelopes, a **presence-discriminated** `FilterNode` union (M18 emits no `discriminator`), the RFC 7807 `ProblemDetails` type, one **re-lifted** generic `ViewListResult<TRow>`/`PagedResult<TRow>` per view (M18 monomorphizes them), and a per-view typed client. Additive-only; the emitted document is the parity oracle. See §2.18. |
| D132 | `typescript-client` spec | **Landed (M17 TypeScript client, 2026-07-14).** Secure-by-default client posture: the **read surface is the default**, the **write surface is gated off by default** behind an explicit opt-in flag. The client never embeds a credential (injectable bearer `AuthProvider`), routes every request through an injectable `HttpTransport`, defaults transport to HTTPS (non-HTTPS non-loopback base URL → typed config failure; loopback → warn+continue), and surfaces every outcome as one total, non-throwing discriminated `ClientResult<T>` (incl. distinct `unauthorized`/`not-found`/428/409 members). See §2.18. |
| D133 | `ag-grid-adapter` spec | **Landed (M16 AG Grid adapter, 2026-07-14).** The adapter surface: `AgGridAdapter : ViewAdapter<AgGridRowsRequest, AgGridRowsResponse>` (Core-only, D48), `Id="aggrid"` + `RouteSuffix="aggrid"` → `POST {route}/aggrid` through the **existing** DataTables glue verbatim (no new host mechanism). Three pure, deterministic mapping steps (`BindRequest`/`ToQuery`/`ToResponse`). Consumes D48/D110/D111 unchanged. See §2.20. |
| D134 | `ag-grid-adapter` spec | **Landed (M16, 2026-07-14).** The `filterModel` → `FilterNode` mapping (locked table) in the pure `AgGridFilterModelParser`: text/number/date `type`s, `set` → `In`, `inRange` → `Between` (both bounds required), `blank`/`notBlank` → `IsNull`/`FilterNot`, combined `AND`/`OR` → `FilterAnd`/`FilterOr` (order-preserving). **Advanced Filter deferred for v1** — rejected loudly (`AdapterBindException` → 400 `adapter-bind-failed`), never silently dropped (D67 posture). Consumes D96 unchanged. See §2.20. |
| D135 | `ag-grid-adapter` spec | **Landed (M16, 2026-07-14).** Block paging + response mapping: `PageSize = EndRow - StartRow`, `Page = StartRow / PageSize` (non-positive `PageSize` passed through so the engine rejects it, no clamp/default); response `{rowData = Rows, rowCount = RecordsFiltered}` (filtered total for AG Grid last-block detection; `RecordsTotal` not surfaced). `filterModel` → `Filter` channel, quick filter → `Search` channel; adapter never enforces the tri-whitelist (per-channel engine validation, D111). See §2.20. |
| D136 | `ag-grid-adapter` spec | **Landed (M16, 2026-07-14).** Sample composition: quick-filter transport via `?q=` folded into `AdapterRequest.Values` (zero host change) + a thin hand-written `IServerSideDatasource` (the M17 generated client is OpenAPI-driven; adapter endpoints not yet in the OpenAPI document). `a2n.Vista.Examples.AgGridNorthwind` (net8.0-only): ASP.NET host + AG Grid + TS front-end (`tsc --noEmit` gate) + a guarded `dotnet run -- selftest`. See §2.20. |
| D137 | `northwind-sample-showcase` spec | **Landed (Northwind sample showcase, 2026-07-14).** Showcase composition & layout: the single `a2n.Vista.Examples.AgGridNorthwind` host serves all three pages behind a shared nav and registers `DataTablesAdapter` + `QueryBuilderSchemaAdapter` + `AgGridAdapter` + the OpenAPI emitter, keeping `AllowAnonymousAccess()` (D94); the standalone `a2n.Vista.Examples.Northwind` host stays the separate DataTables-only single-view sample. Revised the earlier draft that had proposed extending the `Northwind` host. Additive at the sample layer. See §2.21. |
| D138 | `northwind-sample-showcase` spec | **Landed (2026-07-14).** View-catalog exposure: an additive read-only endpoint `GET /api/showcase/views` returning a pure `ShowcaseCatalog.Project(IViewRegistry)` (name + route + humanized title; `[]` on empty), secure-by-default (only registered views) and inside the host auth pipeline. Additive — no existing route/envelope/error change. See §2.21. |
| D139 | `northwind-sample-showcase` spec | **Landed (2026-07-14).** Page technology: static HTML + TypeScript compiled by `tsc` (no bundler), emitting into `wwwroot/js`; `tsc --noEmit` typecheck gate + fast-check property tests for the pure transforms (`columns.ts`, `search.ts`). See §2.21. |
| D140 | `northwind-sample-showcase` spec | **Landed (2026-07-14).** Registered Northwind view set: a third read-only view `vOrder` added so `vProductCategory`/`vOrderDetail`/`vOrder` span string/numeric/date/FK/composite-key, exercising the query-builder operators and column affordances. See §2.21. |
| D141 | audit remediation (`docs/audit/2026-07-31-full-code-audit.md`, `SEC-01`) | **Landed (2026-07-31).** Style A row-level-security posture: `IViewScope` gains `RowFilterCount` (a type-erased count) so the combined-delegate `ProjectedViewExecutionPlan` can detect a populated request scope it cannot AND pre-projection and **fail closed**, instead of silently serving unscoped rows. Extends the existing authored-row-filter guard to the `IViewAuthorizer.ShapeQuery` scope. Source-breaking only for an external `IViewScope` implementation (none in-tree). See §2.22. |
| D142 | audit remediation (`SEC-02`) | **Landed (2026-07-31).** The OpenAPI document endpoint is authorized by default: `MapVistaOpenApi()` attaches `RequireAuthorization()` unless the host opted into anonymous access via D94 `AllowAnonymousAccess()` or explicitly set `VistaOpenApiOptions.RequireAuthorization = false`. Rationale: an endpoint with no authorization metadata is anonymous even behind `UseAuthentication`/`UseAuthorization`, and the document publishes every view's route, operation set, writability, and row schemas. Per-caller document filtering through `IViewAuthorizer` is **deferred** (it makes the document per-identity and needs a caching decision). See §2.22. |
| D143 | audit remediation (`SEC-04`) | **Landed (2026-07-31).** Extends D95 to the **sort** channel: a masked field defaults **non-sortable**, with an explicit `Sortable(...)` opt-in overriding it (a new `SortableExplicitlySet` signal, mirrored in the source generator so generated metadata stays byte-identical to the reflection oracle). Rationale: `ORDER BY` on a masked column plus paging leaks the relative ordering of the hidden values — for a numeric/date column close to a binary search — the same probing vector D95 closes for filter and search. Behaviour change for a view that sorted on a masked field; the generated execution plan no longer emits a member accessor for such a field. See §2.23. |
| D144 | audit remediation (`BUG-02`) | **Landed (2026-07-31).** Paging carries an **absolute row offset**: `ViewQueryRequest.Offset` (optional, `null` = the unchanged page model) is authoritative when set, and both grid adapters now pass `start`/`startRow` verbatim instead of dividing by the client's page size. Rationale: the division lost rows twice — integer division snapped an unaligned offset, and the executor's page-size clamp then moved the window, returning wrong rows with no error. Clamping is now a pure size concern: the window start never moves. Supersedes the D135 `Page = StartRow / PageSize` mapping. DataTables additionally rejects `start < 0` and `search[regex]=true` (`AdapterBindException` → 400) and honours per-column `searchable`/`orderable` (audit `DEAD-05`). See §2.23. |
| D145 | audit remediation (`BUG-03`) | **Landed (2026-07-31).** **Authorize before bind**: `ViewRequestExecutor.AuthorizeFacetAsync` is the pre-gate the endpoint mapper calls before reading the body, binding the model, reading the key, or applying the 428 precondition gate; the adapter handler calls it before the body read + adapter bind. The decision is memoized per request in `HttpContext.Items`, so an authorizer still sees exactly one `IsAllowedAsync` call per (view, facet) per request. Rationale: an unauthorized caller used to receive `428` or a `400` bind error instead of `403`, disclosing that the view exists, is writable, and declares a token — and could force JSON parsing work. See §2.23. |
| D146 | audit remediation (`BUG-04`, `BUG-05`) | **Landed (2026-07-31).** Concurrency is real, and the echoed token is the post-write one. Three parts: (1) `VistaConcurrencyTokenStartupValidator` fails startup closed when a view's `WithConcurrencyToken(...)` member is **not** a concurrency token in the `DbContext` model (without it the database emitted no `UPDATE ... WHERE token = @original` predicate, so the Vista-level read-then-compare allowed a lost update); (2) the executor pins the tracked entry's original token so the check happens **in the database**; (3) the new Core-resident, request-scoped `IWriteTokenSink` carries the token read back after `SaveChanges`, which the mapper emits as the update `ETag` — a **delete emits no `ETag`** at all, since the row no longer exists. No `IViewExecutor` port change, so the generated dispatch invoker is untouched. See §2.23. |
| D147 | audit remediation (`BUG-07`) | **Landed (2026-07-31).** Every Vista read is **no-tracking**: all three execution plans apply `AsNoTracking()` to the source query — a direct generic call in `SplitViewExecutionPlan` and in the generated compiled plan (AOT-clean; the `*_VistaExecutionPlan` goldens changed), reflective in the already-`[RequiresUnreferencedCode]` `ProjectedViewExecutionPlan` whose Style A delegate erases the element type (once per request, never per row). Rationale: the masking runtime writes the masked value into the materialized row, so an entity-bearing projection returning **tracked** rows let a later `SaveChanges` on the shared request-scoped `DbContext` persist the mask over real data. Covers List/Detail/Export and the count queries. The same decision makes the **reflection mask non-destructive**: a get-only row — an anonymous Style A projection, previously not maskable at all — is rebuilt through a constructor covering every readable property (full coverage keeps the rebuild lossless; an ambiguous case-insensitive parameter match is treated as no match). See §2.25. |
| D148 | audit remediation (`BUG-10`) | **Landed (2026-07-31).** `ViewMetadata.Equals`/`GetHashCode` are hand-written over the declarative content (name, route, types, **element-wise** `Fields`, authorization, limits, read-only flag). The synthesized record equality compared every instance field including a per-instance lock object, so two identical snapshots were never equal and the hash was an identity hash unstable across runs; it also compared `Fields` by list reference, since `IReadOnlyList<T>` has no structural equality. The D105 startup-completed `KeyFields` is **excluded from both**, so neither changes during an instance's lifetime — the property that makes a type safe in a hash-based collection. Harmless: view names are globally unique (D101/D103) and `Name` is compared, so equal snapshots describe the same view and resolve the same key. See §2.25. |
| D149 | audit remediation (`DEAD-02`) | **Landed (2026-07-31).** Display-format metadata: **the server publishes, the client applies.** `IFieldBuilder.Format(...)` (on the authored surface per `01-view.md` §5.2, and the successor of DynData's `DataFormatString`) now reaches `FieldMetadata.Format`, the `GET {route}/metadata` projection, and the emitted OpenAPI schema (optional). Vista never interprets the hint, so filter, sort, and export keep operating on raw values — presentation cannot change what a query matches or what an export contains. Previously the value was captured and read by nothing, making `.Format("N2")` silent data loss. Additive: the response member is omitted when unset, so a view that sets no hint has a byte-identical `/metadata` payload. The TypeScript client is untouched (it types wire DTOs, not metadata). See §2.26. |
| **D150+** | **next free** | Use for new decisions. The 2026-07-31 audit remediation (D141–D149) was the last landed change. |

Observability-doc-local: `10-operations-and-observability.md` also lists D100/D102 (D102 = observability
names are an operational contract).

---

## 6. Engine + HTTP hardening — status (was: Spec 02 gap analysis)

The Spec 02 gap analysis that drove `query-engine-hardening` is now **resolved**. Snapshot:

### 6.1 Closed (implemented, tested, green)
| Gap | Decision | How it was closed |
|---|---|---|
| Non-deterministic paging | D106 | `EfViewExecutor.ApplySort` appends `KeyFields` as the ordered tiebreaker; empty sort orders by `KeyFields`. |
| PK not surfaced into metadata | D104 | `FieldMetadata.IsPrimaryKey` + `ViewMetadata.KeyFields` (init props); `.PrimaryKey()`/`Key(...)` populate them; registration fail-fast when absent; name convention removed. |
| No `In` cap / no complexity guards | D108 | `FilterCompiler` enforces `MaxInValues`/`MaxFilterDepth`/`MaxFilterLeaves`/`MaxFilterStringLength` from `HardLimits`; `FilterErrorCode.RequestTooComplex`. |
| `IQueryDialect` port vs code | D107 | Built the port: Core `IQueryDialect`; `DefaultQueryDialect` (EF, LIKE) + `NpgsqlQueryDialect` (new `a2n.Vista.EntityFrameworkCore.Npgsql`, ILIKE); `ProviderAwareFilterCompiler` retired. |
| ILIKE wildcard escaping | D107 | Escaping owned by the dialect (`% _ \`), verified. |
| Composite Detail-by-key | D109 | Executor normalizes a scalar or name→value map against `KeyFields`. |
| HTTP surface (action style) | D110 | `POST list/detail/export/create/update/delete` + `GET metadata`; key/query in JSON body; supersedes DR3. |

### 6.2 Still open (follow-ups)
- **D105 single-source PK auto-derivation** — **DONE (2026-07-01, D118)**: `VistaModelKeyDerivationService`
  (EF hosted service) completes `KeyFields` from `DbContext.Model` at startup for single-source executable
  views with no declared key; never overrides declared keys; fails closed otherwise. (Landed with the
  `style-b-executable` consumer.)
- **D107 startup provider guard** — **DONE (2026-06-27)**: `VistaDialectStartupValidator` (EF hosted
  service) throws on a specific-dialect/provider mismatch and warns on default-dialect+PostgreSQL.
- **Per-channel Search/Scope enforcement** — **DONE (2026-06-27, D111)**: the `datatables-adapter` spec
  added `Search`/`Scope` sub-tree slots to `ViewQueryRequest`; the executor compiles each under its origin.
  `VistaSearchMerge` routes global search to the `Search` slot. (Was deferred under DR9.)
- **Masking runtime** — **DONE (2026-07-01, D118)**: `MaskApplier` applies `MaskField` transforms post-
  projection in memory on List/Detail/export (SQL unchanged), fail-closed, AOT-clean via generated
  `MaskAccessor` with an RUC reflection fallback for Style A.
- **Write path / CRUD (M12)** — **DONE (2026-07-07, D119/D120)**: Create/Update/Delete on the
  `IViewExecutor` write facet — `MapWritable` whitelist, protected keys/token, optimistic concurrency,
  server-trusted scope, single `SaveChanges`, minimal write responses, RFC 7807 write errors; reflection
  mapper behind a generator-fillable seam. Bulk still deferred. (See §2.12.)
- **Export formatting** (CSV/XLSX), **metadata cache headers**, **full HTTP TestServer integration test** —
  all DONE (see §7).
- **Doc prose**: `docs/spec/02` §10 (dialect port) and `docs/spec/05` (action surface) prose not yet
  rewritten; authoritative decisions captured here (§2.4/§2.5/§5) and in the two Kiro specs.

---

## 7. Backlog / known gaps (tech debt)

- **Write path (DR7 → M12)** — **DONE (2026-07-07, D119/D120)**: Create/Update/Delete implemented on the
  reflection `TCrud → entity` mapper (behind a generator-fillable seam), with mass-assignment whitelist,
  optimistic concurrency, single `SaveChanges`, and the RFC 7807 write-error model. **The generated write
  mapper + build-time diagnostics landed (2026-07-09, D121/D122; §2.13)** — the store is now filled by
  `WriteMapperGenerator`, the reflection mapper is a fallback only, and VISTA0030/0031/0032 are build-time
  errors (interim startup guards retired). **Remaining write-path debt:** **bulk** ops (v1.x; array body →
  400 today). Its own specs (`write-path`, `source-generator-write-mapper`). See §2.12/§2.13.
- **Style B executable (DR5)** — **DONE (2026-07-01, D118)**: typed Style B views are executable for
  List/Detail via the generated `ICompiledViewExecutionPlan` (`AddVista` adopts it; metadata-only views
  fail fast on execution). See §2.11.
- **Masking runtime** — **DONE (2026-07-01, D118)**: `MaskApplier` applies `MaskField` transforms on
  materialization (fail-closed, post-projection, SQL unchanged). See §2.11.
- **Per-channel enforcement** — bind to Spec 04 adapters.
- **Source generator (Pillar 3)** — **Phase 1 landed (M9/D117, §2.10)**, **Phase 2 landed (D118, §2.11):**
  executable typed Style B plans + member-access for filter/sort + masking runtime + D105 PK derivation,
  **the write-DSL phase landed (D121/D122, §2.13):** the generated write mapper + build-time diagnostics
  (VISTA0030–0033), and **the HTTP-surface phase landed (D123/D124, §2.14):** a generated Core-only
  dispatch invoker + `ViewInvokerStore` (D123) plus an AOT-clean serialization seam in AspNetCore (D124:
  `TypeInfoResolverChain` = shipped `VistaStaticJsonContext` → developer `App_Json_Context` via
  `AddVistaJsonContext` → opt-out reflection fallback), diagnostics `VISTA0040`/`VISTA0041` — the full
  typed Style B HTTP round-trip is now IL2026/IL3050-clean. **The per-view `JsonTypeInfo` phase landed
  (D125/D126, §2.15)** (`JsonSerializerContext`-equivalent via `JsonMetadataServices`), making the developer
  `App_Json_Context` optional. **The OpenAPI emitter landed (M18, D127/D128, §2.16)** as the separate opt-in
  `a2n.Vista.OpenApi` package (a metadata/seam consumer, not a source generator). **Remaining (planned, not
  started):** ~~Style A accessor/serialization coverage~~ — **DONE (2026-07-13, D129/D130, §2.17)**; the
  only remaining generator-adjacent debt is cross-assembly discovery (D97) and `MapView<TView>()` (DR10),
  both v1.x. **With M9-P6 landed, M9 (the Source Generator, Pillar 3) is complete.** **Dependency note:**
  **M17 (TS client) landed (2026-07-14, D131/D132, §2.18)** on the OpenAPI document (M18) — it consumes the
  emitted document only and references no Vista package.
- **Observability (D100) & versioning (D99)** — designed, not built.
- **Adapters (Spec 04, Pillar 2 client half)** — **DataTables.NET + export (CSV/XLSX) + QueryBuilder
  metadata schema landed** (§2.7/§2.8/§2.9); remaining reference adapters (AG Grid, MudBlazor, OData, …)
  are follow-ups.
- **Legacy Kiro specs in Indonesian** — **no migration needed** (`.kiro/` is git-ignored; English not
  required for unpublished tooling — see Language policy in §4).
- **`RouteRoot` global default override** — model R uses a fixed default `/api/views` for ungrouped
  views; to change it globally, wrap registrations in a `RouteGroup`. Add an ergonomic override only if
  demanded.
- **D105 single-source PK auto-derivation** — **DONE (2026-07-01, D118)**: `VistaModelKeyDerivationService`
  completes `KeyFields` from `DbContext.Model` at startup for single-source executable views; explicit keys
  untouched; fails closed otherwise.
- **D107 startup provider guard** — **DONE (2026-06-27)**: `VistaDialectStartupValidator` warns/throws
  on a dialect vs `Database.ProviderName` mismatch.
- **Export pipeline** — **DONE (2026-06-27, D115)**: pluggable `IViewExportWriter` (built-in CSV + XLSX,
  BCL-only; developer-overridable via `AddVistaExportWriter<T>()`); `POST {route}/export` format-by-request.
- **HTTP TestServer integration test** — **DONE (2026-06-27)**: `HttpEndpointIntegrationTests`
  (list/metadata/datatable/page-size-400/metadata-cache over an in-process `TestServer`).
- **Metadata cache headers** — **DONE (2026-06-27)**: opt-in `EnableMetadataCaching()` (ETag/Cache-Control/304).
- **Doc prose** — **reconciled (2026-06-27)**: `docs/spec/02` §10 (dialect port + D111), `04` (adapter
  landed), `05` (action surface + D111/D112) carry up-to-date reconciliation notes; code remains authoritative.

---

## 8. Build & test commands (Windows, from repo root `d:\GitHub\a2n.Vista`)

```powershell
# Build all (net8/9/10)
dotnet build src\a2n.Vista.slnx -c Debug

# Run tests — TUnit uses Microsoft.Testing.Platform: use `dotnet run`, NOT `dotnet test`
# (`dotnet test` mis-parses args and reports "Zero tests ran").
dotnet run --project src\Tests\a2n.Vista.Tests\a2n.Vista.Tests.csproj -c Debug --framework net9.0
# Repeat with --framework net8.0 / net10.0 to cover all TFMs.

# Northwind example end-to-end self-test (example targets net8.0 ONLY):
dotnet run --project src\Examples\Northwind --framework net8.0 -c Debug -- selftest
```

- Tests live in `src\Tests\a2n.Vista.Tests` (TUnit). Tests go through **public API** where possible
  (no `InternalsVisibleTo` for the test project); `InternalsVisibleTo` is granted only to
  `a2n.Vista.EntityFrameworkCore`.
- Reflection-path tests carry `[UnconditionalSuppressMessage("Trimming", "IL2026...", ...)]`.
- Cross-assembly `View<TQuery>` override must be `protected override` (not `protected internal`).

---

## 9. Key code locations

- Contracts: `src/a2n.Vista.Core/Contracts/` (`ViewQueryRequest`, `FilterNode`, `FilterOperator`,
  `FilterOrigin`, `SortSpec`).
- Metadata: `src/a2n.Vista.Core/Metadata/` (`ViewMetadata`, `FieldMetadata`, `HardLimits`,
  `ViewAccessorRegistry` — static process-wide store `viewName → { field → Func<object,object?> }` the
  generated module initializers populate; M9/D117).
- Authoring: `src/a2n.Vista.Core/Authoring/` (`View<>`, `ViewTemplate<>`, `IViewBuilder*`,
  `IFieldBuilder`/`FieldBuilder`/`IFieldBuilderState`, `ViewBuilder`).
- Filter engine: `src/a2n.Vista.Core/Filter/FilterCompiler.cs`; dialect port `Filter/IQueryDialect.cs`.
- Ports: `src/a2n.Vista.Core/Ports/` (`IViewExecutor`, `IViewScope`, `IViewRegistry`, `ViewListResult`).
- EF execution + registration: `src/a2n.Vista.EntityFrameworkCore/` (`Execution/EfViewExecutor.cs`
  — incl. the non-RUC `ListCompiledAsync`/`DetailCompiledAsync` compiled read path (D118),
  `Execution/ICompiledViewExecutionPlan.cs` + `Execution/GeneratedExecutionPlanStore.cs` (D118),
  `Execution/MaskApplier.cs` + mask-spec registry (D118), `Execution/DefaultQueryDialect.cs`,
  `DependencyInjection/IVistaBuilder.cs` + `VistaBuilder.cs` (drains the plan store on `Register<TView>()`),
  `DependencyInjection/VistaServiceCollectionExtensions.cs`, `Hosting/VistaDialectStartupValidator.cs`
  — startup provider guard, `Hosting/VistaModelKeyDerivationService.cs` — D105 startup PK auto-derivation
  (D118)). `ProviderAwareFilterCompiler` was retired.
- Npgsql dialect: `src/Adapters/a2n.Vista.EntityFrameworkCore.Npgsql/` (`NpgsqlQueryDialect.cs`,
  `VistaNpgsqlServiceCollectionExtensions.cs` → `AddVistaNpgsql()`).
- Adapter contract (Core): `src/a2n.Vista.Core/Adapters/` (`IViewAdapter.cs` + `ViewAdapter<,>`,
  `AdapterRequest.cs`, `AdapterListResult.cs`, `AdapterBindException.cs`, `IViewMetadataAdapter.cs`).
- Export (Core): `src/a2n.Vista.Core/Export/` (`IViewExportWriter.cs`, `ExportColumns.cs` — incl. the
  AOT-clean `Value(viewName, row, fieldName)` overload that prefers `ViewAccessorRegistry` and falls back
  to the RUC reflection read; M9/D117), `CsvViewExportWriter.cs`, `XlsxViewExportWriter.cs` (both thread
  `view.Name` through the new overload).
- Source generator: `src/a2n.Vista.SourceGenerators/` (`netstandard2.0`, no Vista project ref):
  `ViewAccessorGenerator.cs` (the `IIncrementalGenerator` — predicate/transform, equatable `ViewModel` +
  `EquatableArray<T>`, accessor-map + `[ModuleInitializer]` emission; **Phase 2** adds the
  `CompiledViewExecutionPlan_<View>` emitter — projection reproduction, member-access map, typed sort
  appliers, `MaskAccessor` get/set, plan-store `[ModuleInitializer]`); **write-DSL phase (D121/D122):**
  `WriteMapperGenerator.cs` (the second `IIncrementalGenerator` — `MapWritable` analyzer, equatable
  `WriteMapperModel`/`WriteMappingModel`, safe-subset `WriteMapper` emitter + store `[ModuleInitializer]`);
  `DiagnosticDescriptors.cs` (`VISTA0001` error, `VISTA0002` info, **`VISTA0003`** warning, **`VISTA0020`**
  error — D118; **`VISTA0030`/`VISTA0031`/`VISTA0032`** errors + **`VISTA0033`** warning — D121/D122),
  `LocationInfo.cs`, `TrackingNames.cs`, `AssemblyMarker.cs`, `AnalyzerReleases.{Shipped,Unshipped}.md`.
- Write seams (Core/EF; D119/D120, filled by D121): `src/a2n.Vista.Core/Write/` (`WriteMapper`,
  `IWriteFacetRegistry`); `src/a2n.Vista.EntityFrameworkCore/Execution/` (`GeneratedWriteMapperStore`,
  `WriteMapperResolver`, `ReflectionWriteMapper`). The interim `ViewBuilderOfTCrud.ValidateWriteFacet`
  zero-mapping/non-scalar/key-token guards were **retired** (D122); the primary-key executability guard
  remains.
- Masking primitives (Core): `src/a2n.Vista.Core/Metadata/` (`MaskSpec`, `MaskAccessor`); captured by
  `Authoring/ViewBuilder.cs` (`MaskField` records predicate + masker; D118).
- DataTables adapter: `src/Adapters/a2n.Vista.Adapters.DataTablesNet/` (`DataTablesModels.cs`,
  `DataTablesAdapter.cs`, `QueryBuilderParser.cs`, `ExternalFilterParser.cs`, `QueryBuilderModels.cs`
  incl. `DataTablesJsonContext`, `QueryBuilderSchemaAdapter.cs`).
- AspNetCore: `src/a2n.Vista.AspNetCore/` (`Routing/VistaEndpointRouteBuilderExtensions.cs` — action-style
  mapper; `Serialization/FilterNodeJsonConverter.cs`, `VistaJson.cs`, `VistaKeyReader.cs`;
  `Execution/VistaRequestEnvelopes.cs`, `VistaMetadataResponse.cs`, `VistaSearchMerge.cs`,
  `VistaInvalidRequestException.cs`, `ViewRequestExecutor.cs`; `Authorization/IViewAuthorizer.cs` +
  `ViewFacet.cs`; `Configuration/VistaEndpoint*`, `Hosting/VistaStartupValidator.cs`,
  `Diagnostics/VistaProblemResults.cs`); adapter glue (`Execution/AdapterRequestFactory.cs`,
  `Execution/ViewRequestExecutor.cs` → `ListForAdapterAsync`,
  `DependencyInjection/VistaAdapterServiceCollectionExtensions.cs` → `AddVistaAdapter<T>()` +
  `AddVistaMetadataAdapter<T>()`; `DependencyInjection/VistaExportServiceCollectionExtensions.cs` →
  `AddVistaExportWriter<T>()`; `ViewRequestExecutor.ExportRowsAsync`).
- OpenAPI emitter (M18/D127/D128): `src/a2n.Vista.OpenApi/` (opt-in package, refs `a2n.Vista.AspNetCore`):
  `VistaOpenApiDocumentBuilder.cs` (metadata-driven, structure reflection-free), `VistaOpenApiDocumentCache.cs`
  (build-once), `VistaOpenApiOptions.cs` (+ `VistaSecurityScheme`, fail-fast `Validate()`),
  `VistaOpenApiServiceCollectionExtensions.cs` → `AddVistaOpenApi(configure?)`,
  `VistaOpenApiEndpointRouteBuilderExtensions.cs` → `MapVistaOpenApi()`, `FacetOperation.cs` (the
  facet→operation table = the endpoint-parity source), `Model/` (`OpenApiModel.cs`, `OpenApiCollections.cs`
  — the hand-authored object model), `Schema/`+`Schemas/` (envelope/`FilterNode`/`ProblemDetails`
  descriptors + the RUC `DtoSchemaGenerator`), `Serialization/` (the source-gen `JsonSerializerContext`),
  and `AspNetCorePipeline/` (`VistaOpenApiDocumentTransformer.cs` + `VistaOpenApiPipeline*Extensions.cs` —
  the optional net9/net10 `Microsoft.AspNetCore.OpenApi` provider, TFM-guarded). Both public APIs carry
  `[RequiresUnreferencedCode]`; the emitter adds **no** `VISTA####` diagnostics (unresolvable-member notices
  go through `ILogger`).
- TypeScript client generator (M17/D131/D132): `src/a2n.Vista.Client.TypeScript/` (standalone CLI, **no**
  Vista project ref): `Acquire/` (`IOpenApiSource`, `FileSource`, `HttpsSource`), `Parse/` (`OpenApiParser`),
  `Resolve/` (`RefResolver`), `Model/` (`OpenApiDocument`), `Modeling/` (`DtoModelBuilder`, `EnvelopeCatalog`,
  `EnvelopeReLifter`, `FilterNodeModelBuilder`, `TypeMapper`, the client model + config/notice types),
  `Emit/` (`DeterministicOrder`, `TypesEmitter`, `FilterNodeEmitter`, `ViewClientEmitter`, `IndexEmitter`,
  `DocsEmitter`, `GeneratedFile`, and `Emit/Runtime/` — `HttpTransportEmitter`, `AuthEmitter`,
  `ResultEmitter`, `UrlEmitter`, `ClientContextEmitter`, `RawPayloadEmitter`), `Write/` (`OutputWriter`),
  `Cli/` (`CommandLine`, `CliHost`, `IPipelineRunner`), `Pipeline/`, `Parity/`, `Program.cs`; the TS
  generated-runtime property harness under `tests/ts-runtime/` (fast-check under Node).
- Example: `src/Examples/Northwind/` (`Program.cs`, `Views/NorthwindViews.cs` — incl. composite
  `vOrderDetail`, `SelfTest.cs`, `OpenApiSelfTest.cs` — the M18 OpenAPI self-test); `src/Examples/a2n.Vista.GeneratorSample/` (a real consumer assembly that
  exercises the generator end to end; referenced by the test project) and `src/Examples/a2n.Vista.AotProbe/`
  (net8, `IsAotCompatible`, IL2026/IL3050-as-errors build proving the generated-accessor export path **and**
  the generated Style B List/Detail compiled path are trim/AOT-clean — `StyleBProbeView.cs` +
  `StyleBExecutableProbe.cs`, D118) — M9/D117 + D118. Phase-2 generator-consumer fixtures:
  `src/Examples/a2n.Vista.GeneratorExecSampleP5` (conditional masking), `…ExecSampleP6` (non-probeable
  masked field), `src/Examples/a2n.Vista.Examples.StyleBExecP7` (single-source PK derivation). Write-DSL
  phase (D121): `src/Examples/a2n.Vista.GeneratorWriteMapperSample` (representative writable views —
  one/many mappings, aliasing, empty whitelist, nullable + `byte[]` scalars); the AotProbe drives a
  generated write mapper end-to-end.
- Tests: `src/Tests/a2n.Vista.Tests/` (`AuthorizationTests`, `MaskingTests`, `RouteGroupTests`,
  `WireVersionTests`, `EnforcementTests`, `DefaultAllowTests`, `PagingTests`, `TypingInvariantTests`,
  `WidgetTestFixtures`, `QueryEngineHardeningTests`, `HttpSurfaceTests`, `DialectStartupGuardTests`,
  `ViewAccessorRegistryTests`, `ExportParityTests`, `GeneratorEndToEndTests`; **D118:**
  `GeneratedRucParityPropertyTests`, `ListPageBoundPropertyTests`, `DetailByKeyRoundTripPropertyTests`,
  `DisallowedFieldRejectionPropertyTests`, `ConditionalMaskingPropertyTests`,
  `NonProbeableMaskedFieldPropertyTests`, `MaskingFailClosedAndOptInTests`,
  `SingleSourcePkDerivationPropertyTests`, `ModelKeyDerivationFailureTests`, `WritableStyleB501Tests`);
  generator snapshot/golden tests in `src/Tests/a2n.Vista.SourceGenerators.Tests/`
  (`ViewAccessorGeneratorTests`, `ViewExecutionPlanGeneratorTests`, `GeneratorDiagnosticsTests`,
  `GeneratorTestHarness`, `SnapshotDeterminismPropertyTests`; **D121/D122:** `WriteMapperRecognitionTests`,
  `WriteMapperGeneratorPackagingTests`). **M18 OpenAPI (D127/D128):** `src/Tests/a2n.Vista.Tests/OpenApi/`
  (`EmitterFixtures.cs` + `RegistryGenerators.cs`; the parity/structure properties
  `EndpointParityPropertyTests`, `DtoSchemaWireParityPropertyTests`, `EnvelopeFilterNodeWireParityPropertyTests`,
  `ReferentialIntegrityPropertyTests`, `OpenApiValidityPropertyTests`, `DeterminismPropertyTests`,
  `SecurityPosturePropertyTests`, `ErrorResponsePropertyTests`, `OperationIdUniquenessPropertyTests`,
  `AdapterEndpointAbsencePropertyTests`; the example/guard tests `OpenApiFixedShapeExampleTests`,
  `OpenApiFixturesSmokeTests`, `OpenApiLayeringGuardTests`) plus root-level `DtoSchemaGeneratorTests` and the
  `OpenApi*Tests` (document assembly/builder/serving/registration/security/coexistence/pipeline). The AOT
  probe's `OpenApiDescriptorProbe.cs` covers the envelopes+`FilterNode`-only AOT-clean document.
  **M17 TS client (D131/D132):** `src/Tests/a2n.Vista.Client.TypeScript.Tests/` — 136 tests/TFM via CsCheck
  on TUnit: the pipeline-stage tests (`FileSourceTests`, `HttpsSourceTests`, `OpenApiParserMalformedTests`,
  `DtoModelBuilderTests`, `FilterNodeModelBuilderTests`, `TypesEmitterTests`, `ViewClientEmitterTests`,
  `OutputWriterAtomicityTests`, `CliTests`, `LayeringSmokeTests`, `FixtureSmokeTests`), the 20 correctness
  properties (`DeterminismHarnessPropertyTests`, `TypeMappingFidelityPropertyTests`,
  `RefResolutionSoundnessPropertyTests`, `GenericReLiftingPropertyTests`, `WriteFacetGatingPropertyTests`,
  `MissingRequiredEnvelopePropertyTests`, `OpenApiVersionGatingPropertyTests`,
  `PerViewReadFacetSetPropertyTests`, `ExportFormatUnionPropertyTests`,
  `UnmappableMemberDegradationPropertyTests`, `NoUiOrGridDependencyPropertyTests`,
  `NoEmbeddedCredentialPropertyTests`, `OneDeclarationPerNamePropertyTests`,
  `DocumentLevelSecurityTests`, `DeterministicOrderTests`, the `RepresentativeValue*`/parity harnesses),
  and the `Fixtures/` sample documents.
