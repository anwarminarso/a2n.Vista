# a2n.Vista QA notes

This folder holds independent verification of the repository's claimed status. The library and sample code are the source of truth; these notes record what was actually built, tested, and called.

| Report | Commit measured | What it covers |
| --- | --- | --- |
| [vista-baseline-2026-10-16.md](./vista-baseline-2026-10-16.md) | `7fad632` (`develop`, same commit as `main` on that day) | .NET 8/9/10 Debug build, `dotnet test` on `src/a2n.Vista.slnx`, Northwind and AG Grid showcase self-tests, HTTP probes of the running samples, and a claimed-vs-verified feature matrix |
| [e2e-playwright.md](./e2e-playwright.md) | — | How to run the Playwright suite, what it covers, and the skipped `vWritableMemo` round trip |
| [vista-e2e-2026-10-16.md](./vista-e2e-2026-10-16.md) | `qa/vista-e2e` (parent `7fad632`, then merged with `develop`) | Counts from the first green run of that suite, plus the library `dotnet test` baseline taken on the same tree |
| [vista-tanstack-smoke-2026-10-16.md](./vista-tanstack-smoke-2026-10-16.md) | `9e3fc24` (`develop`, PR #10) | TanStack showcase smoke: paging, sort, global filter, column hide, unknown sort |

Trimmed logs for the baseline live in [vista-baseline-2026-10-16/](./vista-baseline-2026-10-16/).

## How a baseline is produced

From a clean tree, with the .NET 8, 9, and 10 SDKs on `PATH`:

```bash
dotnet build src/a2n.Vista.slnx
dotnet test src/a2n.Vista.slnx
# extract src/Examples/DB/"Northwind SQLite.zip" to src/Examples/DB/northwind.db first
cd src/Examples/Northwind && dotnet run -- selftest
```

`global.json` selects the Microsoft.Testing.Platform runner, so `dotnet test` executes the TUnit suites. Per-TFM counts come from each suite's TUnit HTML report under `TestResults/`, because the solution-level summary prints only the grand total.

## How the Playwright suite is run

`tests/e2e` is not part of `src/a2n.Vista.slnx`, so the baseline command does not download a browser or start the samples. The end-to-end script builds the real hosts, installs Chromium, and runs the suite:

```bash
bash tests/e2e/run.sh
```

Do not commit `northwind.db` (it is gitignored) or anything under `bin/`, `obj/`, or `TestResults/`.
