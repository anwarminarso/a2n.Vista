import { defineConfig } from "vitest/config";

// The runtime property tests (fast-check) target the emitted TypeScript client.
// The generated output directory is supplied out-of-band via the VISTA_TS_CLIENT_DIR
// environment variable so the same harness can run against any generated output.
export default defineConfig({
  test: {
    include: ["src/**/*.test.ts"],
    environment: "node",
    // Property-based suites can take longer than the default per-test budget.
    testTimeout: 30_000,
  },
});
