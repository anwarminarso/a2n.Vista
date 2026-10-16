# Vista baseline — 2026-10-16

QA pass over commit `7fad6321a38688751dcf6f8b5b30ece3fd1c6b3f` (`release: prepare 0.0.4 and gate the release on tag/version agreement (D153)`). `develop` and `main` pointed at that commit. No library, generator, adapter, or sample code was changed.

**Verdict.** The build succeeds and the test suite is green. The headline numbers in `docs/PROJECT-STATUS.md` (555 tests on net8.0, 556 on net10.0, 115 generator tests, 0 failed) match this run, with net9.0 also at 556. The Northwind self-test exits 0. The AG Grid showcase self-test also exits 0. Several status documents are behind the code, and the running sample host cannot exercise `vWritableMemo` against the shipped SQLite file.

Trimmed logs: [vista-baseline-2026-10-16/](./vista-baseline-2026-10-16/).

## 1. Environment

| Item | Value |
| --- | --- |
| OS | Ubuntu 24.04.5 LTS, kernel 6.12.94+, x86_64 |
| SDKs | 8.0.426, 9.0.319, 10.0.402 (`dotnet --version` = 10.0.402) |
| Runtimes | `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` 8.0.32, 9.0.21, 10.0.13 |
| Install | `dotnet-install.sh` channels 8.0, 9.0, 10.0 into `$HOME/.dotnet` |
| TUnit | 1.56.0.0 (report metadata) |
| Configuration | Debug, the default of the commands below |
| Package version in `Directory.Build.props` | 0.0.4 |

Full text: [environment.txt](./vista-baseline-2026-10-16/environment.txt).

## 2. Commands and results

### 2.1 Build

```bash
dotnet build src/a2n.Vista.slnx -c Debug --verbosity minimal
```

Exit 0. **0 errors, 12 warnings**, 50.44s. Log: [dotnet-build.trimmed.log](./vista-baseline-2026-10-16/dotnet-build.trimmed.log).

| Warning | Where | Times |
| --- | --- | --- |
| CS8619 nullability mismatch (`IReadOnlyList<object?>` assigned to `IReadOnlyList<object>`) | `src/Adapters/a2n.Vista.Adapters.DataTablesNet/DataTablesAdapter.cs:124` | once per TFM (3) |
| TUnitAssertions0015 prefer `.IsTrue()` | `src/Tests/a2n.Vista.Tests/DtoSchemaGeneratorTests.cs:134,135,137` | once per TFM (9) |

`a2n.Vista.AotProbe` is in the solution and promotes IL2026/IL3050 (and the related trim/AOT codes) to errors. It compiled in this build, so the build-time AOT analyzer check on the probe passed. A native `dotnet publish -r linux-x64` was not run.

### 2.2 Tests

```bash
dotnet test src/a2n.Vista.slnx -c Debug --verbosity normal
```

Exit 0. Duration 43.656s of test execution (plus a short up-to-date build). Aggregate from the Microsoft.Testing.Platform footer:

| | Count |
| --- | --- |
| total | 2441 |
| succeeded | 2441 |
| failed | 0 |
| skipped | 0 |

Footer: [dotnet-test.summary.log](./vista-baseline-2026-10-16/dotnet-test.summary.log). The footer does not split by project. The per-TFM numbers below were counted from the TUnit HTML reports (`status == pass` on every test object).

| Suite | net8.0 | net9.0 | net10.0 | failed | skipped |
| --- | ---: | ---: | ---: | ---: | ---: |
| `a2n.Vista.Tests` | 555 | 556 | 556 | 0 | 0 |
| `a2n.Vista.SourceGenerators.Tests` | 115 | 115 | 115 | 0 | 0 |
| `a2n.Vista.Client.TypeScript.Tests` | 143 | 143 | 143 | 0 | 0 |
| **Per TFM** | **813** | **814** | **814** | 0 | 0 |

813 + 814 + 814 = 2441.

The one-test gap inside `a2n.Vista.Tests` is intentional and explained in source:

