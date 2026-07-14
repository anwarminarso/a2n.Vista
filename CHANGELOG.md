# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is `0.x`, anything may change between releases.

## [Unreleased]

### Added
- **Adapters** — the **AG Grid** adapter, `a2n.Vista.Adapters.AgGrid` (Decision
  Log D133–D136; M16): the second Pillar 2 client-half grid adapter, proving the
  neutral `IViewAdapter` contract generalizes to a grid whose request shape
  differs substantially from DataTables. Core-only (no EF/ASP.NET/Npgsql
  reference); no `a2n.Vista.Core`/`EntityFrameworkCore`/`AspNetCore` type is added
  or changed, and the AspNetCore adapter glue is reused verbatim (only a new
  `RouteSuffix` and the JSON-body read path are new). **D133** — an
  `AgGridAdapter : ViewAdapter<AgGridRowsRequest, AgGridRowsResponse>` (`Id` and
  `RouteSuffix` `"aggrid"`) exposed at `POST {route}/aggrid`, with three pure,
  deterministic mapping steps (`BindRequest`/`ToQuery`/`ToResponse`); request
  POCOs (de)serialize through a source-gen `AgGridJsonContext` (AOT-clean —
  anonymous Style A rows ride the documented D96 `[RequiresUnreferencedCode]`
  path, so no new reflection path is introduced). **D134** — a pure
  `AgGridFilterModelParser` maps the AG Grid `filterModel`
  (text/number/date/`set` plus combined AND/OR) to a `FilterNode` per a locked
  table (`inRange` → `Between`, `blank`/`notBlank` → `IsNull`/`FilterNot`, `set`
  → `In`); **Advanced Filter is deferred for v1** — an Advanced-Filter payload is
  rejected loudly (`AdapterBindException` → 400 `adapter-bind-failed`), never
  silently dropped. **D135** — block paging (`PageSize = EndRow - StartRow`,
  `Page = StartRow / PageSize`; a non-positive `PageSize` is passed through
  unchanged so the engine rejects it) and the `{ rowData, rowCount }`
  `LoadSuccessParams` response (`rowCount` is the filtered total for AG Grid
  last-block detection; `RecordsTotal` is not surfaced); the `filterModel` lands
  only in the `Filter` channel and a quick filter only in the `Search` channel,
  and the adapter never enforces the tri-whitelist (per-channel engine
  validation). **D136** — quick-filter transport via `?q=` folded into
  `AdapterRequest.Values` (zero host change), plus a thin hand-written
  `IServerSideDatasource` for the sample (the generated TypeScript client is
  OpenAPI-driven and adapter endpoints are not yet in the OpenAPI document). Ships
  an `a2n.Vista.Examples.AgGridNorthwind` sample (net8.0-only): an ASP.NET host,
  an AG Grid + TypeScript front-end with a `tsc --noEmit` typecheck gate, and a
  guarded `dotnet run -- selftest` round-trip. Additive-only — no server route,
  wire, or behavior change.
- **CI / packaging** — two GitHub Actions workflows under `.github/workflows/`
  (M19). `ci.yml` restores and builds the full solution (`src/a2n.Vista.slnx`) in
  Release and runs the three TUnit suites (`a2n.Vista.Tests`,
  `a2n.Vista.SourceGenerators.Tests`, `a2n.Vista.Client.TypeScript.Tests`) via
  `dotnet run --project … --framework <tfm>` (not `dotnet test`) across a
  net8.0/net9.0/net10.0 matrix on `push`/`pull_request` to `main` and
  `workflow_dispatch`. `publish.yml` packs and pushes to nuget.org on a published
  GitHub Release (the tag drives the version, leading `v` stripped) or manual
  `workflow_dispatch`, using **NuGet Trusted Publishing (OIDC)** via
  `NuGet/login@v1` — no long-lived API key is stored (`permissions: id-token:
  write`; the nuget.org account name is the `NUGET_USER` secret). It ships only
  the seven implemented libraries (`Core`, `EntityFrameworkCore`, `AspNetCore`,
  `OpenApi`, `EntityFrameworkCore.Npgsql`, `Adapters.DataTablesNet`,
  `Client.TypeScript`); the empty scaffolds and `a2n.Vista.SourceGenerators`
  (packaging model unsettled) are intentionally excluded. Additive-only — no
  source, wire, or package-content change.
