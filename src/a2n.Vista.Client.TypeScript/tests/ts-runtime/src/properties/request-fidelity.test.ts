// Feature: typescript-client, Property 12: Request fidelity — only what the document declares
//
// Validates: Requirements 4.2, 13.4
//
// This suite exercises the *committed generated client* under `tests/ts-runtime/generated/`
// (see ../harness/generated.ts) and asserts that each generated view operation sends EXACTLY the
// method + path + media type the document declares — and nothing more or other. Fidelity is checked
// through a `RecordingTransport` (../harness/recording-transport.ts): the transport captures the
// fully composed `HttpRequest` the client routed, so the test can assert on precisely what the
// client put on the wire.
//
// For each facet present on the generated `CustomersClient` we assert, across >= 100 fast-check
// runs with varied request bodies where applicable:
//   - the HTTP method matches the document (POST for list/detail/export, GET for metadata),
//   - the composed URL is the base URL joined with the declared path via exactly one `/`,
//   - the request body is byte-for-byte `JSON.stringify(input)` (or absent for the bodyless GET),
//   - the media type is `application/json` only when a body is present (never on the GET), and
//   - the request carries ONLY the expected headers — `Content-Type` when a body is present plus
//     the `Authorization` header the secured operation requires — with no extra headers, and
//   - EXACTLY ONE request is recorded per call (no duplicate sends).
//
// NOTE ON FACET COVERAGE: the committed generated fixture's `CustomersClient` exposes only the four
// read facets (list, detail, metadata, export); it declares no create/update/delete methods, so the
// document the fixture was generated from does not expose write facets on this view. The write
// facets are therefore not present to exercise here; this suite covers every facet the generated
// client actually declares.

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import { ClientContext, CustomersClient, bearerAuth } from "../harness/generated.js";
import type { HttpRequest, VistaDetailRequestBody, VistaListRequestBody } from "../harness/generated.js";
import { RecordingTransport, makeResponse } from "../harness/recording-transport.js";

// --- Fixture constants (must match how the committed client was generated) ----------------------

const BASE_URL = "https://api.example.com";
const TOKEN = "test-token";
const AUTHORIZATION_VALUE = `Bearer ${TOKEN}`;
const JSON_MEDIA_TYPE = "application/json";

// The declared path for each present facet, exactly as emitted by the generator.
const PATHS = {
  list: "/api/views/customers/list",
  detail: "/api/views/customers/detail",
  metadata: "/api/views/customers/metadata",
  export: "/api/views/customers/export",
} as const;

// --- Per-run scaffolding ------------------------------------------------------------------------

/**
 * Builds a fresh {@link RecordingTransport} (returning a benign, well-formed 200) and a
 * {@link CustomersClient} over a {@link ClientContext} for the secured fixture. A fresh transport
 * per call keeps recorded requests from accumulating across fast-check runs.
 */
function makeClient(): { transport: RecordingTransport; client: CustomersClient } {
  const transport = new RecordingTransport(
    makeResponse({ status: 200, headers: { "content-type": JSON_MEDIA_TYPE }, body: "{}" }),
  );
  const ctx = new ClientContext({
    baseUrl: BASE_URL,
    transport,
    auth: bearerAuth(() => TOKEN),
  });
  return { transport, client: new CustomersClient(ctx) };
}

/** Returns the request's header names, sorted, for exact-set comparison. */
function headerNames(request: HttpRequest): string[] {
  return Object.keys(request.headers).sort();
}

/**
 * Asserts the shared fidelity invariants for a facet that sends a JSON body: exactly one request,
 * the declared POST method + single-slash URL, the verbatim serialized body, the `application/json`
 * media type, and ONLY the `Authorization` + `Content-Type` headers (nothing more).
 */
