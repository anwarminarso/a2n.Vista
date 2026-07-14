// Feature: typescript-client, Property 11: Base-URL validation and transport-security posture
//
// Validates: Requirements 6.5, 7.6, 7.7
//
// This suite exercises the generated base-URL helpers (`validateBaseUrl`, `joinUrl`,
// `BaseUrlValidation` in `runtime/url.ts`) and the `ClientContext` construction guard
// (`runtime/client-context.ts`) against the *committed generated client* under
// `tests/ts-runtime/generated/` (see ../harness/generated.ts for how that fixture was produced).
//
// It asserts, over a wide, bucketed input space (minimum 100 runs), that:
//
//   1. `validateBaseUrl` classifies every base URL into the correct `kind`, cross-checked against
//      an independent oracle that mirrors the specification:
//        - absent / empty / whitespace / syntactically-invalid absolute URL -> "error" (R6.5)
//        - https://<any host>                                               -> "ok"
//        - non-HTTPS (http/ws/ftp/...) over a loopback host                 -> "warn" (R7.6)
//        - non-HTTPS over a non-loopback host                               -> "error" (R7.7)
//
//   2. `ClientContext` construction honors that classification (a RecordingTransport is supplied so
//      no real `fetch` is needed and construction never touches the network):
//        - "error" base URL  -> constructor throws AND issues NO request (fails before any send).
//        - "ok" / "warn"     -> construction succeeds; for "warn" a console.warn is emitted.
//
//   3. `joinUrl` composes base + path with exactly one `/` separator (task-adjacent R6.3 checks).

import { afterEach, describe, expect, it, vi } from "vitest";
import fc from "fast-check";

import { ClientContext, joinUrl, validateBaseUrl } from "../harness/generated.js";
import type { BaseUrlValidation } from "../harness/generated.js";
import { RecordingTransport } from "../harness/recording-transport.js";

// --- Independent oracle ------------------------------------------------------------------------

type ValidationKind = BaseUrlValidation["kind"];

// The loopback hosts permitted to use a non-HTTPS scheme. Written independently of the generated
// helper's private set so the test does not tautologically re-use the code under test. IPv6 `::1`
// is compared after stripping the surrounding brackets from `[::1]`.
const ORACLE_LOOPBACK_HOSTS = new Set<string>(["localhost", "127.0.0.1", "::1"]);

function oracleNormalizeHost(hostname: string): string {
  return hostname.startsWith("[") && hostname.endsWith("]")
    ? hostname.slice(1, -1)
    : hostname;
}

/**
 * Independent classification oracle mirroring the specified rules (R6.5/7.6/7.7). It reproduces the
 * ordering the generated helper applies — empty/invalid first, then HTTPS, then non-HTTPS split by
 * loopback host — using the platform `URL` parser but its own loopback set and normalization.
 */
function oracleKind(baseUrl: unknown): ValidationKind {
  if (typeof baseUrl !== "string" || baseUrl.trim().length === 0) {
    return "error";
  }

  let parsed: URL;
  try {
    parsed = new URL(baseUrl);
  } catch {
    return "error";
  }

  if (parsed.protocol === "https:") {
    return "ok";
  }

  const host = oracleNormalizeHost(parsed.hostname).toLowerCase();
  return ORACLE_LOOPBACK_HOSTS.has(host) ? "warn" : "error";
}

// --- Generators --------------------------------------------------------------------------------

// Bucket 1: absent/empty/whitespace/invalid -> "error".
const emptyOrWhitespaceArb: fc.Arbitrary<string> = fc.constantFrom(
  "",
  " ",
  "\t",
  "\n",
  "   ",
  "\r\n",
);

// Syntactically invalid absolute URLs (the global URL constructor throws on these).
const invalidUrlArb: fc.Arbitrary<string> = fc.oneof(
  fc.constantFrom(
    "not a url",
    "://missing-scheme",
    "http//missing-colon.example.com",
    "example.com/no-scheme",
    "/relative/path",
    "ht!tp://bad-scheme.example.com",
    "https://",
  ),
  // Bare tokens without a scheme separator are relative references, not absolute URLs.
  fc.stringMatching(/^[a-z]{1,12}$/),
);

// Hosts split into loopback and non-loopback buckets.
const loopbackHostArb: fc.Arbitrary<string> = fc.constantFrom(
  "localhost",
  "127.0.0.1",
  "[::1]",
  "LOCALHOST",
  "LocalHost",
);

const nonLoopbackHostArb: fc.Arbitrary<string> = fc.constantFrom(
  "example.com",
  "api.example.com",
  "192.168.1.10",
  "10.0.0.1",
  "sub.domain.co.uk",
  "0.0.0.0",
  "[2001:db8::1]",
  "vista.internal",
);

const nonHttpsSchemeArb: fc.Arbitrary<string> = fc.constantFrom(
  "http",
  "ws",
  "ftp",
  "wss",
  "gopher",
);

const optionalPortArb: fc.Arbitrary<string> = fc.constantFrom(
  "",
  ":80",
  ":443",
  ":5000",
  ":8080",
);

const optionalPathArb: fc.Arbitrary<string> = fc.constantFrom(
  "",
  "/",
  "/api",
  "/api/views",
  "/v1/",
);

// Bucket 2: https://<any host> -> "ok".
const okUrlArb: fc.Arbitrary<string> = fc
  .tuple(
    fc.oneof(loopbackHostArb, nonLoopbackHostArb),
    optionalPortArb,
    optionalPathArb,
  )
  .map(([host, port, path]) => `https://${host}${port}${path}`);