- **Client.TypeScript** — a standalone TypeScript client generator,
  `a2n.Vista.Client.TypeScript` (Decision Log D131/D132; M17): a .NET CLI that
  reads a Vista **OpenAPI 3.0.4** document (from a file or an HTTPS URL) and emits
  a framework-agnostic, strongly-typed TypeScript client — per-view `TRow`/`TCrud`
  DTO types, the fixed Vista request/response envelopes, the presence-discriminated
  `FilterNode` union, the RFC 7807 `ProblemDetails` type, one re-lifted generic
  `ViewListResult<TRow>`/`PagedResult<TRow>` per view, and a per-view typed client
  over an injectable HTTP transport and auth provider. **D131** — the OpenAPI
  document is the single generation source, consumed over a one-way, buffered,
  pure pipeline (**acquire → parse → resolve → model → emit → write**) that makes
  determinism and all-or-nothing failure structural; the generator references
  **no** Vista package (not Core, EF, AspNetCore, or OpenApi). **D132** —
  secure-by-default posture: read facets are the default and write facets are
  gated **off** behind an explicit opt-in; it never embeds a credential, defaults
  transport to HTTPS (a non-HTTPS non-loopback base URL is a typed config
  failure), and surfaces every outcome as one total, non-throwing discriminated
  `ClientResult<T>` (with distinct `unauthorized`/`not-found`/428/409 members).
  Deterministic, atomic, UTF-8 (no BOM) output; purely additive — it touches no
  server code.
- **SourceGenerators** — Pillar 3, Style A coverage phase (Decision Log
  D129/D130), the final planned generator phase — with it the source generator
  (Pillar 3) is complete. **D129** — a fifth incremental generator
  (`StyleAShapeGenerator`), the first to key off an invocation rather than a class
  declaration, recognizes `ViewTemplate<TDbContext>.AddView<TRow>(…)` call sites
  (walking a chained `WithCrud<TCrud, TEntity>()`) and, for the nameable Style A
  subset, emits — keyed by the constant `AddView` name — export accessors and
  read-DTO `JsonTypeInfo` (`TRow`/`ViewListResult<TRow>`/`PagedResult<TRow>`) for a
  **named** `TRow`, plus `TCrud` `JsonTypeInfo` for **any** writable view (`TCrud`
  is always named), all shape-only into the existing `ViewAccessorRegistry` and
  `GeneratedJsonContextStore` (no new store, no new seam). **D130** — the
  reaffirmed permanent by-design boundary: an **anonymous** read row is unnameable
  in generated source, so its read serialization/export stay
  `[RequiresUnreferencedCode]` forever (reaffirms the two-authoring-styles AOT
  asymmetry), while the same view's `TCrud` write still binds AOT-clean — the
  asymmetry is within one view. Non-blocking diagnostics `VISTA0060` (covered),
  `VISTA0061` (anonymous read → RUC by design), `VISTA0062` (non-constant name) —
  Info — and `VISTA0063` (non-emittable member) — Warning; help docs under
  `docs/diagnostics/`. Mechanism-only — no wire change; byte-for-byte parity with
  the reflection oracle is the guard.