- net8.0 only: `ShowcaseCatalogPropertyTests.Catalog_Is_Exactly_The_Registered_Views` (`#if NET8_0`), because the showcase host is net8.0-only (`a2n.Vista.Tests.csproj` project reference is conditioned the same way).
- net9.0 and net10.0 only: `OpenApiPipelineIntegrationTests` (2 tests, `#if NET9_0_OR_GREATER`), because `Microsoft.AspNetCore.OpenApi` is a net9+ package.

555 − 1 + 2 = 556.

**Against the docs.** The 2026-08-11 header of `docs/PROJECT-STATUS.md` says "555 tests/TFM (net8, +6) / 556 (net10) and 115 generator tests, 0 failed". That sentence matches this run. It does not mention net9.0; net9.0 measured 556, the same as net10.0. The generator count is exact.

The same header's older paragraphs, `ROADMAP.md` ("516 tests/TFM (net8) / 517 (net9/net10) + 112 generator tests + 136 tests/TFM" for the TypeScript client), and `docs/MILESTONES.md` (last updated 2026-07-14, still quoting 516/517 and 112) are behind this commit. The TypeScript suite is **143 per TFM**, not 136. The extra 7 are `PathTraversalContainmentTests` (audit finding SEC-06). `docs/PROJECT-STATUS.md` §9 still says 136.

`docs/PROJECT-STATUS.md` §8 still says `dotnet test` reports "Zero tests ran" and that callers must use `dotnet run`. This run used `dotnet test` under SDK 10.0.402 with `global.json` `test.runner = Microsoft.Testing.Platform`, and it executed every suite. The test project comment documents that as the preferred command. `.github/workflows/ci.yml` still launches each suite with `dotnet run --framework`.

### 2.3 Northwind self-test

```bash
# src/Examples/DB/northwind.db extracted from "Northwind SQLite.zip" (24,702,976 bytes)
cd src/Examples/Northwind && dotnet run --framework net8.0 -c Debug -- selftest
```

**Exit 0.** Log: [northwind-selftest.trimmed.log](./vista-baseline-2026-10-16/northwind-selftest.trimmed.log).

| Stage | Result |
| --- | --- |
| Read (`vProductCategory` paging, filter+search, detail ProductId=1, composite detail OrderId=10248/ProductId=11, DataTables round-trip) | PASS |
| Write (`vWritableMemo` Create/Update/Delete on an isolated in-memory database) | PASS, 0 failed operations. `WriteMapper: GENERATED`, `ViewInvoker: GENERATED` |
| OpenAPI | PASS. `GET /openapi/v1.json` → 200, `openapi` 3.0.4, 18,962 bytes, 15 paths, 0 dangling refs, 0 missing, 0 phantom. Metadata response byte-identical with and without the emitter (1,537 bytes) |

Views registered by this host: `vProductCategory`, `vOrderDetail`, `vWritableMemo`.

### 2.4 AG Grid / DataTables showcase

The showcase is `src/Examples/a2n.Vista.Examples.AgGridNorthwind` (net8.0 only). It registers DataTables, QueryBuilder, AG Grid, and OpenAPI, and it adds `vOrder` plus `GET /api/showcase/views`.

```bash
cd src/Examples/a2n.Vista.Examples.AgGridNorthwind && dotnet run --framework net8.0 -c Debug -- selftest
```

**Exit 0.** Same read/write/OpenAPI shape as Northwind, plus step [6] view-browser round-trip (paging + global search + multi-sort + `jsonQB`) PASS. OpenAPI: 23,450 bytes, **19 paths**, views `vProductCategory`, `vOrderDetail`, `vOrder`, `vWritableMemo`. Log: [aggrid-selftest.trimmed.log](./vista-baseline-2026-10-16/aggrid-selftest.trimmed.log).

Both hosts were then started with `--no-launch-profile` (their launch profiles share ports 52898/52899):

| Host | URL |
| --- | --- |
| Northwind | `http://127.0.0.1:52899` |
| AG Grid showcase | `http://127.0.0.1:52999` |

Startup lines: [host-startup.log](./vista-baseline-2026-10-16/host-startup.log). Both logged the Development warning that no `IViewAuthorizer` is registered. Full probe log: [curl-summary.txt](./vista-baseline-2026-10-16/curl-summary.txt).

