# Vista AG Grid Northwind sample

A runnable, end-to-end demo of the Vista **AG Grid adapter**. An AG Grid
[infinite row model](https://www.ag-grid.com/javascript-data-grid/infinite-scrolling/) front-end
(written in TypeScript) drives the read-only Northwind `vProductCategory` view through the Vista AG Grid
adapter endpoint:

```
POST /api/views/vProductCategory/aggrid
```

Every grid interaction — block paging on scroll, single/multi-column sort, text/number column filters,
combined `AND`/`OR` conditions, and quick-filter global search — becomes one `POST` to that endpoint,
mapped by the adapter onto the neutral Vista query contract and executed against a real Microsoft
Northwind SQLite database. The host is an ASP.NET Core app targeting **net8.0**.

> **Why the infinite row model (not the server-side row model)?** The spec (D136 / R7.2) originally
> targeted AG Grid's *server-side row model*, but that is an **AG Grid Enterprise** feature — the
> community package throws `unable to use rowModelType = 'serverSide'` at runtime, so the grid never
> renders without a license. The **infinite row model** ships in `ag-grid-community`, needs no license,
> and drives the exact same block-paging-per-request contract: each block request carries
> `startRow`/`endRow` plus the grid's `sortModel`/`filterModel`, which the Vista adapter binds unchanged.
> The C# adapter, its tests, and the self-test are identical either way — only the front-end row model
> differs. If you have an Enterprise license and prefer the server-side row model, swap
> `createVistaAgGridDatasource` back to an `IServerSideDatasource` and set `rowModelType: 'serverSide'`;
> the request/response shapes are already compatible.

## Prerequisites

- **.NET 8 SDK** — to build and run the host.
- **Node.js** (with `npm`) — to build the TypeScript client.

## Run from a clean checkout

Follow these steps in order. They are sufficient to build and run the sample with no additional,
undocumented steps.

### 1. Extract the Northwind database

The Northwind SQLite database is shipped **zipped** and must be extracted before the first run. The host
never recreates or seeds it, so the extracted file is the single source of truth.

1. Open the folder next to the project: `src/Examples/DB`.
2. Extract `Northwind SQLite.zip` into that folder.
3. Ensure the extracted file is named `northwind.db` (rename it if needed).

The result must be `src/Examples/DB/northwind.db`. If it is missing, the host prints this same guidance
and exits with a non-zero code instead of running.

### 2. Build the TypeScript client

The front-end is plain TypeScript compiled with `tsc` (no bundler). The build emits ES modules into
`../wwwroot/js`, which the host serves as static files.

```sh
cd client
npm install
npm run build      # tsc -> emits main.js / vistaAgGridDatasource.js into ../wwwroot/js
```

To run the type-check gate on its own (no emit):

```sh
npm run typecheck  # tsc --noEmit — must complete with exit code 0 and zero type errors
```

`npm run build` also enforces types because `tsconfig.json` sets `noEmitOnError`, so a type error fails
the build and emits nothing.

### 3. Run the host

From the repository root:

```sh
dotnet run --project src/Examples/a2n.Vista.Examples.AgGridNorthwind
```

or, from the project folder:

```sh
dotnet run
```

Then open the served URL (the console prints it) in a browser. The default file is `index.html`, which
mounts the grid.

### 4. Self-test (optional but recommended)

A guarded end-to-end round-trip verifies the adapter path — request binding, mapping to the neutral query,
the real Core executor, and response shaping — without a browser:

```sh
dotnet run -- selftest
```

It drives an AG Grid `IServerSideGetRowsRequest` (block paging, two `sortModel` keys, a two-condition
combined `filterModel`, and a quick filter) through the same path the endpoint uses, asserts the
`{ rowData, rowCount }` shape and camelCase serialization, and exits `0` only on PASS.

## Composition: the thin AG-Grid datasource (D136)

The front-end talks to Vista through a **thin, hand-written** AG Grid `IDatasource` (infinite row model),
`client/src/vistaAgGridDatasource.ts`. On each block request it:

- assembles `{ startRow, endRow, sortModel, filterModel }` from the AG Grid `IGetRowsParams` (this body is
  field-compatible with the adapter's `IServerSideGetRowsRequest`),
- `POST`s it as JSON to `{route}/aggrid` (here `/api/views/vProductCategory/aggrid`), appending the
  current quick-filter text as a `?q=` query-string parameter when present,
- feeds a successful `{ rowData, rowCount }` response into `params.successCallback(rowData, rowCount)` (the
  `rowCount` is the known total, so AG Grid can detect the last block),
- and on an HTTP error or network failure calls `params.failCallback()` (leaving the displayed rows
  unchanged) and surfaces a visible error message.

Note the column definitions separate `field` (camelCase, e.g. `productName`) from `colId` (PascalCase,
e.g. `ProductName`): Vista serializes row JSON as camelCase, while the adapter matches `sortModel`/
`filterModel` `colId`s against the view's PascalCase field names.

### Why it is hand-written and not the M17 generated client

The M17 `a2n.Vista.Client.TypeScript` generated client is **OpenAPI-driven**: it generates strictly from
the emitted OpenAPI document. M18 **deferred adapter endpoints**, so `{route}/aggrid` is **not** described
in the OpenAPI document today. The datasource must therefore be authored by hand against the AG Grid
server-side row model contract.

The generated client could still optionally supply the row **DTO types**, but the datasource wiring itself
is hand-authored. If a future spec adds the adapter endpoint to the OpenAPI document, this datasource can
migrate to the generated client with **no grid-side change** — `main.ts` only depends on the
`IServerSideDatasource` interface, not on how it is implemented.

## Quick-filter transport

The quick-filter (global search) text is sent **out-of-band** as a `?q=` query-string parameter on the
`POST`, so the JSON body stays a faithful `IServerSideGetRowsRequest`. The adapter reads it from
`AdapterRequest.Values["q"]`, and the value is capped at **1,024 characters** (a longer value is rejected
with `400 adapter-bind-failed`).

## Upgrading to the server-side row model (AG Grid Enterprise)

This sample uses the community **infinite row model** so it runs with no license. AG Grid's **server-side
row model** (the shape D136 / R7.2 originally described) is an **Enterprise** feature — the community
package throws `unable to use rowModelType = 'serverSide'` at runtime. If you have an Enterprise license
and want the server-side row model instead, the server side needs no change (the request/response shapes
are already compatible); on the client:

1. Add an import-map entry for `ag-grid-enterprise` in `wwwroot/index.html` (alongside the existing
   `ag-grid-community` entry), pointing at its CDN ESM build.
2. In `client/src/main.ts`, set your license key and register the enterprise modules before creating the
   grid, then set `rowModelType: 'serverSide'` and pass the datasource as `serverSideDatasource`:

   ```ts
   import { LicenseManager, ModuleRegistry, ServerSideRowModelModule } from 'ag-grid-enterprise';

   LicenseManager.setLicenseKey('<your-license-key>');
   ModuleRegistry.registerModules([ServerSideRowModelModule]);
   ```

3. Switch `createVistaAgGridDatasource` to return an `IServerSideDatasource` (read `params.request` and
   call `params.success({ rowData, rowCount })`). Then re-run `npm run build`.

## What's in this sample

| Path | Role |
|------|------|
| `Program.cs` | net8.0 host: registers the Northwind view (Style A), the Vista endpoints, and the AG Grid adapter; serves `wwwroot`; provides the `selftest` mode. |
| `Views/AgGridNorthwindViews.cs` | Style A central template exposing the read-only `vProductCategory` view. |
| `client/src/main.ts` | Grid bootstrap: column defs, server-side row model options, quick-filter wiring. |
| `client/src/vistaAgGridDatasource.ts` | The thin, hand-written `IServerSideDatasource` (D136). |
| `wwwroot/index.html` | Demo page + import map; `wwwroot/js/*` is the built `tsc` output. |
| `AgGridSelfTest.cs` | The guarded end-to-end round-trip invoked by `dotnet run -- selftest`. |