- **OpenApi** — a new opt-in `a2n.Vista.OpenApi` package that emits an accurate,
  deterministic **OpenAPI v3.x document** for every Vista View mapped to HTTP
  (Decision Log D127/D128; M18). It is a pure downstream consumer of the
  metadata model and the serialization seam and modifies neither. **D127** — a
  runtime, metadata-driven `VistaOpenApiDocumentBuilder` turns each
  `ViewMetadata` from `IViewRegistry` into the fixed operation set
  (`list`/`detail`/`metadata`/`export` for every view, plus
  `create`/`update`/`delete` when writable), over a hand-authored
  `OpenApiDocument` object model serialized byte-stably through its own
  source-gen `JsonSerializerContext`. Path/operation structure, security
  requirements, RFC 7807 error responses, and the polymorphic `FilterNode`
  `oneOf` schema are reflection-free; the Vista envelopes and `ProblemDetails`
  are hand-authored descriptors; only per-view `TRow`/`TCrud`/nested-POCO
  schemas come from a single `[RequiresUnreferencedCode]` `DtoSchemaGenerator`
  branch (the D96 AOT asymmetry), which emits a permissive `{}` schema plus a
  non-fatal notice for an unresolvable member rather than omitting it or
  throwing. Property names, enum-as-string, nullability, and BCL scalar
  type/format all track the seam options so schemas match the wire. **D128** —
  opt-in serving: `AddVistaOpenApi(configure?)` registers the builder, validated
  `VistaOpenApiOptions` (title, version, OpenAPI version, security scheme,
  endpoint path), and a build-once document cache; `MapVistaOpenApi()` maps
  `GET /openapi/v1.json` (configurable) returning the cached document inside the
  host auth pipeline (bypasses nothing). On net9.0/net10.0 an optional
  `VistaOpenApiDocumentTransformer` merges the Vista paths/components into an
  app's built-in `Microsoft.AspNetCore.OpenApi` pipeline document. The emitter is
  **off by default** and additive-only — every existing response is byte-for-byte
  unchanged. Correctness rests on two oracles: the live route table (endpoint
  parity) and the live serializer (schema/wire parity, validated
  instance-against-schema), with determinism as the stabilizer.
  `a2n.Vista.Core`/`EntityFrameworkCore`/`AspNetCore` gain no dependency on the
  new package. Adapter-endpoint documentation is out of scope for v1 (an
  extension hook only).
- **SourceGenerators / AspNetCore / Core** — Pillar 3, per-view `JsonTypeInfo`
  phase (Decision Log D125/D126): serialization is now fully self-service for
  typed Style B — a developer no longer has to author and register an
  `App_Json_Context`. **D125** — a fourth incremental generator
  (`ViewJsonContextGenerator`) emits, per covered typed Style B view, a
  reflection-free `IJsonTypeInfoResolver` built by hand via
  `System.Text.Json.Serialization.Metadata.JsonMetadataServices` (never the
  `[JsonSerializable]` attribute route — the generator-of-generator constraint)
  providing the `JsonTypeInfo` for the view's `TRow`, `ViewListResult<TRow>`,
  `PagedResult<TRow>`, and — when writable — `TCrud`, plus the collection/nullable/
  enum metadata those DTOs reach; a `[ModuleInitializer]` registers it into a new
  Core-resident, serializer-neutral `GeneratedJsonContextStore` (opaque `object`
  handles, so `a2n.Vista.Core` gains no System.Text.Json dependency). **D126** —
  `a2n.Vista.AspNetCore` drains the store and chains each generated context into
  the existing `TypeInfoResolverChain` ahead of the developer context and the
  reflection fallback, making the developer `App_Json_Context` **optional** without
  changing the seam or the dispatch invoker. Mechanism-only — no wire change;
  byte-for-byte parity with the reflection oracle is the guard (master Property 1 +
  round-trip Property 2). Non-blocking diagnostics `VISTA0050` (covered view, per-view
  `JsonTypeInfo` generated) and `VISTA0051` (a DTO member cannot be emitted
  reflection-free → the view falls back to the developer context / reflection);
  help docs under `docs/diagnostics/`. The AOT probe was extended to a full typed
  Style B round-trip with **no developer context and the reflection fallback
  removed** (green with IL2026/IL3050 as errors), and the Northwind example's
  developer `NorthwindJsonContext` was removed — its self-tests still pass on the
  generated per-view serialization.