| Call | Northwind | Showcase |
| --- | --- | --- |
| `GET /` | 200 `text/html` (5,825 B, DataTables demo) | 200 `text/html` (4,357 B) |
| `GET /view-browser.html` | — | 200 (7,051 B) |
| `GET /custom-renderer.html` | — | 200 (5,111 B) |
| `GET /api/showcase/views` | 404 (route is showcase-only) | 200, four entries: vProductCategory, vOrderDetail, vOrder, vWritableMemo |
| `GET /openapi/v1.json` | 200, 3.0.4, 15 paths, 18,962 B | 200, 3.0.4, 19 paths, 23,450 B |
| `GET /api/views/vProductCategory/metadata` | 200, 1,537 B | — |
| `GET /api/views/vOrder/metadata` | — | 200, 2,062 B |
| `GET …/querybuilder` | 200, 2,128 B | 200, 2,523 B (`vOrder`) |
| `POST …/vProductCategory/list` `{"page":0,"pageSize":2,"sort":[{"field":"ProductName","desc":false}]}` | 200, 2 rows (Alice Mutton, Aniseed Syrup), `totalRows` 77, `totalRowsUnfiltered` 77 | 200, same body (479 B) |
| `POST …/vProductCategory/detail` `{"key":1}` | 200, Chai / Beverages | — |
| `POST …/vOrderDetail/detail` `{"key":{"OrderId":10248,"ProductId":11}}` | — | 200, Queso Cabrales, quantity 12 |
| `POST …/detail` `{"key":999999}` | 404, empty body | — |
| `POST …/vProductCategory/export` `format=csv` | 200 `text/csv`, 4,686 B, 78 lines (header + 77 products) | — |
| `POST …/vProductCategory/export` `format=xlsx` | 200 spreadsheet, 5,467 B | — |
| `POST …/vOrder/export` `format=csv` | — | 200 `text/csv`, 10,867 B, 101 lines |
| `POST …/datatable` form `draw=1&start=0&length=2&columns[0][data]=ProductName` | 200, `draw` 1, `recordsTotal` 77, `data` length 2 | 200, same |
| `POST …/aggrid` `startRow=0,endRow=2,sortModel=[{colId:ProductName,sort:asc}]` | 404 (adapter not registered) | 200, `rowData` length 2, `rowCount` 77 |
| `POST …/aggrid` `sortModel colId=notAField` | — | 400 `application/problem+json`, code `filter-unknown-field`, field `notAField` |
| `POST …/vProductCategory/create`, `/update`, `/delete` | 404 (read-only view, routes not mapped) | — |
| `POST …/vWritableMemo/create` `{"model":{"subject":"qa-baseline","body":"curl"}}` | **409** `write-conflict` | **409** `write-conflict` |
| `POST …/vWritableMemo/list` | **500** `text/plain` developer page | — |
| `POST …/vWritableMemo/update` and `/delete` with `If-Match: "0"` | **500** `text/plain` developer page | — |
| `POST …/vWritableMemo/create` with a JSON array | 400 `write-bulk-not-enabled` | — |

Export ignores the request page size on purpose: `ViewRequestExecutor` rewrites the export request to `Page = 0` and `PageSize = view.Limits.MaxExportRows` before materializing rows. The CSV sizes above are the full filtered sets, bounded by that limit.

The OpenAPI documents contain the writable view's `create` / `update` / `delete` operations and do not contain `/datatable`, `/aggrid`, or `/querybuilder`. That matches the documented v1 scope (adapter endpoints are out of the emitter).

### 2.5 Local pack (not a nuget.org publish)

`dotnet pack -c Debug --no-build` of the eight projects listed in `.github/workflows/publish.yml` `PACK_PROJECTS` produced `0.0.4` `.nupkg` and `.snupkg` files for each, exit 0. `a2n.Vista.Core.0.0.4.nupkg` contains `analyzers/dotnet/cs/a2n.Vista.SourceGenerators.dll` (157,184 bytes). Log: [dotnet-pack.summary.log](./vista-baseline-2026-10-16/dotnet-pack.summary.log).

No package was pushed. Trusted Publishing was not exercised.

## 3. Feature matrix

