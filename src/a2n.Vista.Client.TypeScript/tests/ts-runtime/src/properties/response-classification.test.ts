// Feature: typescript-client, Property 7: Response classification is total and correct (never throws)
//
// Validates: Requirements 4.5, 5.6, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6
//
// This suite exercises the generated response classifier (`classifyResponse` in
// `runtime/result.ts`) against the *committed generated client* under `tests/ts-runtime/generated/`
// (see ../harness/generated.ts for how that fixture was produced). It asserts two things across a
// wide, adversarial input space:
//
//   1. Totality — `classifyResponse` NEVER throws, for any { status, contentType, body }. It always
//      returns a value whose `kind` is a member of the `ClientResult` union (Requirement 8.4).
//   2. Correctness of the discriminant — the returned `kind` (and its status-specialized payload)
//      matches the specification, cross-checked against an independent oracle:
//        - 2xx + parseable body                       -> success (8.1)
//        - 2xx + parseSuccess throws                  -> unexpected(status, rawBody) (8.5)
//        - application/problem+json (case-insensitive,
//          ignoring ;charset) + parseable object      -> problem, or the status-specialized
//                                                        not-found(404)/precondition-required(428)/
//                                                        precondition-failed(409) (8.2, 4.5, 5.6)
//        - problem+json + unparseable/non-object body -> unexpected(status, rawBody) (8.6)
//        - non-2xx, non-problem+json                  -> unexpected(status, rawBody) (8.3)
//
// The `parseSuccess` used here is `JSON.parse`, which may throw — that is deliberate, to drive the
// 8.5 (2xx parse-failure) path. Minimum 100 runs (fast-check `numRuns: 100`).

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import { classifyResponse } from "../harness/generated.js";
import type { ClassifiableResponse, ClientResult } from "../harness/generated.js";

// Every discriminant the ClientResult union can carry. `classifyResponse` only ever produces the
// subset {success, problem, not-found, precondition-required, precondition-failed, unexpected}; the
// remaining two (unauthorized, transport-error) are produced by the client, not the classifier.
const ALL_CLIENT_RESULT_KINDS = new Set<string>([
  "success",
  "problem",
  "unauthorized",
  "not-found",
  "precondition-required",
  "precondition-failed",
  "transport-error",
  "unexpected",
]);

const CLASSIFIER_KINDS = new Set<string>([
  "success",
  "problem",
  "not-found",
  "precondition-required",
  "precondition-failed",
  "unexpected",
]);

// The success-body parser under test: JSON.parse, which throws on invalid JSON (exercises 8.5).
const parseSuccess = (body: string): unknown => JSON.parse(body) as unknown;

/**
 * Independent oracle mirroring the specified problem-JSON media-type test (Requirement 8.2):
 * case-insensitive, ignoring media-type parameters such as `;charset`. A null content type is not
 * problem JSON. Written independently of the generated helper so the test does not tautologically
 * re-use the code under test.
 */
function oracleIsProblemJson(contentType: string | null): boolean {
  if (contentType === null) {
    return false;
  }
  const first = contentType.split(";", 1)[0] ?? "";
  return first.trim().toLowerCase() === "application/problem+json";
}

type ExpectedOutcome =
  | { readonly kind: "success"; readonly value: unknown }
  | { readonly kind: "problem"; readonly status: number }
  | { readonly kind: "not-found"; readonly status: 404 }
  | { readonly kind: "precondition-required"; readonly status: 428 }
  | { readonly kind: "precondition-failed"; readonly status: 409 }
  | { readonly kind: "unexpected"; readonly status: number; readonly rawBody: string };

/**
 * The independent classification oracle. It reproduces the specified ordering — 2xx first, then
 * problem+json by status, then the catch-all — using the same `JSON.parse`-based success parser and
 * the same "problem body must be a non-array JSON object" rule the generated classifier applies.
 */
function expectedOutcome(response: ClassifiableResponse): ExpectedOutcome {
  const { status, contentType, body } = response;

  if (status >= 200 && status <= 299) {
    try {
      return { kind: "success", value: parseSuccess(body) };
    } catch {
      return { kind: "unexpected", status, rawBody: body };
    }
  }

  if (oracleIsProblemJson(contentType)) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(body) as unknown;
    } catch {
      return { kind: "unexpected", status, rawBody: body };
    }
    if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
      return { kind: "unexpected", status, rawBody: body };
    }
    switch (status) {
      case 404:
        return { kind: "not-found", status: 404 };
      case 428:
        return { kind: "precondition-required", status: 428 };
      case 409:
        return { kind: "precondition-failed", status: 409 };
      default:
        return { kind: "problem", status };
    }
  }

  return { kind: "unexpected", status, rawBody: body };
}