function assertBodyFacetFidelity(transport: RecordingTransport, path: string, serializedBody: string): void {
  expect(transport.callCount).toBe(1);
  const request = transport.onlyRequest;

  // Method + path + single-slash join.
  expect(request.method).toBe("POST");
  expect(request.url).toBe(`${BASE_URL}${path}`);
  expect(request.url.endsWith(path)).toBe(true);

  // Body is byte-for-byte the serialized input.
  expect(request.body).toBe(serializedBody);

  // Media type is application/json, present because a body is present.
  expect(request.headers["Content-Type"]).toBe(JSON_MEDIA_TYPE);

  // Authorization is attached for the secured operation.
  expect(request.headers["Authorization"]).toBe(AUTHORIZATION_VALUE);

  // ONLY the expected headers — no extras.
  expect(headerNames(request)).toEqual(["Authorization", "Content-Type"]);
}

// --- Generators ---------------------------------------------------------------------------------

// A varied VistaListRequestBody: random page/pageSize plus optional search/format. Every field is
// optional in the document, so an empty object is also a valid value; requiredKeys: [] admits it.
const listBodyArb: fc.Arbitrary<VistaListRequestBody> = fc.record(
  {
    page: fc.integer({ min: 0, max: 100_000 }),
    pageSize: fc.integer({ min: 1, max: 1_000 }),
    search: fc.option(fc.string(), { nil: undefined }),
    format: fc.option(fc.string(), { nil: undefined }),
  },
  { requiredKeys: [] },
);

// A varied VistaDetailRequestBody: the `key` is `unknown`, so exercise string, numeric, and object
// keys to prove the body is serialized verbatim regardless of the key's shape.
const detailBodyArb: fc.Arbitrary<VistaDetailRequestBody> = fc.record({
  key: fc.oneof(
    fc.string(),
    fc.integer(),
    fc.record({ id: fc.integer(), region: fc.string() }),
  ),
});

// --- Properties ---------------------------------------------------------------------------------

describe("Property 12 — request fidelity (only what the document declares)", () => {
  it("list sends POST .../list with application/json and the verbatim serialized body", async () => {
    await fc.assert(
      fc.asyncProperty(listBodyArb, async (body) => {
        const { transport, client } = makeClient();
        await client.list(body);
        assertBodyFacetFidelity(transport, PATHS.list, JSON.stringify(body));
      }),
      { numRuns: 100 },
    );
  });

  it("detail sends POST .../detail with application/json and the verbatim serialized key body", async () => {
    await fc.assert(
      fc.asyncProperty(detailBodyArb, async (body) => {
        const { transport, client } = makeClient();
        await client.detail(body);
        assertBodyFacetFidelity(transport, PATHS.detail, JSON.stringify(body));
      }),
      { numRuns: 100 },
    );
  });

  it("export sends POST .../export with application/json and the verbatim serialized body", async () => {
    await fc.assert(
      fc.asyncProperty(listBodyArb, async (body) => {
        const { transport, client } = makeClient();
        await client.export(body);
        assertBodyFacetFidelity(transport, PATHS.export, JSON.stringify(body));
      }),
      { numRuns: 100 },
    );
  });

  it("metadata sends GET .../metadata with no body and no Content-Type", async () => {
    await fc.assert(
      // The metadata facet takes no argument; the throwaway integer only varies the run.
      fc.asyncProperty(fc.integer(), async () => {
        const { transport, client } = makeClient();
        await client.metadata();

        expect(transport.callCount).toBe(1);
        const request = transport.onlyRequest;

        // Method + path + single-slash join.
        expect(request.method).toBe("GET");
        expect(request.url).toBe(`${BASE_URL}${PATHS.metadata}`);
        expect(request.url.endsWith(PATHS.metadata)).toBe(true);

        // A bodyless GET carries no body and no Content-Type media type.
        expect(request.body).toBeUndefined();
        expect(request.headers["Content-Type"]).toBeUndefined();

        // Authorization is attached for the secured operation, and it is the ONLY header.
        expect(request.headers["Authorization"]).toBe(AUTHORIZATION_VALUE);
        expect(headerNames(request)).toEqual(["Authorization"]);
      }),
      { numRuns: 100 },
    );
  });
});
