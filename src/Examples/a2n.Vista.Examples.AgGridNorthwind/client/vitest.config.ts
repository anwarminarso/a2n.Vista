import { defineConfig } from "vitest/config";

// Property-based tests (fast-check) for the pure client transforms live under `tests/`, kept out of
// `src/` so the `tsc`/`tsc --noEmit` build (which only includes `src/**` and emits to ../wwwroot/js)
// never compiles or ships them.
export default defineConfig({
  test: {
    include: ["tests/**/*.test.ts"],
    environment: "node",
    // Property-based suites can exceed the default per-test budget.
    testTimeout: 30_000,
  },
});
