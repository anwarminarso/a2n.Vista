# a2n.Vista QA notes

This folder holds independent verification of the repository's claimed status. The library and sample code are the source of truth; these notes record what was actually built, tested, and called.

| Report | Commit measured | What it covers |
| --- | --- | --- |
| [vista-baseline-2026-10-16.md](./vista-baseline-2026-10-16.md) | `7fad632` (`develop`, same commit as `main` on that day) | .NET 8/9/10 Debug build, `dotnet test` on `src/a2n.Vista.slnx`, Northwind and AG Grid showcase self-tests, HTTP probes of the running samples, and a claimed-vs-verified feature matrix |

Trimmed logs for that baseline live in [vista-baseline-2026-10-16/](./vista-baseline-2026-10-16/).

## How a baseline is produced

From a clean tree, with the .NET 8, 9, and 10 SDKs on `PATH`:

```bash
dotnet build src/a2n.Vista.slnx
dotnet test src/a2n.Vista.slnx
# src/Examples/DB/northwind.db is the committed read-only template; hosts copy it
cd src/Examples/Northwind && dotnet run -- selftest
```

`global.json` selects the Microsoft.Testing.Platform runner, so `dotnet test` executes the TUnit suites. Per-TFM counts come from each suite's TUnit HTML report under `TestResults/`, because the solution-level summary prints only the grand total.

`src/Examples/DB/northwind.db` is the tracked template. Do not commit anything under `bin/` and `obj/`.
