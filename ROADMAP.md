# a2n.Vista — Project Brief

## Background

`a2n.DynData` is a .NET library that turns an Entity Framework Core `DbContext` into an automatic REST API (datatable, paging, filter, CRUD, export) complete with a JavaScript client for DataTables.js and jQuery QueryBuilder. It works, but a thorough review surfaced significant weaknesses:

- Insecure defaults for production (every `DbSet` is auto-exposed)
- Open mass assignment (deserialization straight into entities)
- A hard dependency on Newtonsoft.Json
- Reflection-heavy (not AOT-friendly)
- Three generic controllers with ~80% code duplication
- Mandatory inheritance of `DynDbContext` (invasive)
- The `QueryTemplate` feature (its most promising part) is only half-finished: a bloated API, fragile type discovery, and a CRUD redirect that is not wired into the controller
- Integrated only with jQuery DataTables + QueryBuilder

In the "auto CRUD/REST API from an ORM" category the competition is crowded (EasyData, Hasura, PostgREST, Supabase, Directus, and others). Competing head-to-head as a generic "auto CRUD generator" is not a healthy strategy.

## Decision

Build **`a2n.Vista`** as an **evolution** of DynData: keep the *view-first* ergonomics that are its strength, while redesigning the foundation (security, AOT, grid-agnostic) that forms its unique differentiation. Neither a patch over DynData, nor a discarding of its ergonomics.

## Core Pillars

### Pillar 1 — View as a First-Class Citizen
DynData's `QueryTemplate` concept is promoted to a core concept rather than an add-on. Vista is an **evolution** of DynData: its authoring ergonomics are preserved while its weaknesses are dropped.

- Developers define a **View** (a LINQ projection) as a declarative unit.
- A View is the single source for: metadata, endpoints, the filter contract, and UI binding.
- A raw `DbSet` is a special case of a View (a View without a projection).
- Every View is defined explicitly → secure-by-default (no auto-expose).
- Declarative field whitelisting → a built-in answer to mass assignment.

**Two authoring styles (both produce the same `ViewMetadata`):**

1. **Anonymous projection — read-only (the DynData style, preserved).** The developer writes a `select new { ... }` projection inline **without** declaring a DTO class. This is DynData's strength: view columns are easy to adjust and iterate on quickly. **Firm rule: an anonymous projection ⇒ no CRUD ⇒ a read-only View.** Without an explicit DTO there is no write contract, hence no mass-assignment surface.
2. **Typed DTO — read + CRUD.** For Views that need writes (create/update/delete), the developer declares `TQuery` (and `TCrud`) explicitly. CRUD is available **only** on this path, complete with a `MapWritable` whitelist. A strongly-typed `View<TQuery, TCrud>`, not `IQueryable<dynamic>`.

So convenience (anonymous, read-only) and write safety (typed DTO + whitelist) are two points on the same spectrum, chosen by the developer **per View**. CRUD never relies on `dynamic`/anonymous projections.

### Pillar 2 — Broad, Grid-Agnostic UI Integration
Separate the server contract from the client adapter.

**Server core**: a neutral query/response contract and a standard filter expression.

**Separate client adapters per UI ecosystem:**
- `a2n.Vista.Adapters.DataTablesNet` — jQuery DataTables + QueryBuilder
- `a2n.Vista.Adapters.AgGrid` — AG Grid
- `a2n.Vista.Adapters.MudBlazor` — MudDataGrid server-side
- `a2n.Vista.Adapters.Telerik` — Telerik UI / Kendo Grid
- `a2n.Vista.Adapters.Syncfusion` — Syncfusion Grid
- `a2n.Vista.Adapters.TanStackTable` — TanStack Table (React/Vue/Solid)
- `a2n.Vista.Adapters.PrimeNG` / `PrimeReact` / `PrimeVue`
- `a2n.Vista.Adapters.Quasar` — QTable (Vue)
- `a2n.Vista.Adapters.OData` — translate to `$filter` (supports many grids directly)
- `a2n.Vista.Adapters.GraphQL` — bonus

Philosophy: **the core does not care which grid is used; the adapter does the translation.**

