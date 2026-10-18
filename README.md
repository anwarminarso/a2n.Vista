<p align="center">
  <img src="./assets/a2n-vista-wordmark.png" alt="a2n.Vista" width="520" />
</p>

# a2n.Vista

> *Define a view, get an API. Type-safe, AOT-friendly, grid-agnostic projections for ASP.NET Core.*

`a2n.Vista` is a .NET library for building back-office applications around **Views** — complex LINQ projections — with grid-agnostic UI integration, secure-by-default behavior, and AOT-clean output.

Vista is the design successor to [`a2n.DynData`](https://github.com/anwarminarso/a2n.DynData) — not a mechanical rewrite, but a redesign focused on its unique differentiators. See [`ROADMAP.md`](./ROADMAP.md) for the full context.

## Status

**0.0.5** (v0.x Foundation). The solution targets **net8.0**, **net9.0**, and **net10.0**. The Northwind read **and** write self-tests pass. The shipping packages are working end to end:

- **Core** — `View`/`ViewBuilder`/`ViewTemplate`, metadata, the filter contract, the `IViewExecutor`/`IViewScope` ports, and the write seam (`WriteMapper`/`IWriteFacetRegistry`).
- **EntityFrameworkCore** — executes Views over EF Core: List + Detail-by-key, deterministic paging, filter/sort/search, composite keys, provider-aware (`IQueryDialect` + optional Npgsql), and the write facet (Create/Update/Delete with a `MapWritable` whitelist and a single `SaveChanges`).
- **AspNetCore** — action-style endpoint mapping (`POST list/detail/export/create/update/delete` + `GET metadata`) and secure-by-default one-door authorization (fail-closed at startup in non-Development).
- **SourceGenerators (Pillar 3, complete)** — every planned phase landed: shape-driven export accessors, executable typed Style B (`ICompiledViewExecutionPlan` + masking runtime + single-source PK auto-derivation), the generated write mapper, the HTTP-surface phase (a generated Core-only dispatch invoker + an AOT-clean serialization seam), the per-view `JsonTypeInfo` phase (a generated per-view `IJsonTypeInfoResolver` that makes the developer `App_Json_Context` **optional**), and the final Style A coverage phase (the nameable central-template subset). The **full typed Style B `request → authorize → execute → serialize` path is AOT-clean** (IL2026/IL3050-free), including serialization with no hand-authored context; reflection remains only as a fallback for anonymous Style A read serialization (permanently `[RequiresUnreferencedCode]` by design).
- **OpenApi** — an opt-in `a2n.Vista.OpenApi` package emits a deterministic **OpenAPI v3.x** document for every mapped View (served off-by-default at `GET /openapi/v1.json`, additive-only).
- **Client.TypeScript** — a standalone CLI that generates a framework-agnostic, strongly-typed **TypeScript client** from the emitted OpenAPI document: per-view DTO types, the fixed Vista envelopes, the `FilterNode` union, and a per-view typed client over an injectable HTTP transport. A pure downstream consumer (no Vista project reference); read facets by default, write facets opt-in, secure-by-default, deterministic output.
- **Adapters (Pillar 2, client half)** — grid adapters translate a grid's native request/response shape into the neutral Vista contract. Three are real: **DataTables.NET** (jQuery DataTables + QueryBuilder), **AG Grid** (`a2n.Vista.Adapters.AgGrid` — server-side row model: block paging, `filterModel`/`sortModel`, quick filter), and **TanStack Table** (`a2n.Vista.Adapters.TanStackTable` — manual pagination, sorting, column filters, global filter, and column visibility), each Core-only and exposed through the same host glue at `POST {route}/{suffix}`.
- **CI / publish** — GitHub Actions workflows build the solution and run the test suites across net8/9/10, and pack + push the shipping packages to nuget.org via NuGet Trusted Publishing (OIDC, no stored API key).

**Write contract (0.0.5).** A token-bearing write repeats the quoted strong `ETag` as `If-Match`.

| Status | Code | When |
| --- | --- | --- |
| 428 | `write-precondition-required` | `If-Match` is missing |
| 412 | `write-concurrency-conflict` | the `ETag` is stale |
| 422 | `write-constraint-violation` | a constraint violation |
| 500 | `write-persistence-failed` | any other save failure |
| 503 | `write-busy` | a probe-recognized lock; the response includes `Retry-After: 2` |
| 204 | — | a successful delete, or an update that leaves the row outside read scope (no body and no `ETag`) |

Do not call `res.json()` on a 204, and that update checks the detail read path before `SaveChanges` (D159) — [0.0.5 changelog](./CHANGELOG.md#005---2026-10-18).

Runnable examples: [`src/Examples/Northwind`](./src/Examples/Northwind) — read-only and writable Views over the real Northwind database, with an end-to-end self-test (`dotnet run -- selftest`) that exercises the generated dispatch and serialization; and [`src/Examples/a2n.Vista.Examples.AgGridNorthwind`](./src/Examples/a2n.Vista.Examples.AgGridNorthwind) — a four-page **Northwind sample showcase** (Simple Wiring, View Browser, Custom Renderer, and TanStack Table at [`/tanstack.html`](./src/Examples/a2n.Vista.Examples.AgGridNorthwind/wwwroot/tanstack.html)) that drives DataTables.NET + jQuery-QueryBuilder, AG Grid, and TanStack Table over several Northwind Views, populated from a `GET /api/showcase/views` catalog (secure-by-default: only registered Views appear).

Not started / skeleton only: the remaining UI adapters (Pillar 2, client half — DataTables.NET, AG Grid, and TanStack Table are real; MudBlazor, Telerik, Syncfusion, PrimeNG, OData, GraphQL are scaffolds), observability (OpenTelemetry), and versioning/deprecation.

Specs under refinement live in [`docs/spec/`](./docs/spec/).

## Upgrading to 0.0.5

- [#8](https://github.com/anwarminarso/a2n.Vista/pull/8) retires `write-conflict`. Clients that matched it as a constraint violation must match `write-constraint-violation` (HTTP 422). `VistaWriteConflictException` is removed and is not an obsolete alias — [0.0.5 changelog](./CHANGELOG.md#005---2026-10-18).
- [#21](https://github.com/anwarminarso/a2n.Vista/pull/21) publishes `a2n.Vista.Adapters.TanStackTable` with this release (it depends on `a2n.Vista.Core` at the same version). `columnVisibility` / `SelectFields` only hides a column in the table UI; hidden fields are still sent to the client — [0.0.5 changelog](./CHANGELOG.md#005---2026-10-18).
- [#23](https://github.com/anwarminarso/a2n.Vista/pull/23) and [#24](https://github.com/anwarminarso/a2n.Vista/pull/24) are the write contract above: quoted `ETag` as `If-Match`, 428 / 412 / 422 / 500 / 503, and 204 for delete and for an update that leaves read scope — [0.0.5 changelog](./CHANGELOG.md#005---2026-10-18).
- `WriteErrorCode` integers shifted. `WriteConflict` (7) was removed. `ConstraintViolation` is 7, `PersistenceFailed` is 8, `BulkNotEnabled` moved from 8 to 9, `NoWritePlan` moved from 9 to 10, and `StoreBusy` is 11. Code that persists or compares the raw integers must update — [0.0.5 changelog](./CHANGELOG.md#005---2026-10-18).

## Three Pillars

1. **View as a First-Class Citizen** — declarative LINQ projections as the core unit, secure-by-default, strongly typed.
2. **Grid-Agnostic UI Integration** — a neutral core with separate adapters per grid ecosystem (DataTables, AG Grid, MudBlazor, Telerik, Syncfusion, TanStack, PrimeNG, OData, GraphQL).
3. **AOT-First** — source generators for metadata and endpoints, with no runtime reflection on the hot path.

## Solution Layout

```
src/
  a2n.Vista.Core                              ← engine: view, query, expression, metadata
  a2n.Vista.AspNetCore                        ← endpoint mapping
  a2n.Vista.EntityFrameworkCore               ← EF Core integration
  a2n.Vista.OpenApi                           ← OpenAPI v3 document
  a2n.Vista.SourceGenerators                  ← compile-time codegen, AOT
  a2n.Vista.Newtonsoft                        ← optional, for legacy
  a2n.Vista.Client.TypeScript                 ← TS codegen tool
  Adapters/
    a2n.Vista.Adapters.*                      ← UI adapters per ecosystem
    a2n.Vista.EntityFrameworkCore.Npgsql      ← PostgreSQL ILIKE dialect
  Examples/
    Northwind                                 ← end-to-end example (EF + AspNetCore)
  Tests/
    a2n.Vista.Tests                           ← unit & integration tests
```

## Documentation

- [ROADMAP](./ROADMAP.md) — vision, positioning vs competitors, release strategy
- [CONTRIBUTING](./CONTRIBUTING.md) — how to build, test, and submit changes
- [CHANGELOG](./CHANGELOG.md) — notable changes per release
- [SECURITY](./SECURITY.md) — how to report a vulnerability

**Specs:**

- [01 — View](./docs/spec/01-view.md) — View concept and API surface (Pillar 1)
- [02 — Filter & Query Engine](./docs/spec/02-filter-and-query.md) — filter contract, paging/sort/search (Pillar 2, server half)
- [03 — Source Generator](./docs/spec/03-source-generator.md) — compile-time codegen (Pillar 3)
- [04 — Adapter Contract](./docs/spec/04-adapter-contract.md) — UI adapter contract (Pillar 2, client half)
- [05 — ASP.NET Core Mapping](./docs/spec/05-aspnetcore-mapping.md) — HTTP composition: read, write, auth, error
- [10 — Operations & Observability](./docs/spec/10-operations-and-observability.md) — cross-cutting
- [11 — Versioning & Deprecation](./docs/spec/11-versioning-and-deprecation.md) — cross-cutting

## Install

Each shipping package is **0.0.5** and targets **net8.0**, **net9.0**, and **net10.0**. `a2n.Vista.Client.TypeScript` is a `dotnet` tool (`vista-ts`), not a library reference.

| Package | Version | Install |
| --- | --- | --- |
| `a2n.Vista.Core` | 0.0.5 | `dotnet add package a2n.Vista.Core --version 0.0.5` |
| `a2n.Vista.EntityFrameworkCore` | 0.0.5 | `dotnet add package a2n.Vista.EntityFrameworkCore --version 0.0.5` |
| `a2n.Vista.AspNetCore` | 0.0.5 | `dotnet add package a2n.Vista.AspNetCore --version 0.0.5` |
| `a2n.Vista.OpenApi` | 0.0.5 | `dotnet add package a2n.Vista.OpenApi --version 0.0.5` |
| `a2n.Vista.EntityFrameworkCore.Npgsql` | 0.0.5 | `dotnet add package a2n.Vista.EntityFrameworkCore.Npgsql --version 0.0.5` |
| `a2n.Vista.Adapters.DataTablesNet` | 0.0.5 | `dotnet add package a2n.Vista.Adapters.DataTablesNet --version 0.0.5` |
| `a2n.Vista.Adapters.AgGrid` | 0.0.5 | `dotnet add package a2n.Vista.Adapters.AgGrid --version 0.0.5` |
| `a2n.Vista.Adapters.TanStackTable` | 0.0.5 | `dotnet add package a2n.Vista.Adapters.TanStackTable --version 0.0.5` |
| `a2n.Vista.Client.TypeScript` | 0.0.5 | `dotnet tool install --global a2n.Vista.Client.TypeScript --version 0.0.5` |

Packages are published to nuget.org from tag `v0.0.5`.

## Building

Requires the .NET SDK (the solution multi-targets .NET 8, 9, and 10).

```sh
dotnet build src/a2n.Vista.slnx
dotnet test src/a2n.Vista.slnx
```

See [CONTRIBUTING](./CONTRIBUTING.md) for the full workflow.

## License

Licensed under the **GNU Lesser General Public License v3.0** (LGPL-3.0-or-later).
See [LICENSE](./LICENSE), [COPYING](./COPYING), and [NOTICES](./NOTICES.md).
