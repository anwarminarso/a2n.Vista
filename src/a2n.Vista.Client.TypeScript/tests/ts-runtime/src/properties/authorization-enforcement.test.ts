// Feature: typescript-client, Property 8: Authorization enforcement
//
// Validates: Requirements 7.2, 7.3, 7.4, 7.5
//
// This suite exercises the *secure-by-default* authorization behavior of the generated client
// against the committed generated clients under `tests/ts-runtime/`:
//   - the primary, fully-secured client (`generated/`, imported via ../harness/generated.js), whose
//     Customers facets are secured at the document level, and
//   - a second, anonymous client (`generated-anon/`, imported via ../harness/generated-anon.js),
//     produced from a copy of the fixture with the top-level `security` + `securitySchemes` removed,
//     which is the only way to exercise the anonymous path (Requirement 7.5).
//
// A `RecordingTransport` (the in-memory HttpTransport test double) lets each property assert exactly
// *what the client sent* (the credential header, or the absence of one) and *whether it sent at all*
// (the short-circuit before send). fast-check varies tokens, custom header names/values, and which
// secured facet is driven, across >= 100 runs (numRuns: 100). Predicates are async
// (`fc.asyncProperty`) because every facet call is awaited.
//
// The four covered clauses:
//   7.2 SECURED + provider yields a credential  -> the credential header is attached to the sent
//       request, and the request IS sent (callCount === 1).
//   7.3 SECURED + NO provider                   -> `unauthorized`, and the request is NOT sent
//       (callCount === 0): short-circuit before send.
//   7.4 SECURED + provider yields null / throws -> `unauthorized`, and the request is NOT sent.
//   7.5 ANONYMOUS document                      -> the request is sent WITHOUT any Authorization
//       header, and the auth provider is never consulted.

import { describe, expect, it } from "vitest";
import fc from "fast-check";

import {
  bearerAuth,
  ClientContext,
  CustomersClient,
} from "../harness/generated.js";
import type {
  AuthCredential,
  AuthProvider,
  ClientResult,
  OperationInfo,
} from "../harness/generated.js";
import * as anon from "../harness/generated-anon.js";
import { makeResponse, RecordingTransport } from "../harness/recording-transport.js";

// A valid HTTPS base URL so ClientContext construction never fails for base-URL reasons (that is
// Property 11's concern, not this one). All requests compose against this.
const BASE_URL = "https://api.example.com";

// A benign 200 response the transport returns for the cases where the request IS sent; every facet's
// success parser accepts an empty JSON object (`export` preserves it raw), so the outcome is `success`.
const OK_RESPONSE = makeResponse({ status: 200, body: "{}" });

// The four read facets present on the Customers view in both fixtures (Requirement 4.1). All are
// secured in the primary fixture and anonymous in the anonymous fixture.
type ReadFacet = "list" | "detail" | "metadata" | "export";
const facetArb: fc.Arbitrary<ReadFacet> = fc.constantFrom("list", "detail", "metadata", "export");

// A structural view of the per-view client shared by the primary and anonymous clients (both emit an
// identical Customers surface). Lets one helper drive either client without coupling to a class.
interface CustomersLike {
  list(body: Record<string, never>): Promise<ClientResult<unknown>>;
  detail(body: { key: unknown }): Promise<ClientResult<unknown>>;
  metadata(): Promise<ClientResult<unknown>>;
  export(body: Record<string, never>): Promise<ClientResult<unknown>>;
}

/**
 * Invokes the chosen read facet with a minimal valid argument. `list`/`export` take an (all-optional)
 * `VistaListRequestBody` — `{}` suffices; `detail` requires a `key`; `metadata` takes no argument.
 */
function callFacet(client: CustomersLike, facet: ReadFacet): Promise<ClientResult<unknown>> {
  switch (facet) {
    case "list":
      return client.list({});
    case "detail":
      return client.detail({ key: "acme" });
    case "metadata":
      return client.metadata();
    case "export":
      return client.export({});
  }
}

/** Reads a header case-insensitively from a recorded request's header bag. */
function header(headers: Readonly<Record<string, string>>, name: string): string | undefined {
  const lower = name.toLowerCase();
  for (const [key, value] of Object.entries(headers)) {
    if (key.toLowerCase() === lower) {
      return value;
    }
  }
  return undefined;
}

// --- 7.2 — secured operation with an available credential attaches it and sends -----------------

describe("Property 8 — authorization enforcement (secured: credential attach, 7.2)", () => {
  it("attaches the bearer Authorization header and sends when a token is available", () => {
    // Any non-empty token yields a credential; assert it appears verbatim as `Bearer <token>`.
    const tokenArb = fc.string({ minLength: 1 }).filter((t) => t !== "");

    return fc.assert(
      fc.asyncProperty(facetArb, tokenArb, async (facet, token) => {
        const transport = new RecordingTransport(OK_RESPONSE);
        const ctx = new ClientContext({
          baseUrl: BASE_URL,
          transport,
          auth: bearerAuth(() => token),
        });
        const client = new CustomersClient(ctx);

        const result = await callFacet(client, facet);

        // The request WAS sent, exactly once, carrying the bearer credential verbatim.
        expect(transport.callCount).toBe(1);
        expect(header(transport.onlyRequest.headers, "Authorization")).toBe(`Bearer ${token}`);
        // A benign 200 body classifies as success (never unauthorized) once the credential attaches.
        expect(result.kind).toBe("success");
      }),
      { numRuns: 100 },
    );
  });

  it("attaches an arbitrary custom credential header returned by the provider and sends", () => {
    // The default scheme is bearer/Authorization, but an AuthProvider may return any header; the
    // client attaches exactly what the provider yields. Exclude Content-Type so it cannot collide
    // with the JSON body header the client sets for POST facets.
    const headerNameArb = fc.constantFrom("Authorization", "X-Api-Key", "X-Auth-Token", "Authentication");
    const headerValueArb = fc.string({ minLength: 1 }).filter((v) => v !== "");

    return fc.assert(
      fc.asyncProperty(
        facetArb,
        headerNameArb,
        headerValueArb,
        async (facet, headerName, headerValue) => {
          const seen: OperationInfo[] = [];
          const provider: AuthProvider = {
            getCredential(op: OperationInfo): Promise<AuthCredential | null> {
              seen.push(op);
              return Promise.resolve({ headerName, headerValue });
            },
          };

          const transport = new RecordingTransport(OK_RESPONSE);
          const ctx = new ClientContext({ baseUrl: BASE_URL, transport, auth: provider });
          const client = new CustomersClient(ctx);

          const result = await callFacet(client, facet);

          // The provider was consulted for a secured operation and its credential was attached.
          expect(seen.length).toBe(1);
          expect(seen[0]?.secured).toBe(true);
          expect(seen[0]?.facet).toBe(facet);
          expect(transport.callCount).toBe(1);
          expect(header(transport.onlyRequest.headers, headerName)).toBe(headerValue);
          expect(result.kind).toBe("success");
        },
      ),
      { numRuns: 100 },
    );
  });
});

