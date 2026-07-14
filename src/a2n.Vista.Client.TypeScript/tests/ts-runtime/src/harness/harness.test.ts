import { describe, expect, it } from "vitest";
import fc from "fast-check";

import { GENERATED_CLIENT_DIR_ENV, resolveGeneratedClientDir } from "./generated-client.js";

// Scaffolding smoke tests: they prove the runner, TypeScript compilation, and
// fast-check are wired together. The real runtime property suites (Property 7,
// 8, 9, 11, 12, 17, 19) land in later tasks under src/properties/.
describe("ts-runtime harness", () => {
  it("runs fast-check property assertions", () => {
    fc.assert(
      fc.property(fc.integer(), fc.integer(), (a, b) => a + b === b + a),
      { numRuns: 100 },
    );
  });

  it("resolves the generated-client directory from the environment", () => {
    const dir = resolveGeneratedClientDir();
    // Unset (the default in CI scaffolding) resolves to null; when set it is a real directory.
    expect(dir === null || typeof dir === "string").toBe(true);
    expect(GENERATED_CLIENT_DIR_ENV).toBe("VISTA_TS_CLIENT_DIR");
  });
});
