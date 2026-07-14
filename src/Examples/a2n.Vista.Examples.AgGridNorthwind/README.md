# Vista Northwind sample showcase

A runnable, multi-page demo that composes the landed Pillar-2 client adapters into one showcase. A single
ASP.NET Core host (targeting **net8.0**) serves three static pages behind a shared navigation, each
driving read-only Northwind views through already-shipped Vista adapter endpoints — nothing here changes a
Core / EF / AspNetCore / adapter contract, route, envelope, or error shape.

The host registers three read-only views (`vProductCategory`, `vOrderDetail`, `vOrder`), so the view set
covers string, numeric, date, boolean, foreign-key, and composite-key shapes. All three views are exposed
under the landed routing model (`/api/views/{viewName}`), and each also exposes the adapter sub-routes
(`/aggrid`, `/datatable`, `/querybuilder`, `/metadata`).

## Prerequisites

- **.NET 8 SDK** — to build and run the host.
- **Node.js** (with `npm`) — to build and type-check the TypeScript client.

## Run from a clean checkout

Follow these steps in order. They are sufficient to build and run the whole showcase with no additional,
undocumented steps.

### 1. Extract the Northwind database

The Northwind SQLite database is shipped **zipped** and must be extracted before the first run. The host
never recreates or seeds it, so the extracted file is the single source of truth.

1. Open the folder next to the project: `src/Examples/DB`.
2. Extract `Northwind SQLite.zip` into that folder.
3. Ensure the extracted file is named `northwind.db` (rename it if needed).

The result must be `src/Examples/DB/northwind.db`. If it is missing, the host prints this same extraction
guidance and exits with a non-zero code instead of running.

### 2. Build the TypeScript client

The front-end is plain TypeScript compiled with `tsc` (no bundler). The build emits ES modules into
`../wwwroot/js`, which the host serves as static files.

```sh
cd client
npm install
npm run build      # tsc -> emits ES modules into ../wwwroot/js
```

To run the type-check gate on its own (no emit):

```sh
npm run typecheck  # tsc --noEmit — must complete with exit code 0 and zero type errors
```

`npm run build` also enforces types because `tsconfig.json` sets `noEmitOnError`, so a type error fails the
build and emits nothing.

To run the front-end test suites (fast-check property tests on the pure transforms, via Vitest):

```sh
npm test           # vitest run — the fast-check property suites for columns.ts / search.ts
```

### 3. Run the host

From the repository root:

```sh
dotnet run --project src/Examples/Northwind
```

or, from the project folder:

```sh
dotnet run
```

Then open the served URL (the console prints it) in a browser. The default file is `index.html` (the
Simple Wiring page); the shared navigation links the other two pages.

### 4. Self-test (optional but recommended)

A guarded end-to-end round-trip verifies the read, write, OpenAPI, and view-browser paths against the
shipped read-only database, without a browser:

```sh
dotnet run -- selftest
```

The view-browser round-trip drives a single `POST {route}/datatable` request combining paging, global
search, multi-sort, and a jQuery-QueryBuilder advanced filter through the real executor, and asserts the
returned page reflects all channels simultaneously. The process exits `0` only when every self-test
passes.

## The three pages (shared navigation)

Every page injects the same cross-page navigation, so the three demos form one showcase.

| Page | URL | Grid / adapter | Highlights |
|------|-----|----------------|------------|
| **Simple Wiring** | `/` (`index.html`) | AG Grid community **infinite row model** → `POST {route}/aggrid` | The minimal end-to-end integration over `vProductCategory`: block paging on scroll, single/multi-column sort, column filters, and a quick-filter global search sent out-of-band as `?q=`. |
| **View Browser** | `/view-browser.html` | DataTables.NET + jQuery-QueryBuilder → `POST {route}/datatable` | Pick any registered view from a selector (catalog from `GET /api/showcase/views`). Columns are discovered from `GET {route}/metadata`; the advanced-filter panel is populated from `GET {route}/querybuilder`. One request combines paging, global search (minimum 3 characters), multi-sort, and the structured advanced filter — each in its own channel. |
| **Custom Renderer** | `/custom-renderer.html` | AG Grid community (server-side driven) → `POST {route}/aggrid` | Consumer-owned `cellRenderer`s (formatted price, a Discontinued badge, a product link) over `vProductCategory`. Paging, sorting, and filtering all run server-side; the customization is confined to presentation only. Community features only — no AG Grid Enterprise dependency. |

Routes shown as `{route}` resolve per view — for example the Simple Wiring and Custom Renderer pages both
drive `vProductCategory`, so their AG Grid endpoint is `POST /api/views/vProductCategory/aggrid`. The View
Browser page composes the sub-route (`/metadata`, `/querybuilder`, `/datatable`) from whichever view the
user selects.

## Access posture: open, read-only (D94)

This sample runs with **open access** — it opts into anonymous access explicitly via
`AllowAnonymousAccess()` in `Program.cs`. Without an authorizer, Vista would otherwise fail closed at
startup in a non-Development environment (Decision Log D94); calling `AllowAnonymousAccess()` makes the
open posture a deliberate, documented choice for a public read-only demo.

Only explicitly-registered views are browsable (secure-by-default): the view catalog and the selector
enumerate exactly the three registered views, never arbitrary database tables. All three views are
read-only (List + Detail; no write facet), and the host never seeds or mutates the shipped database.

**A real application does not run open.** Gate access by registering an authorizer via
`UseAuthorizer<T>()` instead of `AllowAnonymousAccess()`, so every view request is authorized inside the
host pipeline.

## What's in this sample

| Path | Role |
|------|------|
| `Program.cs` | net8.0 host: registers the three Northwind views, the Vista endpoints (open access), the DataTables + QueryBuilder + AG Grid adapters, the OpenAPI emitter, and the `/api/showcase/views` catalog endpoint; serves `wwwroot`; provides the `selftest` mode. |
| `Views/NorthwindViews.cs` | Style A central template exposing the read-only `vProductCategory`, `vOrderDetail`, and `vOrder` views. |
| `Showcase/ShowcaseCatalog.cs` | Pure `ShowcaseCatalog.Project(IViewRegistry)` helper + `ViewCatalogEntry` DTO backing the `/api/showcase/views` catalog. |
| `client/src/` | TypeScript sources: shared `nav.ts`, the pure transforms (`columns.ts`, `search.ts`, `catalog.ts`), the AG Grid datasource, and the per-page orchestration (`simpleWiring.ts`, `customRenderer.ts`, `viewBrowser.ts`). |
| `client/tests/` | fast-check property suites (run via `npm test`). |
| `wwwroot/*.html` | The three demo pages; `wwwroot/js/*` is the built `tsc` output. |
| `SelfTest.cs` / `WriteSelfTest.cs` / `OpenApiSelfTest.cs` | The guarded end-to-end round-trips invoked by `dotnet run -- selftest`. |
