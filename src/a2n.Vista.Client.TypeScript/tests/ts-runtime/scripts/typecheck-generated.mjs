// Type-checks a generated TypeScript client (a `Generated_Output` directory)
// without emitting. The target directory is supplied via the VISTA_TS_CLIENT_DIR
// environment variable so the same gate runs against any generated output.
//
// Behaviour:
//   - variable unset .......... nothing to check yet; exit 0 with a notice
//   - variable set, missing ... exit 1 (the caller expected a generated client)
//   - variable set, present ... run `tsc --noEmit` over the directory
//
// Usage: node scripts/typecheck-generated.mjs

import { existsSync, mkdtempSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const ENV = "VISTA_TS_CLIENT_DIR";
const raw = process.env[ENV];

if (raw === undefined || raw.trim() === "") {
  console.log(
    `[typecheck:generated] ${ENV} is not set — skipping (no generated client to check yet).`,
  );
  process.exit(0);
}

const generatedDir = resolve(raw.trim());
if (!existsSync(generatedDir) || !statSync(generatedDir).isDirectory()) {
  console.error(
    `[typecheck:generated] ${ENV} points at "${generatedDir}", which is not an existing directory.`,
  );
  process.exit(1);
}

const harnessDir = resolve(fileURLToPath(new URL("..", import.meta.url)));

// The generated client is checked with the same strict compiler options as the
// harness itself, but with its own include set (the generated directory).
const compilerOptions = {
  target: "ES2022",
  module: "ESNext",
  moduleResolution: "Bundler",
  lib: ["ES2022", "DOM"],
  strict: true,
  esModuleInterop: true,
  skipLibCheck: true,
  forceConsistentCasingInFileNames: true,
  noEmit: true,
};

const stagingDir = mkdtempSync(join(tmpdir(), "vista-ts-generated-check-"));
const configPath = join(stagingDir, "tsconfig.generated.json");

try {
  // TypeScript's `include` globs are matched with forward slashes on every platform. On Windows
  // `path.join` yields backslashes, which tsc does not treat as glob separators (the `**/*.ts`
  // pattern silently matches nothing). Normalize to forward slashes so the generated directory is
  // actually type-checked. This is the documented harness-side fix for the backslash glob bug.
  const includeGlob = join(generatedDir, "**/*.ts").replace(/\\/g, "/");

  writeFileSync(
    configPath,
    JSON.stringify(
      {
        compilerOptions,
        include: [includeGlob],
      },
      null,
      2,
    ),
    "utf8",
  );

  // Invoke the TypeScript compiler's JS entry point directly through the current Node executable.
  // Spawning the `.bin/tsc.cmd` shim on Windows via spawnSync without a shell fails to launch (the
  // process never starts and `status` comes back null), so we resolve the package's own `bin/tsc`
  // script and run it with `process.execPath`, which is portable across platforms.
  const tscJs = join(harnessDir, "node_modules", "typescript", "bin", "tsc");

  console.log(`[typecheck:generated] Checking generated client at "${generatedDir}"...`);
  const result = spawnSync(process.execPath, [tscJs, "--noEmit", "-p", configPath], {
    cwd: harnessDir,
    stdio: "inherit",
  });

  if (result.error) {
    console.error(`[typecheck:generated] Failed to run tsc: ${result.error.message}`);
    process.exit(1);
  }

  process.exit(result.status ?? 1);
} finally {
  rmSync(stagingDir, { recursive: true, force: true });
}