- **SourceGenerators / AspNetCore / Core** — Pillar 3, HTTP-surface phase
  (Decision Log D123/D124): the last large reflection surface for typed Style B
  is closed, so the full `request → authorize → execute → serialize` path is now
  trim/AOT-clean (IL2026/IL3050-free). **D123** — a third incremental generator
  (`ViewInvokerGenerator`) emits, per covered typed Style B view, a Core-only
  reflection-free `IViewInvoker` that closes `IViewExecutor.List/Detail/Create/
  Update<T>` at compile time (no `MakeGenericMethod`, no `Task<TResult>.Result`
  or `ViewListResult<TRow>` reflection) plus a `[ModuleInitializer]` filling a
  Core-resident, first-wins `ViewInvokerStore`; `ViewRequestExecutor` prefers the
  generated invoker and confines `[RequiresUnreferencedCode]` to private
  `*ReflectionAsync` fallbacks. **D124** — a unified serialization seam in
  `a2n.Vista.AspNetCore`: a `TypeInfoResolverChain` over `VistaJson.Options`
  (shipped `VistaStaticJsonContext` → developer `App_Json_Context`(s) via
  `AddVistaJsonContext(...)` → an opt-out reflection fallback), a reflection-free
  `FilterNodeJsonConverter`, and a shared `VistaJsonWriter`; List/Detail/Export
  responses and write-model binding now (de)serialize through it. `a2n.Vista.Core`
  gains no System.Text.Json/EF/ASP.NET Core dependency. Mechanism-only — no wire
  change; byte-for-byte parity with the reflection path is the guard. Non-blocking
  diagnostics `VISTA0040` (uncovered candidate → reflection fallback) and
  `VISTA0041` (serialization guidance naming the exact `[JsonSerializable]` types);
  help docs under `docs/diagnostics/`. Per-view `JsonTypeInfo` auto-generation is a
  documented non-goal of this phase (generator-of-generator constraint).
- **SourceGenerators** — Pillar 3, write-DSL phase (Decision Log D121/D122): a
  second incremental generator (`WriteMapperGenerator`) that statically analyzes
  each analyzable typed Style B writable view's `MapWritable` chain and emits a
  reflection-free `WriteMapper` (casts + one whitelisted scalar assignment per
  safe mapping, declaration-ordered) plus a `[ModuleInitializer]` filling the
  `GeneratedWriteMapperStore`. `WriteMapperResolver` prefers the generated mapper
  over the reflection fallback with no executor changes, so the typed Style B
  write path is now AOT-clean. The interim write-authoring startup guards are
  promoted to build-time diagnostics `VISTA0030` (zero mappings), `VISTA0031`
  (non-scalar target), `VISTA0032` (key/token target) — all errors — and
  `VISTA0033` (unanalyzable chain → warning + reflection fallback); help docs
  under `docs/diagnostics/`.
- **SourceGenerators / EntityFrameworkCore** — Pillar 3, Phase 2 (Decision Log
  D118): a generated AOT-clean `ICompiledViewExecutionPlan` per typed Style B
  view (compile-time projection, per-field member-access, typed sort appliers,
  masked-field accessors) registered into `GeneratedExecutionPlanStore` and
  adopted by `AddVista`, making typed Style B views executable for List/Detail
  through a non-`[RequiresUnreferencedCode]` compiled path. Bundles single-source
  primary-key auto-derivation from `DbContext.Model` at startup (D105) and the
  masking runtime (`MaskField` transforms applied post-projection in memory,
  fail-closed, SQL unchanged). Diagnostics `VISTA0003`/`VISTA0020`.
- **Write path / CRUD** (Decision Log D119/D120): Create/Update/Delete for
  writable Style B views on the `IViewExecutor` write facet, replacing the prior
  501 stub. Default-deny `MapWritable` mass-assignment whitelist, protected keys
  and concurrency token, optimistic concurrency via `If-Match`/`ETag`,
  server-trusted scope, a single `SaveChanges` per operation, minimal (PK-only)
  write responses, and an RFC 7807 write-error vocabulary. The `TCrud → entity`
  mapping runs behind a fixed-signature seam (`WriteMapper`/`WriteMapperResolver`)
  that the generated write mapper now fills. Bulk operations deferred (array body
  → 400).
- **Adapters** — the **DataTables.NET** reference adapter (Decision Log
  D111–D114): a Core `IViewAdapter` contract, multi-channel `Search`/`Scope`
  request slots on `ViewQueryRequest`, `jsonQB`/`externalFilter` parsing, and the
  `POST {route}/datatable` endpoint. A pluggable **export pipeline** (D115):
  `IViewExportWriter` with built-in zero-dependency CSV and XLSX writers,
  overridable via `AddVistaExportWriter<T>()`. A per-grid **metadata-schema**
  emitter (D116): `IViewMetadataAdapter` + the jQuery-QueryBuilder `metadataQB`
  schema at `GET {route}/querybuilder`.