// --- 7.3 — secured operation with no provider short-circuits before sending ---------------------

describe("Property 8 — authorization enforcement (secured, no provider: 7.3)", () => {
  it("returns unauthorized and does NOT send when no auth provider is supplied", () => {
    return fc.assert(
      fc.asyncProperty(facetArb, async (facet) => {
        const transport = new RecordingTransport(OK_RESPONSE);
        // No `auth` supplied: a secured operation cannot obtain a credential.
        const ctx = new ClientContext({ baseUrl: BASE_URL, transport });
        const client = new CustomersClient(ctx);

        const result = await callFacet(client, facet);

        expect(result.kind).toBe("unauthorized");
        // Short-circuit: nothing reached the transport.
        expect(transport.callCount).toBe(0);
      }),
      { numRuns: 100 },
    );
  });
});

// --- 7.4 — provider yields null or throws: short-circuit before sending -------------------------

describe("Property 8 — authorization enforcement (secured, no/failed credential: 7.4)", () => {
  it("returns unauthorized and does NOT send when the provider yields null", () => {
    // Cover both the explicit null provider and the default bearer provider with an empty token
    // (bearerAuth maps a nullish/empty token to `null`, i.e. no credential available).
    type NullProvider = "explicit-null" | "bearer-empty-token" | "bearer-null-token";
    const providerArb = fc.constantFrom<NullProvider>(
      "explicit-null",
      "bearer-empty-token",
      "bearer-null-token",
    );

    return fc.assert(
      fc.asyncProperty(facetArb, providerArb, async (facet, kind) => {
        const provider: AuthProvider =
          kind === "explicit-null"
            ? { getCredential: () => Promise.resolve(null) }
            : kind === "bearer-empty-token"
              ? bearerAuth(() => "")
              : bearerAuth(() => null as unknown as string);

        const transport = new RecordingTransport(OK_RESPONSE);
        const ctx = new ClientContext({ baseUrl: BASE_URL, transport, auth: provider });
        const client = new CustomersClient(ctx);

        const result = await callFacet(client, facet);

        expect(result.kind).toBe("unauthorized");
        expect(transport.callCount).toBe(0);
      }),
      { numRuns: 100 },
    );
  });

  it("returns unauthorized and does NOT send when the provider throws", () => {
    return fc.assert(
      fc.asyncProperty(facetArb, fc.string(), async (facet, message) => {
        const provider: AuthProvider = {
          getCredential(): Promise<AuthCredential | null> {
            throw new Error(message);
          },
        };

        const transport = new RecordingTransport(OK_RESPONSE);
        const ctx = new ClientContext({ baseUrl: BASE_URL, transport, auth: provider });
        const client = new CustomersClient(ctx);

        const result = await callFacet(client, facet);

        expect(result.kind).toBe("unauthorized");
        expect(transport.callCount).toBe(0);
      }),
      { numRuns: 100 },
    );
  });
});

// --- 7.5 — anonymous document sends without a credential and never consults the provider --------

describe("Property 8 — authorization enforcement (anonymous document: 7.5)", () => {
  it("sends without any Authorization header and never calls the auth provider", () => {
    // The anonymous client is generated from a fixture with no declared security; its facets are
    // classified anonymous, so the client sends the request directly.
    return fc.assert(
      fc.asyncProperty(facetArb, fc.string({ minLength: 1 }), async (facet, token) => {
        // A provider that would attach a credential AND records every call — to prove it is never
        // consulted for an anonymous operation even when one is supplied.
        let providerCalls = 0;
        const provider: AuthProvider = {
          getCredential(): Promise<AuthCredential | null> {
            providerCalls += 1;
            return Promise.resolve({ headerName: "Authorization", headerValue: `Bearer ${token}` });
          },
        };

        const transport = new RecordingTransport(OK_RESPONSE);
        const ctx = new anon.ClientContext({ baseUrl: BASE_URL, transport, auth: provider });
        const client: CustomersLike = new anon.CustomersClient(ctx);

        const result = await callFacet(client, facet);

        // The request was sent (no short-circuit) but carried no authorization credential...
        expect(transport.callCount).toBe(1);
        expect(header(transport.onlyRequest.headers, "Authorization")).toBeUndefined();
        // ...and the provider was never consulted for the anonymous operation.
        expect(providerCalls).toBe(0);
        expect(result.kind).toBe("success");
      }),
      { numRuns: 100 },
    );
  });
});
