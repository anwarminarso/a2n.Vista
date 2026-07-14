// Feature: typescript-client, Property 19: Export format union and raw payload preservation
//
// Validates: Requirements 4.7
//
// This suite covers the *runtime side* of Property 19 against the committed generated client under
// `tests/ts-runtime/generated/` (see ../harness/generated.ts for how that fixture was produced).
// The generator side (the `format` literal union equalling the document's declared export formats)
// is covered separately by task 9.4; here we assert the client's `export` facet preserves the
// response body verbatim.
//
// Requirement 4.7 — the `export` operation's SUCCESS result preserves the response body verbatim as
// raw, unparsed text: no JSON parsing, no transformation, no trimming. Unlike `list`/`detail`, whose
// success parsers `JSON.parse` the body into a typed object, `export`'s success parser is the
// identity `(raw) => raw`, so `RawPayload` is just the body `string`.
//
// A fresh `RecordingTransport` per run returns a 2xx response whose body is an ARBITRARY string
// (CSV, XML, binary-ish text, JSON-looking text, empty, unicode, very long, ...). We call
// `CustomersClient.export({})` through a secured `ClientContext` (bearerAuth, since the fixture is
// secured) and assert:
//   - result.kind === "success" for any 2xx body regardless of content/content-type (even non-JSON
//     bodies succeed — the raw-payload behavior, contrasted with list/detail which JSON.parse), and
//   - result.value === the exact response body string the transport returned (byte-for-byte).
//   - The Content-Type header is varied (text/csv, application/octet-stream, application/json, none)
//     and the body is preserved verbatim in every case for a 2xx export.
//   - A non-2xx export response does NOT classify as success (raw preservation is success-path only).
//
// Predicates are async (`fc.asyncProperty`) because the facet call is awaited. Minimum 100 runs
// (fast-check `numRuns: 100`).

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import { bearerAuth, ClientContext, CustomersClient } from "../harness/generated.js";
import type { ClientResult, RawPayload } from "../harness/generated.js";
import { makeResponse, RecordingTransport } from "../harness/recording-transport.js";

// A valid HTTPS base URL so ClientContext construction never fails for base-URL reasons.
const BASE_URL = "https://api.example.com";

// A non-empty token so bearerAuth always yields a credential (the fixture's Customers view is
// secured; without a credential `export` would short-circuit to `unauthorized` and never send).
const TOKEN = "export-token";

/** Builds a secured export client backed by the given recording transport. */
function makeExportClient(transport: RecordingTransport): CustomersClient {
  const ctx = new ClientContext({
    baseUrl: BASE_URL,
    transport,
    auth: bearerAuth(() => TOKEN),
  });
  return new CustomersClient(ctx);
}

// --- Generators ---------------------------------------------------------------------------------

// Arbitrary export bodies, biased to include the tricky, format-diverse cases R4.7 cares about:
// empty, CSV, JSON-looking (which must NOT be parsed), unicode, and free-form text. fast-check's
// `fullUnicodeString` adds arbitrary code points (including surrogate pairs, control chars, etc.).
const trickyBodies = [
  "", // empty body
  "a,b,c\n1,2,3", // CSV
  "a,b,c\r\n1,2,3\r\n4,5,6", // CSV with CRLF
  '{"not":"parsed"}', // JSON-looking: export must return it verbatim, NOT the parsed object
  "[1,2,3]", // JSON array text: still verbatim
  "<root><row id=\"1\"/></root>", // XML
  "café — π ★ 日本語 😀", // unicode with multi-byte / astral chars
  "\u0000\u0001\u0002 binary-ish \uFFFD", // binary-ish / control chars
  "   leading and trailing spaces   ", // must NOT be trimmed
  "\n\n\n", // whitespace-only, must NOT be trimmed
  "PK\u0003\u0004 spreadsheet-ish bytes", // ZIP/xlsx magic-ish text
] as const;

const bodyArb: fc.Arbitrary<string> = fc.oneof(
  fc.constantFrom(...trickyBodies),
  fc.string(),
  fc.fullUnicodeString(),
  // Very long strings to exercise large payloads.
  fc.string({ minLength: 2000, maxLength: 5000 }),
);