### Pillar 3 — AOT-First, Not AOT-as-an-Afterthought
- Source generator for metadata (no runtime reflection)
- Source generator for endpoint registration
- A strongly-typed expression builder per View
- Target Native AOT compatibility with minimal `RequiresUnreferencedCode` annotations
- OpenAPI/Swagger docs generated at compile time

**Anonymous projection vs AOT trade-off (deliberate):** the **typed-DTO** View is the fully AOT-clean path — metadata, expression builder, and serialization (via `JsonSerializerContext` source-gen) are all compile-time. The **anonymous-projection** View (read-only) sacrifices some AOT cleanliness: anonymous types have no STJ source-gen path yet, so this path is marked `[RequiresUnreferencedCode]` and is aimed at non-AOT scenarios / fast iteration. Developers targeting full Native AOT use typed DTOs. This is a conscious choice: DynData's ergonomics remain available for those who need them, without compromising the AOT path for those targeting AOT production.

## Additional Requirements (Mandatory in the Concept)

- **Minimal API support**: `app.MapVistaView<MyView>()`, not just controllers
- **System.Text.Json native**, Newtonsoft optional in a separate package
- **Provider-agnostic filtering**: auto-detect ILike/Contains from the DB configuration, not a client flag
- **Centralized authorization** via a single `IViewAuthorizer` (in the style of DynData's `IDynDataAPIAuth`) — registered once (`UseAuthorizer<T>`), gating all Views and facets, plus a server-trusted row-scope hook (`ShapeQuery`). No authorizer → default allow + a startup warning.
- **Built-in hard limits**: max page size, max export rows
- **Row-level security hook** and declarative field masking
- **Bulk operations** via `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (EF 7+)
- **TypeScript client generator** from View metadata (strongly typed)
- **No mandatory base DbContext inheritance** (composition via DI, not inheritance)

## Differentiation vs Competitors

| Competitor | Their position | Weakness from Vista's viewpoint |
|------------|----------------|----------------------------------|
| EasyData | Auto-CRUD + commercial UI | Vendor-locked UI, weak on complex view features |
| Hasura/Supabase | GraphQL/REST from a DB | Not .NET native, needs external infra |
| PostgREST | REST from a Postgres schema | Postgres-only, views defined in SQL |
| OData (Microsoft) | Standard query language | Not an auto-API, not AOT-first |
| AutoAPI / Auto.Rest.API | Auto REST from `DbSet` | Weak maintenance, no view concept |

**Vista's position**: *"A .NET library for building back-office applications around complex LINQ-projection views, with grid-agnostic integration, secure-by-default behavior, and AOT-clean output."*

## NuGet Package Structure

```
a2n.Vista.Core                       ← engine: view, query, expression, metadata
a2n.Vista.AspNetCore                 ← endpoint mapping (MVC + Minimal API)
a2n.Vista.SourceGenerators           ← compile-time codegen, AOT
a2n.Vista.EntityFrameworkCore        ← EF Core integration
a2n.Vista.Newtonsoft                 ← optional, for legacy

a2n.Vista.Adapters.DataTablesNet
a2n.Vista.Adapters.AgGrid
a2n.Vista.Adapters.MudBlazor
a2n.Vista.Adapters.Telerik
a2n.Vista.Adapters.Syncfusion
a2n.Vista.Adapters.TanStackTable
a2n.Vista.Adapters.PrimeNG
a2n.Vista.Adapters.OData
a2n.Vista.Adapters.GraphQL

a2n.Vista.Client.TypeScript          ← TS codegen tool
```

**Dependency rules (D48):**

- `Core` — **EF- and HTTP-free**. Neutral contracts + the `IViewExecutor`/`IViewScope` ports. References no other package.
- `EntityFrameworkCore` → `Core`. Implements `IViewExecutor`, provider detection, CRUD/bulk, plus DbContext-bound authoring (`ViewTemplate<TDbContext>`).
- `AspNetCore` → `Core`. Endpoint mapping + `IViewAuthorizer` (HTTP-bound). Does **not** reference EF.
- `Adapters.*`, `Client.TypeScript` → `Core` only (neutral contracts, no EF/ASP.NET).
- `SourceGenerators` — Roslyn (netstandard2.0), no project references.
- `EntityFrameworkCore` & `AspNetCore` **do not reference each other**; they meet at `IViewExecutor` (Core) via DI in the composition root.

## Namespace & Naming Conventions

```csharp
namespace a2n.Vista;
namespace a2n.Vista.AspNetCore;
namespace a2n.Vista.Adapters.AgGrid;
```

Internal terminology:
- `View<T>` — the core unit (replaces `QueryTemplate`)
- `ViewBuilder` — the fluent view-configuration API
- `IViewRegistry` — the view registry
- `ViewMetadata` — the generated metadata
- `IViewAdapter<TRequest, TResponse>` — the UI adapter contract
- `MapView<TView>()` — the Minimal API extension

## Branding

- **Package ID**: `a2n.Vista.*` (consistent with the maintainer's ecosystem)
- **Brand name in marketing/docs**: `Vista`
- **Tagline**: *"Define a view, get an API. Type-safe, AOT-friendly, grid-agnostic projections for ASP.NET Core."*
- **Initial GitHub repo**: `anwarminarso/a2n.Vista`
- **Possible migration to a `vista-net` org** in the future if community momentum appears

## Relationship to a2n.DynData

`a2n.Vista` is an **evolution** (major-version successor) of `a2n.DynData`, not an unrelated library. The goal: DynData users feel "at home" — the centralized view authoring with anonymous projections is preserved — while the security and AOT weaknesses are closed. `a2n.DynData` is marked legacy/maintenance-only with a pointer to `a2n.Vista` and a migration guide.

README message:

> a2n.Vista is the evolution of a2n.DynData — same view-first ergonomics, now with type-safe CRUD, AOT support, and grid-agnostic adapters.

## Branch & Release Strategy

- DynData keeps getting bug-fix maintenance, but no new major features
- Vista is developed as a new repo, not a DynData branch
- Vista's release is planned in three stages:
  1. **v0.x — Foundation**: Core, AspNetCore, EF Core integration, a basic source generator, and one reference adapter (DataTablesNet)
  2. **v1.0 — Production-ready**: security hardening, hard limits, OpenAPI, the TS client generator, and two major adapters (AG Grid, MudBlazor)
  3. **v1.x — Ecosystem**: additional adapters (Telerik, Syncfusion, TanStack, PrimeNG, OData, GraphQL), bulk ops, audit log, soft delete, SignalR live updates

## Status & Next Steps

> For the detailed, authoritative snapshot see `docs/PROJECT-STATUS.md`; for the milestone tracker with
> progress bars and the dependency graph see `docs/MILESTONES.md`. Build is green on net8/9/10 with
> **516 tests/TFM (net8) / 517 (net9/net10)** + **112 generator tests** + **136 tests/TFM** for the
> TypeScript client generator (M17); the AOT probe is clean (zero IL2026/IL3050 on the full generated Style B
> HTTP round-trip), and the Northwind read + write + OpenAPI self-tests pass (write reports both
> `WriteMapper: GENERATED` and `ViewInvoker: GENERATED`), as does the Northwind sample showcase self-test
> (read + write + OpenAPI + a view-browser round-trip combining paging, global search, multi-sort, and an
> advanced filter).

**Done (v0.x foundation):**

1. Repo skeleton: solution layout, multi-targeted build (.NET 8/9/10), test framework (TUnit on Microsoft.Testing.Platform).
2. Pillar 1 (View) spec plus supporting specs: filter/query (02), source generator (03), adapter contract (04), ASP.NET Core mapping (05), operations & observability (10), versioning & deprecation (11).
3. **Pillar 1 — Core View engine (complete, read + write):**
   - **Core** — `View`/`ViewBuilder`/`ViewTemplate`, metadata, filter contract, the `IViewExecutor`/`IViewScope`/`IViewRegistry` ports, `FilterCompiler` (tri-whitelist + DoS guards), the write seam (`WriteMapper`/`IWriteFacetRegistry`).
   - **EntityFrameworkCore** — View execution (List + Detail, deterministic paging, filter/sort/search, composite keys, provider-aware) and the write facet (Create/Update/Delete with mass-assignment whitelist, optimistic concurrency, single `SaveChanges`); `IQueryDialect` port + Npgsql dialect; startup PK auto-derivation (D105) and provider guard.
   - **AspNetCore** — action-style endpoint mapping (`POST list/detail/export/create/update/delete` + `GET metadata`), RFC 7807 error mapping, secure-by-default one-door auth (fail-closed in non-Development).
4. **Pillar 2 — server-half query engine (complete & hardened)**; client half: two real grid adapters — the **DataTables.NET** reference adapter and the **AG Grid** adapter (M16, D133–D136 — server-side row model: block paging, `filterModel`/`sortModel`, quick filter, with an AG Grid + TypeScript Northwind sample) — plus the pluggable **export pipeline** (CSV/XLSX) and the **QueryBuilder metadata-schema** emitter.
5. **Pillar 3 — source generator (complete):** Phase 1 (shape-driven export accessors), Phase 2 (executable typed Style B via generated `ICompiledViewExecutionPlan` + masking runtime), the write-DSL phase (the generated write mapper), the HTTP-surface phase (a generated Core-only dispatch invoker + `ViewInvokerStore` and an AOT-clean serialization seam), the per-view `JsonTypeInfo` phase (a generated per-view `IJsonTypeInfoResolver` built via `JsonMetadataServices` + a Core-resident `GeneratedJsonContextStore` the seam auto-chains, making the developer-authored `App_Json_Context` **optional**), and the final **Style A coverage** phase (a fifth generator covering the nameable central-template subset — named-`TRow` export accessors + read-DTO `JsonTypeInfo`, and every writable view's `TCrud` `JsonTypeInfo`) have all landed — **every planned generator phase has shipped**. The **full** typed Style B `request → authorize → execute → serialize` path is AOT-clean, including serialization with no hand-authored context; anonymous Style A read serialization stays permanently `[RequiresUnreferencedCode]` by design (the deliberate AOT trade-off above).
6. **OpenAPI (M18):** an opt-in `a2n.Vista.OpenApi` package emits a deterministic OpenAPI v3.x document for every mapped View (served off-by-default at `GET /openapi/v1.json`, additive-only).
7. **TypeScript client generator (M17):** the standalone `a2n.Vista.Client.TypeScript` CLI generates a framework-agnostic, strongly-typed TS client from the emitted OpenAPI document — a pure downstream consumer with no Vista project reference (read facets by default, write facets opt-in, secure-by-default, deterministic output).
8. End-to-end Northwind example with passing read **and** write self-tests (the write self-test runs through the generated write mapper *and* the generated dispatch invoker) — now with its developer `NorthwindJsonContext` removed, exercising the generated per-view serialization, plus an OpenAPI self-test.
9. **CI + NuGet publish workflows (M19):** `.github/workflows/ci.yml` (build the solution + run the three TUnit suites across net8/9/10) and `publish.yml` (pack + push the 7 shipping packages to nuget.org via **NuGet Trusted Publishing / OIDC** — no long-lived API key) — with M19 the v0.x foundation is complete.
10. **Northwind sample showcase (D137–D140):** the `a2n.Vista.Examples.AgGridNorthwind` host became a three-page showcase (Simple Wiring / View Browser / Custom Renderer) reaching DynData "Table Browser" parity on the read surface — a `GET /api/showcase/views` catalog over `IViewRegistry`, dynamic columns from metadata, server-side paging/global-search/multi-sort, and a jQuery-QueryBuilder advanced filter — plus the `vOrder` view. Additive at the sample layer only.

**Next:**

1. Observability (D100 — OpenTelemetry) and versioning/deprecation (D99).
2. The remaining reference adapters (MudBlazor next; then Telerik, Syncfusion, TanStack, PrimeNG, OData, GraphQL) — seven are still empty scaffolds (DataTables.NET and AG Grid are done).
3. Bulk write operations (v1.x; an array body is rejected with 400 today).
4. Final availability check: NuGet `a2n.Vista.*`, GitHub username/org, domain (optional).
