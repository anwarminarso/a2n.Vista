# a2n.Vista TypeScript client

This directory is a generated, framework-agnostic TypeScript client for an a2n.Vista API. It was produced from the API's OpenAPI document and contains the request/response types, the polymorphic `FilterNode` filter tree, the RFC 7807 `ProblemDetails` type, a small runtime (transport, auth, result, URL helpers), and one typed client class per view. Do not edit these files by hand: regenerating overwrites them.

The client imports no UI framework and no grid library. It talks to the server only through an HTTP transport you inject (or the default `fetch`-backed one), so it runs in any TypeScript runtime.

## Importing

Everything is re-exported from the barrel (`index.ts`), so a single import path reaches the context, the runtime helpers, and every view client:

```ts
import { ClientContext, CustomersClient, bearerAuth } from "./generated";
```

## Constructing a client

Create one `ClientContext` and share it across the view clients. The context validates the base URL at construction and holds the transport and the optional auth provider.

```ts
const ctx = new ClientContext({
  baseUrl: "https://api.example.com",
  // transport is optional; when omitted the default fetch-backed transport is used.
  // auth is optional; supply it only when the API declares secured operations.
  auth: bearerAuth(() => getAccessToken()),
});

const customers = new CustomersClient(ctx);
```

- **`baseUrl`** (required) — the API root. Each request path is joined to it with exactly one `/`.
- **`transport`** (optional) — any `HttpTransport`. Omit it to use the default backed by the platform global `fetch`; use `createFetchTransport()` if you want construction to fail fast when `fetch` is unavailable, or `fetchTransport` for the lazy singleton.
- **`auth`** (optional) — an `AuthProvider`. `bearerAuth(tokenProvider)` attaches `Authorization: Bearer <token>` to each secured request. The client never embeds a credential; you always supply the token.

## Calling read facets

Each view client exposes exactly the read facets the API declares for that view. Absent facets are simply not emitted. Every method returns a `Promise<ClientResult<T>>` and never throws.

```ts
// list: POST {route}/list — a typed request body, a paged result.
const listed = await customers.list({ page: 0, pageSize: 20 });
if (listed.kind === "success") {
  for (const row of listed.value.page.items) {
    // row is fully typed
  }
}

// detail: POST {route}/detail — a typed key, a single row (404 -> "not-found").
const detailed = await customers.detail({ key: 1 });

// metadata: GET {route}/metadata — no argument, the view's field metadata.
const meta = await customers.metadata();

// export: POST {route}/export — a typed format union; the body is the raw payload.
const exported = await customers.export({ format: "csv" });
```

## Write facets

Write facets are **gated off by default**, so no `create`, `update`, or `delete` method is emitted on any view client. To adopt the write surface deliberately, regenerate the client with write-facet generation enabled; writable views then expose typed `create`/`update`/`delete` methods that return a `ClientResult<T>` and never throw.

## Handling results

Every operation returns a single discriminated union, `ClientResult<T>`. Read the `kind` field to handle the outcome — you never inspect the HTTP status directly and never catch an exception for an HTTP or parse failure.

```ts
const result = await customers.list({ page: 0, pageSize: 20 });
switch (result.kind) {
  case "success":
    // result.value is the typed success payload
    break;
  case "problem":
    // RFC 7807 body in result.problem, HTTP status in result.status
    break;
  case "not-found":            // 404, a typed ProblemDetails
  case "unauthorized":         // no credential available; request was not sent
  case "precondition-required": // 428, missing concurrency token
  case "precondition-failed":   // 409, stale concurrency token
  case "transport-error":       // the transport rejected; no retry was performed
  case "unexpected":            // non-2xx / undecodable body; raw body preserved
    break;
}
```

## Secure by default

- **No embedded credential.** The generated output contains no token or secret. You supply credentials through the `AuthProvider`; secured requests without one short-circuit to a typed `unauthorized` result and are never sent.
- **HTTPS by default.** A non-HTTPS base URL to a loopback host (`localhost`, `127.0.0.1`, `::1`) warns and continues; a non-HTTPS base URL to any other host is rejected at construction and no request is issued.
- **Anonymous APIs.** When the API declares no secured operations, requests are sent without a credential and the auth provider is never consulted.