// Content types the export endpoint might return; `null` means the header is absent entirely.
const contentTypeArb: fc.Arbitrary<string | null> = fc.constantFrom(
  "text/csv",
  "text/csv; charset=utf-8",
  "application/octet-stream",
  "application/json",
  "application/json; charset=utf-8",
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  "text/plain",
  null,
);

// 2xx statuses the export success path accepts.
const okStatusArb: fc.Arbitrary<number> = fc.constantFrom(200, 201, 202, 203, 206, 299);

/** Builds a canned 2xx response with the given body and (optional) content-type header. */
function ok2xx(status: number, contentType: string | null, body: string) {
  const headers: Record<string, string> = contentType === null ? {} : { "content-type": contentType };
  return makeResponse({ status, headers, body });
}

// --- Property 19 (runtime, R4.7): 2xx export preserves the body verbatim as raw text ------------

describe("Property 19 — export success preserves the response body as raw, unparsed text (R4.7)", () => {
  it("returns success whose value is the exact response body, byte-for-byte, for any 2xx body", () => {
    return fc.assert(
      fc.asyncProperty(okStatusArb, contentTypeArb, bodyArb, async (status, contentType, body) => {
        // Fresh transport per run (no cross-run state).
        const transport = new RecordingTransport(ok2xx(status, contentType, body));
        const client = makeExportClient(transport);

        const result: ClientResult<RawPayload> = await client.export({});

        // (1) A 2xx export always succeeds — even for non-JSON bodies (the raw-payload contract,
        // contrasted with list/detail which JSON.parse and would degrade to `unexpected`).
        expect(result.kind).toBe("success");

        // (2) The core R4.7 assertion: the success value IS the response body, verbatim. No parsing,
        // no transformation, no trimming — a strict, byte-for-byte string identity.
        if (result.kind === "success") {
          expect(result.value).toBe(body);
          // RawPayload is the body `string`; confirm no accidental deserialization happened.
          expect(typeof result.value).toBe("string");
        }

        // The request went through the transport exactly once (no retry).
        expect(transport.callCount).toBe(1);
      }),
      { numRuns: 100 },
    );
  });

  it("preserves a JSON-looking body verbatim instead of returning the parsed object", () => {
    // A focused, deterministic guard: the string must be preserved, NOT the parsed value.
    return fc.assert(
      fc.asyncProperty(
        fc.dictionary(fc.string(), fc.string()),
        contentTypeArb,
        async (obj, contentType) => {
          const body = JSON.stringify(obj);
          const transport = new RecordingTransport(ok2xx(200, contentType, body));
          const client = makeExportClient(transport);

          const result = await client.export({});

          expect(result.kind).toBe("success");
          if (result.kind === "success") {
            // Verbatim string, not the deserialized object.
            expect(result.value).toBe(body);
            expect(result.value).not.toEqual(obj as unknown);
          }
        },
      ),
      { numRuns: 100 },
    );
  });
});

// --- Raw preservation is success-path only: a non-2xx export is NOT a success -------------------

describe("Property 19 — non-2xx export does not classify as success (R4.7, success-path scope)", () => {
  it("classifies any non-2xx export response as a failure kind, never success", () => {
    // Non-2xx statuses spanning problem-mapped (404/409/428), generic problem, and non-problem.
    const nonOkStatusArb = fc.constantFrom(400, 401, 404, 409, 428, 500, 503);
    // A problem+json content type drives the specialized/problem kinds; other types drive unexpected.
    const failContentTypeArb = fc.constantFrom(
      "application/problem+json",
      "application/problem+json; charset=utf-8",
      "text/csv",
      "application/octet-stream",
      null,
    );

    return fc.assert(
      fc.asyncProperty(nonOkStatusArb, failContentTypeArb, bodyArb, async (status, contentType, body) => {
        const headers: Record<string, string> =
          contentType === null ? {} : { "content-type": contentType };
        const transport = new RecordingTransport(makeResponse({ status, headers, body }));
        const client = makeExportClient(transport);

        const result = await client.export({});

        // Raw preservation is specifically the success path; a non-2xx export is never `success`.
        expect(result.kind).not.toBe("success");
        expect(transport.callCount).toBe(1);
      }),
      { numRuns: 100 },
    );
  });
});