Claimed-done means the current status docs (`docs/PROJECT-STATUS.md` §1–§2, `ROADMAP.md` "Done", `docs/MILESTONES.md` §2) describe the feature as landed. "Verified" means this pass found the implementation and a passing test or a live call.

| Feature | Claim | Verified | Evidence |
| --- | --- | --- | --- |
| Core (views, metadata, filter contract, ports, `FilterCompiler`) | Done (M1) | Yes | `src/a2n.Vista.Core/` (`Contracts/`, `Metadata/`, `Authoring/`, `Filter/`, `Ports/`). Covered by `a2n.Vista.Tests` (555/556 passing). |
| EF Core read (List/Detail, paging, filter/sort/search, composite keys, dialect) | Done (M1, M3) | Yes | `src/a2n.Vista.EntityFrameworkCore/Execution/EfViewExecutor.cs`, `DefaultQueryDialect.cs`. Northwind self-test steps [1]–[4] PASS against SQLite. |
| EF Core write (Create/Update/Delete, whitelist, concurrency, single `SaveChanges`) | Done (M12, D119/D120) | Yes, in-process | `EfViewExecutor` write methods; `WriteSelfTest` PASS with generated mapper and invoker. Live HTTP write against `northwind.db` fails (see §4). |
| Npgsql dialect | Done | Present, not executed against Postgres | `src/Adapters/a2n.Vista.EntityFrameworkCore.Npgsql/NpgsqlQueryDialect.cs`, `AddVistaNpgsql()`. Packed. No PostgreSQL instance in this environment. |
| AspNetCore action surface | Done (M4, D110) | Yes | `VistaEndpointRouteBuilderExtensions.cs` maps `POST {route}/list\|detail\|export\|create\|update\|delete` and `GET {route}/metadata`. Curl table above. |
| Auth fail-closed + anonymous opt-in | Done (D94) | Partially observed | Samples call `AllowAnonymousAccess()` and log the Development warning when no authorizer is registered. Non-Development fail-closed was not re-run here; `AuthorizationTests` are inside the green suite. |
| Source generators (five incremental generators) | Done (M9 complete) | Yes | `ViewAccessorGenerator`, `WriteMapperGenerator`, `ViewInvokerGenerator`, `ViewJsonContextGenerator`, `StyleAShapeGenerator` in `src/a2n.Vista.SourceGenerators/`. 115 generator tests per TFM, 0 failed. Northwind write self-test reports both generated mapper and invoker. |
| OpenAPI emitter | Done (M18) | Yes | `src/a2n.Vista.OpenApi/`. Self-tests and curl: 3.0.4, 15 paths (Northwind) / 19 paths (showcase). |
| TypeScript client | Done (M17) | Yes, suite only | `src/a2n.Vista.Client.TypeScript/` CLI. 143 tests/TFM passing. The Node fast-check harness under `tests/ts-runtime` was not run (no Node invocation in this pass). |
| DataTables adapter + QueryBuilder schema | Done (M5, M8) | Yes | `src/Adapters/a2n.Vista.Adapters.DataTablesNet/` (`DataTablesAdapter`, `QueryBuilderParser`, `QueryBuilderSchemaAdapter`). Self-test step [5] PASS. HTTP `POST /datatable` and `GET /querybuilder` returned 200. |
| AG Grid adapter | Done (M16, D133–D136, D150) | Yes | `src/Adapters/a2n.Vista.Adapters.AgGrid/` (`AgGridAdapter`, `AgGridFilterModelParser`, `AgGridJsonContext`). Showcase `POST /aggrid` 200. Unknown `colId` returned 400 `filter-unknown-field` (D150). |
| Northwind showcase (D137–D140) | Done | Yes | Three HTML pages 200. Catalog returns exactly the four registered views. Self-test view-browser step PASS. |
| Export CSV + XLSX | Done (M7, D115) | Yes | HTTP export returned `text/csv` and the OpenXML spreadsheet content type. |
| CI workflow | Done (M19) | Inspected, not executed on GitHub | `.github/workflows/ci.yml`: build Release, then `dotnet run` of the three suites on net8/net9/net10. Triggers are `push` and `pull_request` to **`main` only**, plus `workflow_dispatch`. |
| NuGet publish | Done (M19, D153) | Pack verified locally; publish not run | `publish.yml` guard job compares the tag with `<Version>`. `PACK_PROJECTS` lists **eight** libraries, including `Adapters.AgGrid`. Local pack succeeded (§2.5). nuget.org push and OIDC login were not run. |
| Observability (M14, D100) | Not started | Confirmed absent | No `ActivitySource` / `Meter` / `AddHealthChecks` under `src/`. `docs/spec/10-operations-and-observability.md` is still DRAFT. |
| Versioning (M15, D99) | Deferred | Confirmed deferred | `docs/spec/11-versioning-and-deprecation.md` is DRAFT. No version-prefixed routes were observed; OpenAPI is unversioned `3.0.4` at `/openapi/v1.json` (the document name, not a Vista wire version). |
| Bulk writes | Deferred to v1.x | Confirmed rejected | Array create body → 400 `write-bulk-not-enabled`. |