- **Query engine hardening** (Decision Log D104, D106–D109): a view key model in
  metadata (`FieldMetadata.IsPrimaryKey`/`ViewMetadata.KeyFields`, composite),
  deterministic paging (key-field tiebreaker), an `IQueryDialect` port with a
  default (LIKE) dialect and an optional Npgsql (ILIKE) dialect, DoS guards
  (filter depth/leaves/`In`-count/string-length), and composite Detail-by-key.
- **HTTP action surface** (Decision Log D110): `POST {route}/list|detail|export`
  and `GET {route}/metadata` with the key and query carried in the JSON body,
  superseding the earlier query-string List form. Opt-in metadata cache headers
  (`ETag`/`Cache-Control`/304) via `EnableMetadataCaching()`.
- **SourceGenerators** — Pillar 3, Phase 1 (Decision Log D117): an incremental
  generator (`ViewAccessorGenerator`) that recognizes typed Style B views by
  fully-qualified name and emits shape-driven field accessors plus a
  `[ModuleInitializer]` that registers them into a Core `ViewAccessorRegistry`.
  The export pipeline prefers generated accessors and falls back to reflection
  (coexistence), removing the `[RequiresUnreferencedCode]` value-read on the
  export path for covered views. Diagnostics `VISTA0001` (non-partial Style B
  view) and `VISTA0002` (Style B view without a public parameterless
  constructor); help docs under `docs/diagnostics/`.
- **Core** — `ViewAccessorRegistry` (static, thread-safe accessor store) and an
  AOT-clean `ExportColumns.Value(viewName, row, fieldName)` overload consumed by
  the CSV/XLSX export writers.
- **Core** — View authoring (`View`/`ViewBuilder`/`ViewTemplate`), `ViewMetadata`,
  the filter contract, and the `IViewExecutor`/`IViewScope` ports.
- **EntityFrameworkCore** — View execution over EF Core (List + Detail-by-key,
  paging, filter/sort/search, provider-aware) and DbContext-bound authoring.
- **AspNetCore** — generic endpoint mapping (`MapVistaViews`), RFC 7807 error
  mapping, and an optional fail-open `IViewAuthorizer` with a startup warning.
- **Examples** — a Northwind sample app exposing the read-only `vProductCategory`
  View over the real Microsoft Northwind SQLite database, with an end-to-end
  self-test (`dotnet run -- selftest`).
- Specs `01`–`05`, `10`, and `11` under `docs/spec/`.
- Project docs: `CONTRIBUTING`, `CODE_OF_CONDUCT`, `SECURITY`, `SUPPORT`,
  `NOTICES`, `COPYING`, and this changelog.

### Changed
- **Routing** (Decision Log D101/D103, model R): a view's full route is composed
  at registration (default root `/api/views`, or a `RouteGroup` prefix) and baked
  into `ViewMetadata.Route`; the AspNetCore layer is a dumb mapper that maps each
  view at its `ViewMetadata.Route`. View names are globally unique and one view
  maps to exactly one endpoint.
- **Authorization** (Decision Log D94): without an `IViewAuthorizer`, endpoints
  allow-all with a startup warning in Development but **fail-closed at startup**
  in non-Development environments unless `AllowAnonymousAccess()` is called.
- **Masking** (Decision Log D95): a `MaskField`'d field defaults to
  non-filterable and non-searchable unless explicitly opted back in.

### Fixed
- **AspNetCore** — `FilterNode` leaf values that are JSON integers now round-trip
  as `long` instead of being boxed as `double`, preserving int64 precision (values
  above 2^53). Surfaced by the polymorphic-round-trip property test added with the
  HTTP-surface phase.
- **AspNetCore** — write requests are no longer mis-classified as `400` when they
  should be authorized/denied: the write envelope is deserialized through the
  case-insensitive serialization seam so the `model`/`key` members bind correctly,
  restoring the `403`-before-`400` ordering for denied writes.

[Unreleased]: https://github.com/anwarminarso/a2n.Vista/commits
