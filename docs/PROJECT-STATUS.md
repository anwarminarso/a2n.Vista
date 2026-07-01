# a2n.Vista — Project Status & Session Handoff

> Status: **LIVING DOCUMENT** — update as work proceeds.
> Last updated: 2026-07-01 (`style-b-executable` **LANDED**: D118 — source-generator Phase 2 =
> M10 + M11 + M13. Second generator emitter produces AOT-clean `ICompiledViewExecutionPlan` per typed
> Style B view → executable List/Detail (DR5 closed for typed views); D105 single-source PK
> auto-derivation at startup; masking runtime on materialization. Build green net8/9/10, **156 tests/TFM**,
> Northwind self-test PASS, AOT probe clean. M9 Phase 1 landed earlier: D117 — incremental generator +
> shape-driven export accessors)
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
  endpoints). **Implemented.**
- **Pillar 2 — Adapters + neutral query engine** (server half = query engine, **built & hardened**:
  PK-in-metadata, deterministic paging, DoS guards, `IQueryDialect` port, composite keys — see §2.4;
  HTTP surface is now action-style POST + `GET metadata` — see §2.5; multi-channel Search/Scope request —
  see §2.7; client half = grid adapters, **DataTables.NET reference adapter built** — see §2.7; other
  grid adapters not built).
- **Pillar 3 — Source generator** (AOT-clean codegen). **Phase 1 landed (M9, D117):** an incremental
  generator emits shape-driven field accessors for typed Style B views, registered into a Core store the
  export pipeline prefers over reflection (coexistence) — see §2.10. **Phase 2 landed (M10+M11+M13,
  D118):** a second emitter produces an AOT-clean `ICompiledViewExecutionPlan` per typed Style B view
  (compile-time projection, per-field member-access, typed sort appliers, masked-field accessors) →
  executable List/Detail (DR5 closed for typed views), plus startup single-source PK auto-derivation
  (D105) and the masking runtime on materialization — see §2.11. The remaining reflection paths
  (Style A serialization, write mapping) stay `[RequiresUnreferencedCode]` until later phases.

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
  zero IL2026/IL3050 on that path; Style A keeps its RUC annotation. Writable Style B endpoints
  (Create/Update/Delete) still return **501** (M12 unbuilt; DR7); the generated plan implements List/Detail
  only (R4.7).
- **Property tests (1–8)** placed next to the code they validate: generated/RUC behavioral parity (model-
  based), List page-bound + unfiltered total, Detail-by-key round-trip, disallowed-field rejection-before-
  SQL (interceptor spy), conditional masking at materialization, masked-field non-probeable, single-source
  PK auto-derivation, snapshot determinism. EF-aware generator-consumer fixtures
  (`a2n.Vista.GeneratorExecSampleP5/P6`, `a2n.Vista.Examples.StyleBExecP7`) emit real compiled plans the
  tests run through `EfViewExecutor`.

**Still deferred after Phase 2:** the **write path (M12, DR7)** — Create/Update/Delete stay 501;
`JsonSerializerContext` generation; OpenAPI; TypeScript client; Style A (anonymous) accessor/serialization
generation; cross-assembly discovery polish (D97) and `MapView<TView>()` (DR10).

---

## 3. Documentation map (authoritative)

Under `docs/spec/` (all **English** after the 2026-06-20 migration; see §4 language policy):
- `01-view.md` — **foundation**; View concept, public contract, full Decision Log (D1–D50, §13.1
  DR1–DR10, §13.2 D94–D103). Status: IMPLEMENTED, reconciled with code.
- `02-filter-and-query.md` — query engine (Pillar 2 server half). Status: IMPLEMENTED & **hardened**
  (D104–D109 via `query-engine-hardening`); **prose lags the code** for §10 (dialect port) — the code is
  authoritative (see §2.4/§6).
- `03-source-generator.md` — Pillar 3. Status: **DESIGN INTENT (frozen; D71–D81)** — remains the
  authoritative intent; **Phase 1 has landed** (M9/D117, shape-driven export accessors — see §2.10) and
  **Phase 2 has landed** (D118, `style-b-executable` — executable typed Style B + D105 + masking runtime;
  see §2.11). The rest (JSON contexts, OpenAPI, Style A) is still forward-looking.
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
  scoping (nested groups combine), `RegisterAssembly(...)` (`[RequiresUnreferencedCode]`, metadata-only),
  default root for ungrouped. View names are **globally unique**; a view maps to **exactly one** endpoint
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
- **DR3** Pillar 1 List = **`GET {root}/{viewName}`** (query string). `POST .../query` is the Pillar 2
  adapter form (layers on top).
- **DR4** `WithValidator`/`WithInterceptor` deferred (not in code).
- **DR5** Style B `Register<TView>()` is **metadata-only** (not executable without an `IViewExecutionPlan`
  via `Register<TView>(plan)` or source-gen).
