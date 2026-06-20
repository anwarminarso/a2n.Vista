# a2n.Vista

> *Define a view, get an API. Type-safe, AOT-friendly, grid-agnostic projections for ASP.NET Core.*

`a2n.Vista` is a .NET library for building back-office applications around **Views** — complex LINQ projections — with grid-agnostic UI integration, secure-by-default behavior, and AOT-clean output.

Vista is the design successor to [`a2n.DynData`](https://github.com/anwarminarso/a2n.DynData) — not a mechanical rewrite, but a redesign focused on its unique differentiators. See [`ROADMAP.md`](./ROADMAP.md) for the full context.

## Status

**Pre-alpha — v0.x (Foundation), in progress.** The three core packages are working:

- **Core** — `View`/`ViewBuilder`/`ViewTemplate`, metadata, filter contract, and the `IViewExecutor`/`IViewScope` ports.
- **EntityFrameworkCore** — executes Views over EF Core: List + Detail-by-key, paging, filter/sort/search, provider-aware.
- **AspNetCore** — generic endpoint mapping (`MapVistaViews`), RFC 7807 error mapping, optional fail-open authorizer with a startup warning.

Runnable example: [`src/Examples/Northwind`](./src/Examples/Northwind) — the read-only `vProductCategory` View over the real Northwind database, with an end-to-end self-test (`dotnet run -- selftest`).

Not started / skeleton only: the source generator (Pillar 3), the UI adapters (Pillar 2, client half), and the TypeScript client generator.

Specs under refinement live in [`docs/spec/`](./docs/spec/).

## Three Pillars

1. **View as a First-Class Citizen** — declarative LINQ projections as the core unit, secure-by-default, strongly typed.
2. **Grid-Agnostic UI Integration** — a neutral core with separate adapters per grid ecosystem (DataTables, AG Grid, MudBlazor, Telerik, Syncfusion, TanStack, PrimeNG, OData, GraphQL).
3. **AOT-First** — source generators for metadata and endpoints, with no runtime reflection on the hot path.

## Solution Layout

```
src/
  a2n.Vista.Core                 ← engine: view, query, expression, metadata
  a2n.Vista.AspNetCore           ← endpoint mapping (MVC + Minimal API)
  a2n.Vista.EntityFrameworkCore  ← EF Core integration
  a2n.Vista.SourceGenerators     ← compile-time codegen, AOT
  a2n.Vista.Newtonsoft           ← optional, for legacy
  Adapters/
    a2n.Vista.Adapters.*         ← UI adapters per ecosystem
  a2n.Vista.Client.TypeScript    ← TS codegen tool
  Examples/
    Northwind                    ← end-to-end example (EF + AspNetCore)
  Tests/
    a2n.Vista.Tests              ← unit & integration tests
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
