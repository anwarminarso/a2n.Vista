# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is `0.x`, anything may change between releases.

## [Unreleased]

### Fixed
- **The AG Grid adapter no longer silently drops an unknown `sortModel[].colId`**
  ([#2](https://github.com/anwarminarso/a2n.Vista/issues/2), Decision Log D150). The sort channel
  matched each `colId` against the view projection and skipped what it could not resolve, while a
  `filterModel` key with the *same* spelling mistake was rejected with `400`
  `filter-unknown-field`. Since matching is ordinal, a merely mis-cased name counts as unknown —
  and `rowData` is serialized camelCase while field names are PascalCase, so mis-casing is the
  natural mistake to make. One misspelling therefore produced a precise error on one channel and a
  healthy-looking `200` with an untouched row order on the other: a sort that looked applied and
  was not. Every `sortModel` entry is now carried through verbatim and in position, and the engine
  refuses an unknown or non-sortable field with the same `400` the filter channel already
  produced — the adapter builds, the engine enforces (Spec 04 §6 invariant 2, D67).

### Breaking (behaviour, not API)
- **A sort on a column the server does not project is now an error, not a no-op** (D150, the fix
  above). Sorting by a client-side-only column previously returned `200` with the rows in the
  provider's incidental order; it now returns `400` naming the field. **Action:** give any column
  with no server field behind it `sortable: false` in its `colDef` (an actions or selection column
  should carry that already), and make sure `colId` is the view's field name — the PascalCase C#
  property — not the camelCase key you read in `rowData`. `GET {route}/metadata` publishes the
  names. The `filterModel` channel is unchanged; the ordinal whitelist is unchanged. The
  DataTables adapter is unchanged: its request declares its own columns
  (`columns[i][data]`/`[orderable]`), so it can still tell a UI column from a typo and only the
  self-declared UI column is skipped.

## [0.0.1] - 2026-07-31

Remediation of the 2026-07-31 full code audit (`docs/audit/2026-07-31-full-code-audit.md`)
across six tranches, plus the packaging work needed to publish honestly. **Every security
finding (`SEC-01`–`SEC-06`) and every correctness finding (`BUG-01`–`BUG-13`) is fixed**, each
with a regression test; the report holds the per-finding status table and the still-open items.

**Supported grid adapters in this release: DataTables.NET and AG Grid.** The MudBlazor, OData,
GraphQL, PrimeNG, Syncfusion, TanStack Table and Telerik adapter projects — and
`a2n.Vista.Newtonsoft` — are reserved scaffolds with no implementation and are **not published**.
They are excluded both by the release workflow and by `IsPackable=false` in their own project
files, so installing a Vista adapter package can never give you an empty assembly.

Read the **breaking behaviour** section below before upgrading from `0.0.1-beta.2`: three
defaults changed to close real defects, and one of them fails startup by design.

### Breaking (behaviour, not API)
- **A masked field is no longer sortable by default** (`SEC-04`, Decision Log D143). Ordering by
  a masked column and paging through the result leaks the relative order of the hidden values —
  for a numeric or date column that is close to a binary search over them. This is the probing
  vector D95 already closes for filter and search. **Action:** add `Sortable()` on the field to
  opt back in; that is now a reviewed choice rather than a silent default. The generated
  execution plan no longer emits a member accessor for a masked, non-sortable field.
- **A view whose declared concurrency token is not backed by the EF model now fails startup**
  (`BUG-04`, D146). Without `IsRowVersion()` / `IsConcurrencyToken()` in the model, EF emitted no
  `UPDATE ... WHERE token = @original` predicate, so Vista's read-then-compare was non-atomic and
  two concurrent writes could both succeed — a lost update, silently. The startup validator names
  the offending view and property. **Action:** configure the property as a concurrency token in
  `OnModelCreating`. This surfaced five test fixtures with exactly that misconfiguration; both
  shipped samples were already correct.
- **Reads no longer return change-tracked entities** (`BUG-07`, D147). Every execution plan applies
  `AsNoTracking()` to its source query. A DTO projection was never tracked, so most views are
  unaffected; an *entity-bearing* projection (an identity projection, or a Style A view registered
  as `(db, sp) => db.Set<Entity>()`) previously handed back rows attached to the request-scoped
  `DbContext` the write path shares. Because the masking runtime writes the masked value into the
  materialized row, a later `SaveChanges` on that context could **persist the mask over real
  data**. **Action:** if you relied on a read endpoint returning tracked entities for a subsequent
  save, re-fetch through the write path instead.
- **The success `ETag` is the post-write token, and a delete emits none** (`BUG-05`, D146). The
  update response previously echoed the request's own `If-Match`, which for a store-generated
  `rowversion` was stale on arrival and guaranteed the client's next update a 409. A delete no
  longer emits an `ETag` for a row that no longer exists.
- **Write and adapter endpoints authorize before they bind** (`BUG-03`, D145). An unauthorized
  caller now receives `403` where it previously received `428 write-precondition-required` or a
  `400` bind error — responses that disclosed the view exists, is writable, and declares a
  concurrency token, and that let an unauthenticated client force JSON parsing work.
- **Paging carries an absolute row offset** (`BUG-02`, D144). `ViewQueryRequest` gained an optional
  `Offset`; when set it wins over `Page`. Both grid adapters now pass `start` / `startRow` verbatim
  instead of dividing by the client's page size. Before, an unaligned offset snapped to a page
  boundary and the engine's page-size clamp shifted the window — a grid asking for rows 200–399
  with a 100-row cap silently received rows 100–199. Clamping is now purely a size concern: the
  window start never moves.
- **DataTables honours the flags it binds** (`DEAD-05`, D144). A column marked `searchable:false`
  no longer receives a `Contains` leaf, `orderable:false` is no longer sorted, and
  `search[regex]=true` is rejected as a bind error instead of being executed as a literal
  `Contains`. A negative `start` is a bind error too, matching the AG Grid range check.

### Added
- **Display-format metadata** (`DEAD-02`, D149). `IFieldBuilder.Format("N2")` has been on the
  authored surface since the beginning — and is the successor of DynData's `DataFormatString` —
  but the captured value was read by nothing, so the call was **silent data loss**. It now reaches
  `FieldMetadata.Format`, the `GET {route}/metadata` response, and the emitted OpenAPI schema.
  The contract is deliberately narrow: **the server publishes the hint, the client applies it.**
  Vista never interprets it, so filtering, sorting, and export keep operating on raw values — a
  presentation hint cannot change what a query matches or what an export contains. The response
  member is omitted when unset, so a view that sets no format has a byte-identical `/metadata`
  payload.
- **Packaging: license, symbols, and source stepping.** Every published package now declares
  `LGPL-3.0-or-later` as an SPDX expression, ships a `.snupkg` symbol package, and enables
  SourceLink (`PublishRepositoryUrl` + `EmbedUntrackedSources`), with `ContinuousIntegrationBuild`
  on in CI for reproducible paths. Before this, packages carried **no license metadata at all** and
  `dotnet pack` omitted it *silently* — a real problem for a copyleft project, since a consumer
  cannot honour terms the package never declares. The release workflow now fails if any package
  loses its license expression or its symbol package.

### Changed
- **`RegisterAssembly` registers on the same terms as `Register<TView>()`** (`DEAD-06`). It used to
  register **metadata only**, so a scanned view became route-bearing and discoverable while staying
  permanently non-executable — no generated execution plan adopted, no mask specs, no write facet —
  and the executor threw "no generated execution plan" at request time. Both entry points now share
  one registration body. It also had no test coverage at all; it now has a test driven by a
  dedicated deterministic scan-target assembly.
- **`ViewMetadata` equality is content-based with a stable hash** (`BUG-10`, D148). The synthesized
  record equality compared every instance field, including a per-instance lock object, so two
  identical snapshots were **never** equal and the hash code was an identity hash unstable across
  runs; `Fields` was also compared by list reference. Equality is now hand-written over the
  declarative content with element-wise `Fields`, and the startup-completed `KeyFields` is excluded
  from both so neither can change during an instance's lifetime.
- **The reflection mask no longer refuses get-only rows** (`BUG-07`, D147). The fallback advertised
  as the Style A path could not mask an **anonymous** row at all — the one row shape Style A is
  built around — because it required a setter. It now rebuilds the row through a constructor
  covering every readable property, leaving the original untouched.
- Export responses stream the already-materialized buffer instead of copying it once more
  (`PERF-01`, partial — true streaming to the response body remains).

### Performance
- **The XLSX worksheet streams instead of being buffered whole** (`PERF-03`). It used to be
  accumulated into one `StringBuilder`, returned as a single string, then converted with
  `Encoding.UTF8.GetBytes` — two large-object-heap buffers holding the entire document, the
  intermediate one UTF-16 at roughly twice the byte size. At the default 100,000-row export cap
  that was the dominant allocation of the request. Peak memory is now one row plus the archive's
  compression buffer, whatever the row count. Byte output is unchanged.
- **The export reflection fallback no longer looks up a member per cell** (`PERF-02`). For a
  100,000-row × 10-column Style A export that was a million uncached `GetProperty` calls per
  request; the resolved member is now memoized per row type and field name.
- **View authoring runs `Configure` once per view** (`PERF-04`). Metadata, mask specs, the write
  facet, and row filters each used to build their own builder and re-run `Configure` — four or more
  full authoring passes — and the `ViewMetadata` published to the registry was a different instance
  from the one `Name` read. All four now come from one cached authoring result.
- **The filter field lookup is built once per view** (`PERF-05`). A single List request compiles up
  to three filter channels and a grid adapter binds a fourth, each rebuilding an identical
  dictionary over data that cannot change after registration. One shared, frozen lookup now serves
  all of them.
- **Metadata caching stopped paying for itself on every request** (`PERF-07`). With
  `EnableMetadataCaching()` on, the endpoint re-projected, re-serialized, and re-hashed (SHA-256)
  the whole payload per request — including on the `304` path. The payload and its `ETag` are now
  computed once per view, so a `304` costs one string comparison.

### Security
- **Row-level security no longer drops silently on a central-template (Style A) view**
  (`SEC-01`, Decision Log D141). A request whose authorizer pushed row filters into the
  scope is now **refused** by the combined-delegate execution plan instead of being served
  unscoped, closing a cross-tenant leak. `IViewScope` gained `RowFilterCount` so a
  type-erased plan can detect a populated scope without knowing `TSource`.
- **The OpenAPI document endpoint is authorized by default** (`SEC-02`, D142).
  `MapVistaOpenApi()` attaches `RequireAuthorization()` unless the host opted into
  anonymous access via `AllowAnonymousAccess()` (D94) or set the new
  `VistaOpenApiOptions.RequireAuthorization = false`. An endpoint carrying no
  authorization metadata is anonymous even behind `UseAuthentication`/`UseAuthorization`,
  and the document publishes every view's route, operation set, writability, and schemas.
- **Hidden fields are no longer published in the emitted document** (`SEC-03`). The row
  schema is filtered against the view's field flags, so a `Hidden()` field is absent from
  `components.schemas` exactly as it is from `GET {route}/metadata`; a maskable field stays
  described but is annotated as substitutable.
- **`.Key(nameof(Row.Id))` is guarded like `.Key("Id")`** (`SEC-05`). The write-DSL
  analyzer resolves key names as compile-time constants through the semantic model, so
  `nameof(...)`, `const` fields, and constant concatenation all raise `VISTA0032` when a
  mapping targets a key. Previously only a string literal matched, and the safer spelling
  let the generated mapper mass-assign the primary key.
- **Path-traversal containment in the TypeScript client generator** (`SEC-06`). A view name
  derived from the (external) OpenAPI document must match `[A-Za-z_][A-Za-z0-9_]*` and is
  otherwise a typed `GenerationError`; independently, the write stage refuses any path that
  resolves outside `--out`. This also removes an unhandled exception on a document with no
  usable `operationId`.
- **CSV export defuses formula injection** (`BUG-11`). A cell starting with `=`, `+`, `-`,
  `@`, tab or CR is prefixed with an apostrophe so spreadsheets render it as text. The XLSX
  writer additionally strips XML-illegal control characters (well-formed surrogate pairs are
  preserved), which previously made Excel reject the whole workbook.
- **Write bind errors are leak-free** (`BUG-06`). A malformed write body no longer echoes the
  `System.Text.Json` message (which embeds internal CLR type names and the model's member
  path); the client gets Vista-authored text plus the stable machine-readable code, and the
  cause is retained as `InnerException` for server-side logging.

### Fixed
- A typed filter value on a `Guid`/`DateTimeOffset`/`DateOnly`/`TimeOnly` field now returns
  the documented **400** instead of a 500 (`BUG-01`).
- A writable view that declares its key with the view-level `Key(...)` override (the
  documented path for join/union views) no longer fails at startup (`BUG-09`).
- Two row types sharing a simple name in different namespaces now get **distinct** OpenAPI
  component schemas instead of the second silently documenting the first one's shape
  (`BUG-08`).
- A negated empty DataTables QueryBuilder group (`{"not":true,"rules":[]}`) keeps its
  negation instead of dropping the filter and returning every row (`BUG-12`).
- On net10, the OpenAPI transformer maps `additionalProperties` again, so the same
  application emits the same document on every target framework (`BUG-13`).
- `HardLimits.AbsoluteMaxExportRows` is now enforced: `MaxExportRows` clamps on every
  construction path, including `with` (`DEAD-04`).
- Entity-bearing reads no longer let a mask reach the database (`BUG-07`, D147) — see
  **Breaking** above; the persistence path is the reason this is listed as a defect and not
  merely a performance change.
- The generators write string literals through one shared writer (`DEAD-09`, partial). Two
  accessor-map emitters had drifted — one escaped its keys, the other concatenated them raw.
  Generated output is byte-identical (a CLR member name cannot contain a quote), so this closed a
  latent inconsistency rather than a live defect; the wider cross-generator deduplication is
  tracked, not done.
- `dotnet pack` on the solution succeeds again. `a2n.Vista.SourceGenerators` is bundled into
  `a2n.Vista.Core` under `analyzers/dotnet/cs` and sets `IncludeBuildOutput=false`, so packing it
  as a project of its own failed with `NU5017` and took the whole solution's pack down. It is now
  declared `IsPackable=false`, which states the intent rather than half-declaring a package that
  cannot be built.

### Known gaps (deliberate, tracked)
Recorded here because `0.x` means "anything may change" — not "everything is finished".
`docs/PROJECT-STATUS.md` §7.1 holds the detail and the reasoning.
- **The OpenAPI emitter's adapter-documentation extension point is not implemented** (requirement
  12.2 of the `openapi-emitter` spec). Adapter endpoints are correctly absent from the document
  (requirement 12.1, tested), but the promised contribution point does not exist yet.
- **Three public members exist without behaviour**, pending a scope decision plus a spec
  reconciliation: `IViewRegistry.Register<TView>()` always throws (superseded in substance by the
  D101/D103 route model), `CrudOn<TEntity>(projectionForRead)` discards its parameter, and the
  TypeScript client's `--base-url` is parsed and ignored.
- **Export still materializes every row before writing** (`PERF-01`), bounded by
  `MaxExportRows` (default 100,000; absolute cap 1,000,000). The writers no longer buffer the
  document, but a streamed row source remains future work.
- **Every AG Grid block fetch pays a discarded unfiltered `COUNT`** (`PERF-08`); removing it needs a
  `ViewQueryRequest` contract decision.
- **Observability (D100) and wire versioning (D99)** are designed, not built.

## [0.0.1-beta.2] - 2026-07-15

First public pre-release. The Foundation (`v0.x`) surface is working end to end on
.NET 8/9/10: Core view authoring, EF Core execution with the write facet, ASP.NET
Core endpoint mapping, the complete source generator (Pillar 3), the opt-in OpenAPI
emitter, the standalone TypeScript client generator, and the DataTables.NET + AG Grid
adapters. Because this is `0.x`, anything may still change between releases.

### Added
- **Packaging (Pillar 3 delivery)** — the `a2n.Vista.SourceGenerators` analyzer is now
  **bundled into the `a2n.Vista.Core` package** (packed under `analyzers/dotnet/cs`)
  rather than shipped standalone. Consumers of the Core package get the AOT-clean
  metadata/execution/serialization codegen transitively, with no extra package
  reference and no manual `OutputItemType="Analyzer"` wiring. Settles the previously
  deferred source-generator packaging model.
- **Packaging (presentation)** — every shipping package now carries the a2n brand
  icon and a per-package `README.md` rendered on its nuget.org page (with an absolute
  logo URL so it renders off-repo).
- **Client.TypeScript ships as a `dotnet tool`** — `PackAsTool` with the command
  `vista-ts`. Install with `dotnet tool install --global a2n.Vista.Client.TypeScript`
  and invoke as `vista-ts --source <doc> --out <dir>`.
- **Examples** — a **Northwind sample showcase** (Decision Log D137–D140):
  the `a2n.Vista.Examples.AgGridNorthwind` host is now a three-page showcase
  behind a shared nav, reaching feature parity with the legacy DynData
  "Table Browser" on the read surface, purely additive at the sample layer (no
  Core/EF/AspNetCore/adapter contract, route, envelope, or error change).
  **D137** — the single `a2n.Vista.Examples.AgGridNorthwind` host serves all
  three pages and registers `DataTablesAdapter` + `QueryBuilderSchemaAdapter` +
  `AgGridAdapter` + the OpenAPI emitter, keeping `AllowAnonymousAccess()` (D94);
  the standalone `a2n.Vista.Examples.Northwind` host stays a separate
  single-view sample. **D138** — an additive read-only catalog endpoint
  `GET /api/showcase/views` (a pure `ShowcaseCatalog.Project` over
  `IViewRegistry`) supplies the browsable-view list, secure-by-default (only
  registered views), inside the host auth pipeline. **D139** — the pages are
  static HTML + TypeScript compiled by `tsc` (no bundler), with a `tsc --noEmit`
  typecheck gate and fast-check property tests for the pure transforms
  (`columns.ts`, `search.ts`). **D140** — a third read-only view, `vOrder`, is
  registered so the set (`vProductCategory`/`vOrderDetail`/`vOrder`) spans
  string/numeric/date/foreign-key/composite-key fields. The three pages: a
  **Simple Wiring** AG Grid page (infinite row model → `POST {route}/aggrid`,
  `?q=` quick filter), a **View Browser** DataTables.NET + jQuery-QueryBuilder
  page (view selection, dynamic columns from `GET {route}/metadata`, server-side
  paging + min-length global search + single/multi sort + a
  `GET {route}/querybuilder`-driven advanced filter posted through
  `POST {route}/datatable`), and a **Custom Renderer** AG Grid page (consumer-owned
  community `cellRenderer`s, presentation-only). The host self-test gained a
  view-browser round-trip that exercises all channels in one request.
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
  the eight implemented libraries (`Core`, `EntityFrameworkCore`, `AspNetCore`,
  `OpenApi`, `EntityFrameworkCore.Npgsql`, `Adapters.DataTablesNet`,
  `Adapters.AgGrid`, `Client.TypeScript`); the empty scaffolds and
  `a2n.Vista.SourceGenerators` (packaging model unsettled) are intentionally
  excluded. Additive-only — no source, wire, or package-content change.
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

[Unreleased]: https://github.com/anwarminarso/a2n.Vista/compare/v0.0.1-beta.2...HEAD
[0.0.1-beta.2]: https://github.com/anwarminarso/a2n.Vista/releases/tag/v0.0.1-beta.2