### Scaffold-only adapters

Each of these projects is a `.csproj` (`IsPackable` false) plus an empty `internal` `AssemblyMarker`. No adapter type, no tests, no README. They are excluded from `PACK_PROJECTS`.

| Project | Files beyond the csproj | Scaffold |
| --- | --- | --- |
| `a2n.Vista.Adapters.MudBlazor` | `AssemblyMarker.cs` | Yes |
| `a2n.Vista.Adapters.Telerik` | `AssemblyMarker.cs` | Yes |
| `a2n.Vista.Adapters.Syncfusion` | `AssemblyMarker.cs` | Yes |
| `a2n.Vista.Adapters.TanStackTable` | `AssemblyMarker.cs` | Yes |
| `a2n.Vista.Adapters.PrimeNG` | `AssemblyMarker.cs` | Yes |
| `a2n.Vista.Adapters.OData` | `AssemblyMarker.cs` | Yes |
| `a2n.Vista.Adapters.GraphQL` | `AssemblyMarker.cs` | Yes |
| `a2n.Vista.Newtonsoft` | `AssemblyMarker.cs` (references the Newtonsoft.Json package, ships no converter) | Yes |

`ROADMAP.md` also names a Quasar adapter and PrimeReact/PrimeVue. There is no Quasar project. PrimeReact and PrimeVue are not separate projects.

## 4. Discrepancies and bugs

### 4.1 Running sample: writable view vs the shipped database

`vWritableMemo` is registered on both HTTP hosts and targets `VistaMemos`. The shipped `northwind.db` has no such table. The self-test avoids that by building an isolated in-memory database (`WriteSelfTest`). A caller of the running host gets:

- `POST /list`, `/update`, and `/delete`: HTTP 500 `text/plain` from the developer exception page. Message: `SQLite Error 1: 'no such table: VistaMemos'`. `VistaExceptionHandlingMiddleware` only maps exceptions `VistaProblemResults.TryCreate` accepts, and it rethrows everything else, so the provider exception reaches the Development page with a stack trace (file paths under `/workspace`).
- `POST /create`: HTTP 409 `urn:a2n.vista:error:write-conflict` / code `write-conflict`. `EfViewExecutor.SaveWriteChangesAsync` maps every `DbUpdateException` that is not a `DbUpdateConcurrencyException` to `VistaWriteConflictException`. A missing table surfaces as a constraint conflict.

The in-process write path itself passed. The defect is the combination of a writable route on a database that cannot store the row, and a 409 classification for a missing relation.

### 4.2 Status documents disagree with each other and with the tree