- **DR6** List result = **`ViewListResult<TRow>`** (`PagedResult<TRow> Page` + `long TotalRowsUnfiltered`).
  **Supersedes** the proposed `ViewQueryResult<T>` (Spec 02 §6.2, D51).
- **DR7** Write endpoints mapped but EF write **not implemented** → **501** (writable) / **404** (read-only).
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
| **D119+** | **next free** | Use for new decisions. |

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
- **Export formatting** (CSV/XLSX), **metadata cache headers**, **full HTTP TestServer integration test** —
  all DONE (see §7).
- **Doc prose**: `docs/spec/02` §10 (dialect port) and `docs/spec/05` (action surface) prose not yet
  rewritten; authoritative decisions captured here (§2.4/§2.5/§5) and in the two Kiro specs.

---

## 7. Backlog / known gaps (tech debt)

- **Write path (DR7)** — Create/Update/Delete return 501. Needs `TCrud → entity` mapping (reflection
  now, source-gen later), concurrency, SaveChanges, bulk. Its own spec (M12). **Stays 501 through D118.**
- **Style B executable (DR5)** — **DONE (2026-07-01, D118)**: typed Style B views are executable for
  List/Detail via the generated `ICompiledViewExecutionPlan` (`AddVista` adopts it; metadata-only views
  fail fast on execution). See §2.11.
- **Masking runtime** — **DONE (2026-07-01, D118)**: `MaskApplier` applies `MaskField` transforms on
  materialization (fail-closed, post-projection, SQL unchanged). See §2.11.
- **Per-channel enforcement** — bind to Spec 04 adapters.
- **Source generator (Pillar 3)** — **Phase 1 landed (M9/D117, §2.10)** and **Phase 2 landed (D118, §2.11):**
  executable typed Style B plans + member-access for filter/sort + masking runtime + D105 PK derivation.
  Remaining phases: `JsonSerializerContext`, OpenAPI, Style A accessor/serialization, plus cross-assembly
  discovery (D97) and `MapView<TView>()` (DR10).
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
  appliers, `MaskAccessor` get/set, plan-store `[ModuleInitializer]`), `DiagnosticDescriptors.cs`
  (`VISTA0001` error, `VISTA0002` info, **`VISTA0003`** warning + **`VISTA0020`** error — D118),
  `LocationInfo.cs`, `TrackingNames.cs`, `AssemblyMarker.cs`, `AnalyzerReleases.{Shipped,Unshipped}.md`.
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
- Example: `src/Examples/Northwind/` (`Program.cs`, `Views/NorthwindViews.cs` — incl. composite
  `vOrderDetail`, `SelfTest.cs`); `src/Examples/a2n.Vista.GeneratorSample/` (a real consumer assembly that
  exercises the generator end to end; referenced by the test project) and `src/Examples/a2n.Vista.AotProbe/`
  (net8, `IsAotCompatible`, IL2026/IL3050-as-errors build proving the generated-accessor export path **and**
  the generated Style B List/Detail compiled path are trim/AOT-clean — `StyleBProbeView.cs` +
  `StyleBExecutableProbe.cs`, D118) — M9/D117 + D118. Phase-2 generator-consumer fixtures:
  `src/Examples/a2n.Vista.GeneratorExecSampleP5` (conditional masking), `…ExecSampleP6` (non-probeable
  masked field), `src/Examples/a2n.Vista.Examples.StyleBExecP7` (single-source PK derivation).
- Tests: `src/Tests/a2n.Vista.Tests/` (`AuthorizationTests`, `MaskingTests`, `RouteGroupTests`,
  `WireVersionTests`, `EnforcementTests`, `DefaultAllowTests`, `PagingTests`, `TypingInvariantTests`,
  `WidgetTestFixtures`, `QueryEngineHardeningTests`, `HttpSurfaceTests`, `DialectStartupGuardTests`,
  `ViewAccessorRegistryTests`, `ExportParityTests`, `GeneratorEndToEndTests`; **D118:**
  `GeneratedRucParityPropertyTests`, `ListPageBoundPropertyTests`, `DetailByKeyRoundTripPropertyTests`,
  `DisallowedFieldRejectionPropertyTests`, `ConditionalMaskingPropertyTests`,
  `NonProbeableMaskedFieldPropertyTests`, `MaskingFailClosedAndOptInTests`,
  `SingleSourcePkDerivationPropertyTests`, `ModelKeyDerivationFailureTests`, `WritableStyleB501Tests`);
  generator snapshot/golden tests in `src/Tests/a2n.Vista.SourceGenerators.Tests/`
  (`ViewAccessorGeneratorTests`, `GeneratorTestHarness`, `SnapshotDeterminismPropertyTests`).
