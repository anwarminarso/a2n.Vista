# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is `0.x`, anything may change between releases.

## [Unreleased]

### Added
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
