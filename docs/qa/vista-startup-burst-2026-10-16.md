# Startup burst smoke — 2026-10-16

This is a smoke only. It is **not** a regression proof of the EF Core 9/10 lazy relational-model race ([dotnet/efcore#39214](https://github.com/dotnet/efcore/issues/39214), Vista [issue #13](https://github.com/anwarminarso/a2n.Vista/issues/13)). The race is rare. A wave of 32 requests that all return 200 does not show the race is gone, and one red run would not measure how often it happens.

Recorded on branch `qa/vista-startup-burst`, parent `9e3fc24` (`develop`). No library code was changed. The Northwind sample project gained an opt-in `VistaHostFramework` property so this smoke can build that host for `net9.0` and `net10.0`. With the property unset, the sample stays `net8.0`.

## What would actually show the warmup

Deterministic evidence for the test-harness warmup is the unit tests on [PR #12](https://github.com/anwarminarso/a2n.Vista/pull/12) (`cursor/fix-composite-key-model-race-86a9`, open, not merged into `9e3fc24`):

- `RelationalModelWarmup.WarmAll` finalizes each concrete test `DbContext` on one thread (`EnsureCreated`) before TUnit or CsCheck run in parallel.
- `CompositeKeyOrderIndependencePropertyTests.Concurrent_EnsureCreated_After_Warmup_Completes` then runs 32 threads of `EnsureCreated`. That pull request's own remarks say this checks the post-warmup path only. It does not prove a burst before warmup would have thrown.
- `Reported_Seed_9CKbg4_346s7_Deletes_The_Same_Row_For_Both_Key_Orders` replays the composite-key sample from CI run 38877351961.

A library host warmup (issue #13, option 1: finalize the relational model at startup) is not in this tree. PR #12 warms the test harness only.

EF 8.0.11 finalizes the relational model during context initialization, so this smoke does not start a net8 host. The pinned packages exercised here are EF Core 9.0.6 and 10.0.0.

## What this smoke does

`bash tests/e2e/run-startup-burst.sh` builds `src/Examples/Northwind` twice (`-p:VistaHostFramework=net9.0` and `net10.0`), then runs `tests/e2e/a2n.Vista.E2E.StartupBurst.csproj` (TUnit on `net8.0`, no Playwright). The project is not in `src/a2n.Vista.slnx`. It is also not compiled into `a2n.Vista.E2E`: that assembly's session hook issues `GET /` before any test, which would warm the host.

For each TFM the test:

1. Starts `dotnet <Northwind.dll>` from the Northwind project directory, with `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS` on loopback (`18791` for net9, `18792` for net10). No launch profile.
2. Waits until the process logs `Now listening on`. It does not send a readiness HTTP request.
3. Immediately releases 32 dedicated threads on a barrier. Each thread sends one call. The eight calls, repeated four times, are list and read only (`vWritableMemo` is not called; the shipped database has no `VistaMemos` table, so that view returns 500/409 for a different reason):
   - `POST /api/views/vProductCategory/list`
   - `POST /api/views/vProductCategory/detail` `{"key":1}`
   - `POST /api/views/vOrderDetail/detail` `{"key":{"OrderId":10248,"ProductId":11}}`
   - `GET /api/views/vProductCategory/metadata`
   - `GET /api/views/vProductCategory/querybuilder`
   - `GET /openapi/v1.json`
   - `POST /api/views/vProductCategory/datatable` (form)
   - `POST /api/views/vOrderDetail/list`
4. Fails if any call throws or returns status 500 or higher. There is no retry.

Vista hosted services still run during host start and may touch the `DbContext` before or during the wave. Northwind's views declare keys, so startup key derivation does not need the relational model. The log line `Application started` had not been observed when the wave was released (`applicationStartedBeforeBurst=False` on both TFMs).

## Environment

- Ubuntu 24.04.5 LTS, x64
- SDK 10.0.402. Shared frameworks: `Microsoft.NETCore.App` / `Microsoft.AspNetCore.App` 8.0.32, 9.0.21, 10.0.13
- Smoke runner: `net8.0` (runtime 8.0.32), TUnit 1.56.0
- Hosts: Northwind `net9.0` on EF Core 9.0.6 (runtime 9.0.21), Northwind `net10.0` on EF Core 10.0.0 (runtime 10.0.13)
- Database: `src/Examples/DB/northwind.db` extracted from `Northwind SQLite.zip` (gitignored, not committed)

## Result

TUnit report from the run at 16 Oct 2026, 09:44:51 UTC. Wall time 2.3s. Runner summary: 2 passed, 0 failed, 0 skipped, duration 2s 632ms.

```text
startup-burst tfm=net9.0 runtimeTfm=net9.0 ef=9.0.6 applicationStartedBeforeBurst=False responses=32 statuses=[200 x32]
startup-burst tfm=net10.0 runtimeTfm=net10.0 ef=10.0.0 applicationStartedBeforeBurst=False responses=32 statuses=[200 x32]
```

| Test | Host | EF Core | Responses | Statuses | Result |
| --- | --- | --- | --- | --- | --- |
| `Net9_Host_First_Request_Wave_Returns_No_500` | `net9.0` | 9.0.6 | 32 | 200 × 32 | passed (1053 ms) |
| `Net10_Host_First_Request_Wave_Returns_No_500` | `net10.0` | 10.0.0 | 32 | 200 × 32 | passed (1153 ms) |

No response body contained `GetRelationalModel`, `ModelNotFinalized`, or `database is locked`, because no response was a 500. That is an observation about this wave, not evidence the race is closed.

After the smoke, the script builds the Northwind sample again for the default `net8.0` so its assets file is not left pointing at `net10.0`. That build succeeded. `dotnet msbuild` on the sample with `VistaHostFramework` unset reports `TargetFramework=net8.0`.

## CI

`.github/workflows/e2e.yml` keeps the required Playwright job (`bash tests/e2e/run.sh`) unchanged. The smoke is a second job, `Startup burst smoke (informational, not required)`, with `continue-on-error: true`. A failure there does not fail the workflow. The job comment states the same limit as this note: the smoke is not a regression test for efcore#39214.