| Document | What it says | What this run / the tree shows |
| --- | --- | --- |
| `docs/PROJECT-STATUS.md` header (2026-08-11) | 555 (net8) / 556 (net10), 115 generator tests | Matches. net9.0 is also 556. TypeScript suite (143) is omitted from that sentence. |
| `docs/PROJECT-STATUS.md` §9 and the M17 paragraph; `ROADMAP.md` status; `docs/MILESTONES.md` | TypeScript suite = 136/TFM; main suite 516/517; generator tests 112 | Measured 143, 555/556/556, and 115. |
| `docs/PROJECT-STATUS.md` opening M19 paragraph and `docs/MILESTONES.md` M19 row | Publish ships **7** libraries; "8 grid-adapter shells"; source-generator packaging "unsettled" and would emit an empty package | `.github/workflows/publish.yml` and `docs/PROJECT-STATUS.md` §2.19 ship **8** libraries, including `a2n.Vista.Adapters.AgGrid`. Scaffolds are 7 adapters + Newtonsoft. `a2n.Vista.SourceGenerators` is `IsPackable` false and is packed inside Core at `analyzers/dotnet/cs` (confirmed in the local nupkg). |
| `docs/MILESTONES.md` §3 and §4 v1.0 | M16 still listed under remaining work, and v1.0 still "remaining" AG Grid + MudBlazor | M16 (AG Grid) is in the DONE table and in the code. MudBlazor is still a scaffold. The §3 intro still lists the TypeScript client as remaining work after M17 landed. |
| `docs/PROJECT-STATUS.md` §8 | `dotnet test` runs zero tests | This `dotnet test` run executed 2441 tests. |
| `docs/spec/03-source-generator.md` | "DESIGN INTENT"; "Pillar 3 is not yet implemented" (2026-06-20) | Five generators are in tree; 115 tests pass; self-test reports GENERATED mapper and invoker. |
| `docs/spec/05-aspnetcore-mapping.md` | "PARTIALLY IMPLEMENTED"; writes return 501 | Write routes execute. Read-only writes are unmapped (404). A bulk body returns 400, not 501. |
| `docs/spec/01-view.md` reconciliation note | Write path still "forward-looking" | Write path is implemented (M12). |
| `docs/spec/04-adapter-contract.md` status line | DataTables only | AG Grid is a second real adapter. The contract file's later notes were not fully revised to say so in the banner. |
| `docs/spec/02-filter-and-query.md` | "PARTIALLY IMPLEMENTED (Pillar 1)" | Server-half engine is treated as done in the status docs. The spec banner was not moved. |
| `ROADMAP.md` package list | Quasar adapter | No project. |

`docs/spec/10` and `docs/spec/11` correctly remain drafts.

### 4.3 Other notes

- The Debug build is green in the sense of zero errors. It is not warning-free (12 warnings, §2.1). Later status entries that say "0 warnings" do not describe this tree.
- `src/Examples/a2n.Vista.Examples.StyleBExecP2/` is a generator fixture project. It is not in `src/a2n.Vista.slnx`, and nothing references it. `ListPageBoundPropertyTests` drives `a2n.Vista.StyleBExecSample` instead. The P2 project is leftover.
- CI will not run on a pull request into `develop`. `ci.yml` listens to `main`.
- `Discontinued` on `vProductCategory` is metadata type `String` and serializes as `"0"` / `"1"`. That matches the metadata the server published; it is recorded so a later UI pass does not treat it as a boolean.

## 5. Suggested next tests

1. Repeat `dotnet test` in **Release**, the configuration `ci.yml` uses, and confirm the same 555/556/556 + 115 + 143 split.
2. Run the TypeScript fast-check harness (`src/a2n.Vista.Client.TypeScript/tests/ts-runtime`) and `tsc --noEmit` for the showcase scripts.
3. Start the showcase with `ASPNETCORE_ENVIRONMENT=Production` (and a deliberate anonymous opt-in, which the sample already has) and confirm a missing `VistaMemos` table becomes an RFC 7807 problem with no stack, and decide whether create should stay 409.
4. Point `AddVistaNpgsql()` at a real Postgres and repeat one List plus one Create/Update/Delete.
5. `dotnet publish` the AOT probe (or a trimmed host) for a runtime native check. The build-time analyzer already passed inside the solution build.
6. Dry-run `publish.yml`'s guard on a mismatched tag versus `<Version>0.0.4</Version>` in a fork, without pushing to nuget.org.
7. Add `develop` to the CI trigger if that branch is the integration target, or document that only `main` is gated.
8. Refresh `ROADMAP.md`, `docs/MILESTONES.md`, the spec banners in `docs/spec/03` and `docs/spec/05`, and the TypeScript test count (136 → 143) so the next baseline has one set of numbers.
