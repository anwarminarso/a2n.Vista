# Playwright end-to-end tests

`tests/e2e` drives the real Northwind sample and the real AG Grid / DataTables showcase. Both hosts are Kestrel processes started with `dotnet run` against the shipped `northwind.db`. API checks use `HttpClient`. UI checks use headless Chromium.

## Why TUnit and Microsoft.Playwright

The repository's test runner is TUnit on Microsoft.Testing.Platform (Decision Log D7, `global.json`). The e2e project stays on that runner and references the `Microsoft.Playwright` driver directly.

It is not an NUnit or xUnit Playwright project, and it is not a Node `@playwright/test` suite. A second runner or a Node toolchain would sit outside the way every other Vista test is built and executed. The samples themselves are `net8.0` only, so the e2e project is `net8.0` only as well.

The project is intentionally absent from `src/a2n.Vista.slnx`. `dotnet test src/a2n.Vista.slnx` remains the library baseline: no browser download, no Kestrel process, no dependency on the Playwright package.

## What it covers

Both hosts listen on loopback ports chosen so they do not collide with the samples' launch profiles (`18771` Northwind, `18772` showcase). The database path is relative to the sample's working directory (`../DB/northwind.db`), so each process is started from its project directory with `--no-launch-profile`.

API (Northwind host, unless noted):

- `vProductCategory` list, detail, paging, sort, and `Contains` filter
- composite detail on `vOrderDetail` (`OrderId` 10248, `ProductId` 11)
- CSV and XLSX export (export ignores the requested page size and returns the full set)
- DataTables server protocol (`POST .../datatable`, form body)
- read-only create, update, and delete are unmapped (`404`)
- OpenAPI document at `/openapi/v1.json`
- QueryBuilder schema
- AG Grid route is absent on the DataTables-only host (`404`)

API (showcase host):

- `GET /api/showcase/views` catalog
- AG Grid server protocol, including unknown `colId` → `400` `filter-unknown-field`
- DataTables protocol on the same view
- `vOrder` list and CSV export
- OpenAPI document includes the fourth view
- read-only writes on `vProductCategory` are unmapped (`404`)

UI (Chromium):

- Simple Wiring (`/`): grid renders rows, sorting `ProductName` flips ascending then descending, the Product floating filter narrows to names containing `Chai`
- Custom renderer (`/custom-renderer.html`): a `UnitPrice` cell is formatted with `$`
- View Browser (`/view-browser.html`): selecting `vProductCategory` renders `Alice Mutton`, and the next page is a different first row
- Northwind DataTables page (`/`): first row is `Alice Mutton`
- CSV export download. The showcase has no export button. The test issues `POST /api/views/vProductCategory/export` from the page and clicks a download link so Playwright records a real download. That keeps the sample UI unchanged.

The grids load AG Grid 32.3.9, DataTables 2.1.8, and jQuery 3.7.1 from public CDNs. The UI tests need those hosts to be reachable.

## Known gap: `vWritableMemo`

`WritableMemoRoundTripTests` describes the HTTP round trip the live host should provide: list returns 200, create returns 200 with a key and an `ETag`, detail returns the new subject, update with `If-Match` returns 200, delete with `If-Match` returns 200, and a following detail returns 404.

The test is skipped. On the shipped database it does not pass, and the skip reason is the bug:

- `src/Examples/DB/northwind.db` (inside `Northwind SQLite.zip`) has no `VistaMemos` table.
- `POST .../vWritableMemo/list`, `update`, and `delete` return **500** (`SQLiteException: no such table`). `VistaExceptionHandlingMiddleware` only maps exceptions that `VistaProblemResults.TryCreate` accepts, so this surfaces as the developer exception page.
- `POST .../create` returns **409** `write-conflict`. `EfViewExecutor.SaveWriteChangesAsync` classifies every non-concurrency `DbUpdateException` as `VistaWriteConflictException`.
- The sample self-test (`dotnet run -- selftest`) still passes. Its write check uses an isolated in-memory database, not `northwind.db`.

There is a second gap in the same round trip, asserted by the skipped test so it stays visible: `HandleCreateAsync` returns the new key and does not emit an `ETag`. `MemoRow` also omits `RowVersion`. A client that only has the create response cannot form the `If-Match` header that update and delete require (a missing header is **428**). The in-memory self-test does not go through that HTTP response.

Do not "fix" the skip by expecting 409 or 500. Unskip the test when a live host can persist a memo and hand back a concurrency token.

## Run it

From the repository root, with the .NET 8 SDK on `PATH` (8, 9, and 10 when you also want the library baseline):

```bash
bash tests/e2e/run.sh
```

The script:

1. Extracts `src/Examples/DB/Northwind SQLite.zip` to `northwind.db` when the database file is missing. The database is gitignored.
2. Builds both sample hosts and `tests/e2e/a2n.Vista.E2E.csproj` for `net8.0`.
3. Installs Playwright's Chromium by running the Node driver that `Microsoft.Playwright` copies into the build output (`.playwright/node/.../node .playwright/package/cli.js`). Playwright 1.63 ships `playwright.ps1` rather than `playwright.sh`, so the script does not require PowerShell. When passwordless `sudo` works, it also installs the OS libraries (`install-deps chromium`).
4. Runs `dotnet test tests/e2e/a2n.Vista.E2E.csproj --no-build`. Extra arguments are forwarded to `dotnet test`. The runner is Microsoft.Testing.Platform, so a VSTest `--filter` is not accepted. Narrow a run with TUnit's tree filter, for example `bash tests/e2e/run.sh -- --treenode-filter "/*/*/NorthwindApiTests/*"`.

`dotnet test` on the e2e project alone does not build the hosts. `run.sh` is the supported entry point. The session hook also runs `playwright install chromium`, which is a no-op once the script has installed the browser.

Chromium is launched with `--no-sandbox` and `--disable-dev-shm-usage` so the same command works in a container and on GitHub-hosted runners.

## CI

`.github/workflows/e2e.yml` runs `bash tests/e2e/run.sh` on pull requests into `develop`, and on `workflow_dispatch`. It does not modify `.github/workflows/ci.yml`. The library matrix in `ci.yml` already runs on push and pull_request to `main` and `develop` (PR #7); this job adds the browser suite. The job timeout is 20 minutes. It needs outbound network access for NuGet, the Playwright browser download, and the grid CDNs.

## Startup burst smoke (informational)

`tests/e2e/run-startup-burst.sh` is a separate HTTP smoke. It is not part of `run.sh` and it does not install Chromium. The required Playwright job does not run it.

It builds the Northwind sample for `net9.0` and `net10.0` (`-p:VistaHostFramework`, so the default solution build stays `net8.0`), starts each host with no HTTP warmup, and sends 32 concurrent list and read requests. The assertion is that none of them return 500.

This is a smoke only. It is not a regression proof of the EF Core lazy relational-model race ([dotnet/efcore#39214](https://github.com/dotnet/efcore/issues/39214), Vista issue #13). That race is rare. A green wave does not show it is gone. Deterministic evidence for the warmup is the unit-test harness on [PR #12](https://github.com/anwarminarso/a2n.Vista/pull/12) (`RelationalModelWarmup.WarmAll` and `Concurrent_EnsureCreated_After_Warmup_Completes`), which is not merged here. A library host warmup is not in this tree.

The workflow runs the script as its own job, `Startup burst smoke (informational, not required)`, with `continue-on-error: true`. A failure there does not fail the required Playwright check. Counts from the 2026-10-16 run are in [vista-startup-burst-2026-10-16.md](./vista-startup-burst-2026-10-16.md).
