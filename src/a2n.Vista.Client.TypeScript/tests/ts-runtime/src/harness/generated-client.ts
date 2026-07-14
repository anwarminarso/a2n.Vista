import { existsSync, statSync } from "node:fs";
import { resolve } from "node:path";

/**
 * Environment variable naming the directory that holds a `Generated_Output`
 * (the emitted TypeScript client: `index.ts`, `types.ts`, `filter-node.ts`,
 * `runtime/`, `views/`, `README.md`).
 *
 * The harness is intentionally decoupled from the generator: point this at any
 * generated output directory to run the runtime property tests against it.
 */
export const GENERATED_CLIENT_DIR_ENV = "VISTA_TS_CLIENT_DIR";

/**
 * Resolves the configured generated-output directory.
 *
 * @returns the absolute path when the variable is set and the directory exists,
 *          otherwise `null`.
 */
export function resolveGeneratedClientDir(): string | null {
  const raw = process.env[GENERATED_CLIENT_DIR_ENV];
  if (raw === undefined || raw.trim() === "") {
    return null;
  }

  const dir = resolve(raw.trim());
  if (!existsSync(dir) || !statSync(dir).isDirectory()) {
    return null;
  }

  return dir;
}

/**
 * Convenience guard for property suites that require the generated client.
 * Returns the resolved directory or throws a descriptive error naming the
 * environment variable to set.
 */
export function requireGeneratedClientDir(): string {
  const dir = resolveGeneratedClientDir();
  if (dir === null) {
    throw new Error(
      `No generated client found. Set ${GENERATED_CLIENT_DIR_ENV} to a directory ` +
        `containing the emitted TypeScript client (index.ts, types.ts, runtime/, views/).`,
    );
  }
  return dir;
}
