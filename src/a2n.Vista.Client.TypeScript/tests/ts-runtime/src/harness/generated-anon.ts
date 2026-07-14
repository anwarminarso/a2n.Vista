// Shared scaffolding for the authorization-enforcement property test (task 14.2, Property 8).
//
// This module is the single import surface for the *anonymous* generated client — a second
// `Generated_Output` committed under `tests/ts-runtime/generated-anon/`. It exists solely to cover
// Requirement 7.5 ("the app opted into anonymous access"): the primary fixture
// (`generated/`) is fully secured at the document level, so it cannot exercise the anonymous path.
//
// The anonymous client was produced by the same C# generator CLI, from a copy of the valid Vista
// fixture with the top-level `security` requirement and `components.securitySchemes` removed:
//
//   dotnet run --framework net8.0 --project src/a2n.Vista.Client.TypeScript -- \
//     --source src/a2n.Vista.Client.TypeScript/tests/ts-runtime/fixtures/anonymous-vista-document.json \
//     --out    src/a2n.Vista.Client.TypeScript/tests/ts-runtime/generated-anon \
//     --base-url https://api.example.com
//
// Regenerating into that directory (same args) is idempotent (Requirement 9), so the committed
// anonymous fixture stays byte-stable. Do NOT hand-edit anything under `generated-anon/`.
//
// It re-exports the anonymous client's whole public barrel; a test imports it under its own
// namespace so its type/value names do not collide with the primary (secured) client's:
//
//   import * as anon from "../harness/generated-anon.js";

export * from "../../generated-anon/index";
