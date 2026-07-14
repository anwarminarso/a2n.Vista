# Generated-runtime test harness (`ts-runtime`)

A Node workspace that exercises the **generated** a2n.Vista TypeScript client — the
`Generated_Output` emitted by the M17 generator (`src/a2n.Vista.Client.TypeScript`).

The C# generator has its own property tests on the repo's TUnit runner (CsCheck). This
harness is the *other* runtime: it runs **fast-check** property tests under Node against the
emitted client, and type-checks the generated output. It does not depend on the generator; it
targets a directory of generated files supplied by an environment variable.

## Layout

```
tests/ts-runtime/
  package.json              # devDeps: typescript, vitest, fast-check, @types/node
  tsconfig.json             # strict type-check config for the harness sources
  vitest.config.ts          # test runner config
  scripts/
    typecheck-generated.mjs # type-checks the generated output directory
  src/
    harness/                # shared helpers + scaffolding smoke tests
    properties/             # runtime property suites land here (later tasks)
```

## Install

```
npm install
```

## Pointing at a generated client

The harness targets a generated output directory named by the `VISTA_TS_CLIENT_DIR`
environment variable. That directory is what the generator emits: `index.ts`, `types.ts`,
`filter-node.ts`, `runtime/`, `views/`, and `README.md`.

PowerShell:

```powershell
$env:VISTA_TS_CLIENT_DIR = "D:\path\to\generated\client"
```

bash / zsh:

```bash
export VISTA_TS_CLIENT_DIR=/path/to/generated/client
```

When the variable is unset, the type-check gate is skipped (nothing generated yet) and the
runtime property suites that require a client are expected to skip.

## Scripts

| Script                        | What it does                                                        |
| ----------------------------- | ------------------------------------------------------------------- |
| `npm run typecheck`           | `tsc --noEmit` over the harness sources                             |
| `npm run typecheck:generated` | `tsc --noEmit` over `$VISTA_TS_CLIENT_DIR` (skips if unset)         |
| `npm test`                    | Runs the vitest suites (fast-check property tests)                  |
| `npm run test:watch`          | Runs vitest in watch mode                                           |
| `npm run verify`              | `typecheck` + `typecheck:generated` + `test`                        |

## Committed generated fixture + shared scaffolding (reused by all 14.x / 15.x suites)

Task 14.1 established a **committed** generated client and shared test helpers that every later
runtime property suite (14.2–14.7, 15.2/15.3) reuses without regenerating or duplicating anything.

### The generated client (the artifact under test)

- **Path:** `tests/ts-runtime/generated/` (committed — **not** git-ignored).
- **Produced by** the C# generator CLI (write facets ON so writable-view suites have create/update/
  delete; a default base URL baked in):

  ```
  dotnet run --framework net8.0 --project src/a2n.Vista.Client.TypeScript -- \
    --source src/Tests/a2n.Vista.Client.TypeScript.Tests/Fixtures/valid-vista-document.json \
    --out    src/a2n.Vista.Client.TypeScript/tests/ts-runtime/generated \
    --emit-write-facets --base-url https://api.example.com
  ```

  Generation is deterministic/idempotent (Requirement 9): re-running with the same args yields
  byte-identical output. **Do not hand-edit** anything under `generated/`.

### Shared helpers (`src/harness/`)

| File | Exports | Use |
| ---- | ------- | --- |
| `generated.ts` | `export * from "../../generated/index"` | Single import surface for the generated client's public API (types + runtime). Import from `../harness/generated.js`. |
| `recording-transport.ts` | `RecordingTransport`, `RejectingTransport`, `makeResponse`, `makeProblemResponse`, `CannedResponse` | In-memory `HttpTransport` doubles: `RecordingTransport` records every `HttpRequest` (`.requests`, `.onlyRequest`, `.callCount`) and answers with queued/canned/default `HttpResponse`s (`.enqueue`, `.enqueueCanned`, `.respondWith`); `RejectingTransport` always rejects (transport-error / no-retry tests). |
| `generated-client.ts` | `resolveGeneratedClientDir`, `requireGeneratedClientDir`, `GENERATED_CLIENT_DIR_ENV` | Resolve a generated dir from `VISTA_TS_CLIENT_DIR` (for gates/harnesses that target an arbitrary output). The property suites import `generated.ts` directly, so they do **not** need this env var. |

### How to run

```powershell
# from tests/ts-runtime
npm test                    # runs all *.test.ts under src/ (vitest)
npm run typecheck           # tsc over the harness AND the transitively-imported generated client

# full gate (also runs the standalone generated-dir type-check):
$env:VISTA_TS_CLIENT_DIR = "$PWD\generated"
npm run verify              # typecheck + typecheck:generated + test
```

## Conventions

- Each property suite is tagged `Feature: typescript-client, Property {n}: {text}` and runs a
  minimum of 100 iterations (`{ numRuns: 100 }`).
- Runtime property suites live under `src/properties/*.test.ts` and import the generated client via
  `../harness/generated.js`.
- All published artifacts (code, config, comments, docs) are in English.
