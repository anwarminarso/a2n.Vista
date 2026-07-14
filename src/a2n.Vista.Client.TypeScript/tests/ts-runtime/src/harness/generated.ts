// Shared scaffolding for the 14.x/15.x generated-runtime property tests.
//
// This module is the single import surface the runtime property suites use to reach the
// *generated* client (the `Generated_Output` committed under `tests/ts-runtime/generated/`).
// It re-exports the generated client's whole public surface (the barrel `index.ts`), so a test
// imports types and runtime helpers from one place and never hard-codes deep paths into the
// generated tree:
//
//   import { classifyResponse, type ClientResult } from "../harness/generated.js";
//
// The generated client is the artifact under test. It was produced by the C# generator CLI:
//
//   dotnet run --project src/a2n.Vista.Client.TypeScript -- \
//     --source src/Tests/a2n.Vista.Client.TypeScript.Tests/Fixtures/valid-vista-document.json \
//     --out    src/a2n.Vista.Client.TypeScript/tests/ts-runtime/generated \
//     --emit-write-facets --base-url https://api.example.com
//
// Regenerating into that directory (same args) is idempotent (Requirement 9), so the committed
// fixture stays byte-stable. Do NOT hand-edit anything under `generated/`.

export * from "../../generated/index";