// --- Generators ---------------------------------------------------------------------------------

// Full HTTP status range, biased to include the classifier-significant codes (2xx, 404, 409, 428).
const statusArb: fc.Arbitrary<number> = fc.oneof(
  fc.integer({ min: 100, max: 599 }),
  fc.constantFrom(200, 201, 204, 299, 400, 401, 404, 409, 428, 500),
);

// Content types: problem+json in various casings/with parameters, near-misses, other types, null.
const contentTypeArb: fc.Arbitrary<string | null> = fc.oneof(
  fc.constant(null),
  fc.constantFrom(
    "application/problem+json",
    "application/problem+json; charset=utf-8",
    "APPLICATION/PROBLEM+JSON",
    "Application/Problem+Json; charset=UTF-8",
    "  application/problem+json  ",
    "application/json",
    "application/json; charset=utf-8",
    "text/plain",
    "text/plain; charset=utf-8",
    "application/problem+jsonx", // near-miss: must NOT be treated as problem+json
    "",
  ),
  fc.string(),
);

// Bodies: JSON objects, arbitrary valid JSON (arrays/scalars), free strings (often invalid JSON),
// and hand-picked edge cases including a realistic ProblemDetails object.
const jsonValueArb = fc.jsonValue();
const bodyArb: fc.Arbitrary<string> = fc.oneof(
  fc.dictionary(fc.string(), jsonValueArb).map((o) => JSON.stringify(o)),
  jsonValueArb.map((v) => JSON.stringify(v)),
  fc.string(),
  fc.constantFrom(
    "",
    "not json at all",
    "{ unclosed",
    "[1,2,3]",
    "null",
    "42",
    '"a string"',
    "true",
    "{}",
    '{"type":"about:blank","title":"Not Found","status":404,"code":"VIEW_KEY_NOT_FOUND"}',
  ),
);

const responseArb: fc.Arbitrary<ClassifiableResponse> = fc.record({
  status: statusArb,
  contentType: contentTypeArb,
  body: bodyArb,
});

// --- Property -----------------------------------------------------------------------------------

describe("Property 7 — response classification is total and correct (never throws)", () => {
  it("classifies every response into a valid, correct ClientResult without throwing", () => {
    fc.assert(
      fc.property(responseArb, (response) => {
        // (1) Totality: classifyResponse must not throw for ANY input.
        const result: ClientResult<unknown> = classifyResponse(response, parseSuccess);

        // The result always carries a valid ClientResult discriminant, and specifically one the
        // classifier is allowed to produce.
        expect(ALL_CLIENT_RESULT_KINDS.has(result.kind)).toBe(true);
        expect(CLASSIFIER_KINDS.has(result.kind)).toBe(true);

        // (2) Correctness: the discriminant (and its status-specialized payload) matches the oracle.
        const expected = expectedOutcome(response);
        expect(result.kind).toBe(expected.kind);

        switch (result.kind) {
          case "success":
            // 8.1: 2xx parseable body -> success carrying the parsed value.
            expect(expected.kind).toBe("success");
            if (expected.kind === "success") {
              expect(result.value).toEqual(expected.value);
            }
            break;

          case "problem":
            // 8.2: problem+json (non-specialized status) -> problem with the HTTP status + body.
            expect(result.status).toBe(response.status);
            expect(typeof result.problem).toBe("object");
            expect(result.problem).not.toBeNull();
            break;

          case "not-found":
            // 4.5: documented 404 problem -> not-found carrying the literal 404 status.
            expect(result.status).toBe(404);
            expect(response.status).toBe(404);
            expect(typeof result.problem).toBe("object");
            break;

          case "precondition-required":
            // 5.6: 428 problem -> distinct precondition-required carrying the literal 428 status.
            expect(result.status).toBe(428);
            expect(response.status).toBe(428);
            expect(typeof result.problem).toBe("object");
            break;

          case "precondition-failed":
            // 5.6: 409 problem -> distinct precondition-failed carrying the literal 409 status.
            expect(result.status).toBe(409);
            expect(response.status).toBe(409);
            expect(typeof result.problem).toBe("object");
            break;

          case "unexpected":
            // 8.3/8.5/8.6: catch-all -> unexpected carrying the HTTP status and the raw body verbatim.
            expect(result.status).toBe(response.status);
            expect(result.rawBody).toBe(response.body);
            break;

          default:
            // unauthorized / transport-error are never produced by the classifier.
            throw new Error(`classifier produced an unexpected kind: ${(result as { kind: string }).kind}`);
        }
      }),
      { numRuns: 100 },
    );
  });
});
