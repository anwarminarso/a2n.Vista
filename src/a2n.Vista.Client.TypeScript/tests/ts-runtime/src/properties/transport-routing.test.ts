// Feature: typescript-client, Property 17: Every request is routed through the transport exactly once, with no retry
//
// Validates: Requirements 6.1, 6.6
//
// This suite exercises the *committed generated client* under `tests/ts-runtime/generated/` (see
// ../harness/generated.ts for how that fixture was produced) to prove two universal properties of
// the transport seam across every CustomersClient facet and a wide space of response shapes:
//
//   1. ROUTING EXACTLY ONCE (Requirement 6.1) — every operation issues EXACTLY ONE `send` through
//      the injected transport, regardless of the response status/kind (2xx, 4xx problem+json, 5xx,
//      arbitrary bodies). The client performs no direct network I/O beyond the injected seam. We
//      assert `transport.callCount === 1` after each awaited call.
//
//   2. NO RETRY ON TRANSPORT REJECTION (Requirement 6.6) — when the transport rejects/throws, the
//      client surfaces a `transport-error` ClientResult (it does NOT throw / reject), and records
//      EXACTLY ONE attempt — no retry, no partial success. We assert the returned promise resolves
//      to `{ kind: "transport-error" }` and `transport.callCount === 1`.
//
// A fresh transport is constructed per fast-check run so the counts are strictly per-operation. The
// context is given a `bearerAuth` provider with a non-empty token so the secured facets actually
// reach the transport (a missing/empty credential would short-circuit to `unauthorized` and send
// nothing — not what this property tests). Minimum 100 runs (fast-check `numRuns: 100`).

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import {
  ClientContext,
  CustomersClient,
  bearerAuth,
} from "../harness/generated.js";
import type { ClientResult } from "../harness/generated.js";
import {
  RecordingTransport,
  RejectingTransport,
  makeResponse,
  makeProblemResponse,
} from "../harness/recording-transport.js";
import type { HttpResponse } from "../harness/generated.js";

// The four CustomersClient facets, each invoked with a minimal, valid request body. Every entry is
// a thunk that, given a client, awaits the facet call and returns its ClientResult. Keeping the
// invocation behind a thunk lets fast-check pick a facet per run while the transport (and thus the
// per-operation call count) is created fresh each time.
type FacetInvoker = {
  readonly name: string;
  readonly invoke: (client: CustomersClient) => Promise<ClientResult<unknown>>;
};

const FACETS: readonly FacetInvoker[] = [
  { name: "detail", invoke: (c) => c.detail({ key: "ALFKI" }) },
  { name: "export", invoke: (c) => c.export({ page: 1, pageSize: 25 }) },
  { name: "list", invoke: (c) => c.list({ page: 1, pageSize: 25 }) },
  { name: "metadata", invoke: (c) => c.metadata() },
];

const facetArb: fc.Arbitrary<FacetInvoker> = fc.constantFrom(...FACETS);

// A spread of response shapes the transport can answer with: 2xx (parseable + unparseable bodies),
// problem+json at specialized and generic statuses, plain non-2xx, and odd bodies. Routing must be
// exactly once regardless of which of these the transport returns.
const responseArb: fc.Arbitrary<HttpResponse> = fc.oneof(
  // 2xx with a parseable JSON body -> success path.
  fc.record({
    status: fc.constantFrom(200, 201, 204, 299),
    body: fc.constantFrom("{}", '{"page":{"items":[]}}', '"raw"', "42", "[]"),
  }).map(({ status, body }) => makeResponse({ status, body })),
  // 2xx with an unparseable body -> success-parse failure degrades to unexpected (still one send).
  fc.constantFrom(200, 201).map((status) => makeResponse({ status, body: "not json" })),
  // problem+json at specialized statuses -> not-found / precondition-required / precondition-failed.
  fc.constantFrom(404, 428, 409).map((status) =>
    makeProblemResponse(status, `{"type":"about:blank","status":${status}}`),
  ),
  // problem+json at a generic failure status -> problem.
  fc.constantFrom(400, 403, 422, 500).map((status) =>
    makeProblemResponse(status, `{"type":"about:blank","status":${status}}`),
  ),
  // plain non-2xx, non-problem body -> unexpected.
  fc.record({
    status: fc.constantFrom(400, 401, 500, 503),
    body: fc.constantFrom("", "oops", "<html>error</html>"),
  }).map(({ status, body }) => makeResponse({ status, body })),
);

// Non-empty bearer tokens so the secured facets obtain a credential and actually send.
const tokenArb: fc.Arbitrary<string> = fc.constantFrom("t0ken", "abc.def.ghi", "secret");

const BASE_URL = "https://api.example.com";

describe("Property 17 — every request is routed through the transport exactly once, with no retry", () => {
  it("routes each facet through the injected transport exactly once for any response (Requirement 6.1)", async () => {
    await fc.assert(
      fc.asyncProperty(facetArb, responseArb, tokenArb, async (facet, response, token) => {
        // Fresh transport per run so callCount is strictly per-operation.
        const transport = new RecordingTransport(response);
        const ctx = new ClientContext({
          baseUrl: BASE_URL,
          transport,
          auth: bearerAuth(() => token),
        });
        const client = new CustomersClient(ctx);

        const result = await facet.invoke(client);

        // Exactly one send per operation, regardless of the response status/kind (routing seam).
        expect(transport.callCount).toBe(1);
        // The single recorded request targets the injected base URL (routed through the seam, no
        // direct network I/O that bypasses the transport).
        expect(transport.onlyRequest.url.startsWith(BASE_URL)).toBe(true);
        // A response was classified into some ClientResult without throwing; on this path the client
        // never short-circuits to unauthorized (a credential is available) or transport-error (the
        // transport resolved), so it must reach the classifier.
        expect(result.kind).not.toBe("unauthorized");
        expect(result.kind).not.toBe("transport-error");
      }),
      { numRuns: 100 },
    );
  });

  it("surfaces a transport-error with exactly one attempt and no retry on rejection (Requirement 6.6)", async () => {
    // The value the transport rejects with is varied to show the surfacing is independent of it.
    const rejectionArb: fc.Arbitrary<unknown> = fc.oneof(
      fc.constant(new Error("network down")),
      fc.constant(new TypeError("Failed to fetch")),
      fc.string(),
      fc.constant(undefined),
      fc.constant(null),
    );

    await fc.assert(
      fc.asyncProperty(facetArb, rejectionArb, tokenArb, async (facet, rejection, token) => {
        // Fresh rejecting transport per run so the attempt count is strictly per-operation.
        const transport = new RejectingTransport(rejection);
        const ctx = new ClientContext({
          baseUrl: BASE_URL,
          transport,
          auth: bearerAuth(() => token),
        });
        const client = new CustomersClient(ctx);

        // The client must NOT throw: the rejection is surfaced as a resolved ClientResult. If the
        // facet promise rejected, awaiting it here would throw and fail the property.
        const result = await facet.invoke(client);

        // The transport rejection is surfaced as a typed transport-error (not thrown).
        expect(result.kind).toBe("transport-error");
        // Exactly one attempt was recorded — no retry, no partial success.
        expect(transport.callCount).toBe(1);
      }),
      { numRuns: 100 },
    );
  });
});