// Bucket 3: non-HTTPS + loopback host -> "warn".
const warnUrlArb: fc.Arbitrary<string> = fc
  .tuple(nonHttpsSchemeArb, loopbackHostArb, optionalPortArb, optionalPathArb)
  .map(([scheme, host, port, path]) => `${scheme}://${host}${port}${path}`);

// Bucket 4: non-HTTPS + non-loopback host -> "error".
const errorNonHttpsUrlArb: fc.Arbitrary<string> = fc
  .tuple(nonHttpsSchemeArb, nonLoopbackHostArb, optionalPortArb, optionalPathArb)
  .map(([scheme, host, port, path]) => `${scheme}://${host}${port}${path}`);

// The union covering every classification bucket, so a single run spreads across all four kinds.
const anyBaseUrlArb: fc.Arbitrary<string> = fc.oneof(
  emptyOrWhitespaceArb,
  invalidUrlArb,
  okUrlArb,
  warnUrlArb,
  errorNonHttpsUrlArb,
);

// --- Tests -------------------------------------------------------------------------------------

afterEach(() => {
  vi.restoreAllMocks();
});

describe("Property 11 — base-URL validation and transport-security posture", () => {
  it("validateBaseUrl classifies every base URL to the kind the oracle expects (R6.5/7.6/7.7)", () => {
    fc.assert(
      fc.property(anyBaseUrlArb, (baseUrl) => {
        const result = validateBaseUrl(baseUrl);

        // The kind always matches the independent oracle.
        expect(result.kind).toBe(oracleKind(baseUrl));

        // The classification payload is well-formed for each kind.
        switch (result.kind) {
          case "ok":
            expect(result.url).toBe(baseUrl);
            break;
          case "warn":
            expect(result.url).toBe(baseUrl);
            expect(typeof result.warning).toBe("string");
            expect(result.warning.length).toBeGreaterThan(0);
            break;
          case "error":
            expect(typeof result.error).toBe("string");
            expect(result.error.length).toBeGreaterThan(0);
            break;
          default:
            throw new Error(
              `validateBaseUrl produced an unexpected kind: ${(result as { kind: string }).kind}`,
            );
        }
      }),
      { numRuns: 100 },
    );
  });

  it("ClientContext construction honors the classification and issues no request when it fails (R6.5/7.7)", () => {
    fc.assert(
      fc.property(anyBaseUrlArb, (baseUrl) => {
        const kind = oracleKind(baseUrl);
        const transport = new RecordingTransport();
        // Silence and observe the loopback warning without polluting test output.
        const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

        try {
          if (kind === "error") {
            // An "error" base URL must fail construction before any request is issued.
            expect(() => new ClientContext({ baseUrl, transport })).toThrow();
            // No request may ever be routed through the transport (fails before any send).
            expect(transport.callCount).toBe(0);
          } else {
            // "ok" and "warn" base URLs complete construction.
            const context = new ClientContext({ baseUrl, transport });
            expect(context.baseUrl).toBe(baseUrl);
            expect(context.transport).toBe(transport);
            // Construction never issues a request on its own.
            expect(transport.callCount).toBe(0);

            if (kind === "warn") {
              // A non-HTTPS loopback base URL emits a warning but still constructs (R7.6).
              expect(warnSpy).toHaveBeenCalledTimes(1);
            } else {
              // A valid HTTPS base URL constructs cleanly with no warning.
              expect(warnSpy).not.toHaveBeenCalled();
            }
          }
        } finally {
          warnSpy.mockRestore();
        }
      }),
      { numRuns: 100 },
    );
  });

  it("joinUrl composes base + path with exactly one '/' separator (R6.3)", () => {
    // Canonical single-slash composition.
    expect(joinUrl("https://x/", "/a/b")).toBe("https://x/a/b");
    // Trailing slash on base and leading slash on path collapse to one.
    expect(joinUrl("https://x/", "a/b")).toBe("https://x/a/b");
    expect(joinUrl("https://x", "/a/b")).toBe("https://x/a/b");
    expect(joinUrl("https://x", "a/b")).toBe("https://x/a/b");
    // Multiple redundant slashes at the join collapse to one; internal segments are preserved.
    expect(joinUrl("https://x///", "///a/b")).toBe("https://x/a/b");
    expect(joinUrl("https://x/api/", "/views/customers")).toBe(
      "https://x/api/views/customers",
    );
    // An empty (or slash-only) path yields the base with any trailing slashes stripped.
    expect(joinUrl("https://x/", "")).toBe("https://x");
    expect(joinUrl("https://x/", "/")).toBe("https://x");

    // Property: for any base and path, the join never introduces a `//` at the seam and always
    // preserves the path's internal, non-empty segments in order.
    const segmentArb = fc.stringMatching(/^[a-z0-9]{1,8}$/);
    fc.assert(
      fc.property(
        fc.array(segmentArb, { minLength: 1, maxLength: 5 }),
        fc.boolean(),
        fc.boolean(),
        (segments, baseTrailingSlash, pathLeadingSlash) => {
          const base = `https://host.example.com${baseTrailingSlash ? "/" : ""}`;
          const path = `${pathLeadingSlash ? "/" : ""}${segments.join("/")}`;
          const joined = joinUrl(base, path);

          // Exactly one slash at the seam: the scheme's `://` is the only `//` present.
          const withoutScheme = joined.slice("https://".length);
          expect(withoutScheme.includes("//")).toBe(false);

          // The composed URL ends with the path's segments joined by single slashes.
          expect(joined).toBe(`https://host.example.com/${segments.join("/")}`);
        },
      ),
      { numRuns: 100 },
    );
  });
});
