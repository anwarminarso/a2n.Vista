// Shared scaffolding for the 14.x/15.x generated-runtime property tests.
//
// A `RecordingTransport` is an in-memory `HttpTransport` (the generated transport seam) that:
//   - records every `HttpRequest` the client routed through it (order preserved), and
//   - answers each `send` with a queued or canned `HttpResponse` (status, headers, body),
// so a test can assert on *what the client sent* (Property 12/17: method, path, media type, and
// that each request goes through the transport exactly once with no retry) and drive *what the
// client receives* (Property 7/8: response classification and auth enforcement).
//
// It performs no network I/O and never retries — it is a pure test double. A companion
// `RejectingTransport` models a transport that rejects/throws (Requirement 6.6, Property 17's
// transport-error path).

import type {
  HttpRequest,
  HttpResponse,
  HttpTransport,
} from "./generated.js";

/**
 * Options for building a canned {@link HttpResponse}. Only `status` is required; `headers` and
 * `body` default to an empty header set and an empty body.
 */
export interface CannedResponse {
  readonly status: number;
  readonly headers?: Readonly<Record<string, string>>;
  readonly body?: string;
}

/**
 * Builds a fully-formed {@link HttpResponse} from a {@link CannedResponse}, filling in the default
 * empty headers and empty body. A convenience so tests can enqueue `{ status: 200, body: "..." }`.
 */
export function makeResponse(canned: CannedResponse): HttpResponse {
  return {
    status: canned.status,
    headers: canned.headers ?? {},
    body: canned.body ?? "",
  };
}

/**
 * Builds an `application/problem+json` {@link HttpResponse} for a given status and body. The
 * content type carries a `charset` parameter on purpose, so tests exercise the media-type
 * parameter-stripping in the classifier (Requirement 8.2).
 */
export function makeProblemResponse(status: number, body: string): HttpResponse {
  return {
    status,
    headers: { "content-type": "application/problem+json; charset=utf-8" },
    body,
  };
}

/**
 * A recording, queue-driven {@link HttpTransport} test double.
 *
 * Enqueue responses in the order the client will consume them; each `send` shifts the next queued
 * response, or falls back to the configured default response, or (if neither is set) throws to make
 * an unmet expectation obvious. Every request is captured in {@link requests}.
 */
export class RecordingTransport implements HttpTransport {
  /** Every request routed through this transport, in the order it was sent. */
  readonly requests: HttpRequest[] = [];

  private readonly queue: HttpResponse[] = [];
  private defaultResponse: HttpResponse | null = null;

  /**
   * @param defaultResponse an optional response returned once the queue is drained. When omitted,
   *        `send` throws if it is called with an empty queue (an unmet test expectation).
   */
  constructor(defaultResponse?: HttpResponse) {
    this.defaultResponse = defaultResponse ?? null;
  }

  /** Queues a response to be returned by a subsequent `send`. Returns `this` for chaining. */
  enqueue(response: HttpResponse): this {
    this.queue.push(response);
    return this;
  }

  /** Queues a canned response (shorthand for `enqueue(makeResponse(canned))`). */
  enqueueCanned(canned: CannedResponse): this {
    return this.enqueue(makeResponse(canned));
  }

  /** Sets the fallback response returned when the queue is empty. Returns `this` for chaining. */
  respondWith(response: HttpResponse): this {
    this.defaultResponse = response;
    return this;
  }

  /** The number of requests recorded so far. */
  get callCount(): number {
    return this.requests.length;
  }

  /** The single recorded request; throws if zero or more than one request was sent. */
  get onlyRequest(): HttpRequest {
    if (this.requests.length !== 1) {
      throw new Error(
        `Expected exactly one recorded request, but found ${this.requests.length}.`,
      );
    }
    // Length is exactly 1 here, so the element is defined despite noUncheckedIndexedAccess.
    return this.requests[0] as HttpRequest;
  }

  /** Records the request and returns the next queued (or default) response. Never retries. */
  send(request: HttpRequest): Promise<HttpResponse> {
    this.requests.push(request);
    const next = this.queue.shift() ?? this.defaultResponse;
    if (next === null) {
      throw new Error(
        "RecordingTransport received a request but has no queued or default response to return.",
      );
    }
    return Promise.resolve(next);
  }
}

/**
 * An {@link HttpTransport} that always rejects, modeling a transport-level failure (a network error
 * or a throwing custom transport). The client must surface this as a `transport-error` result with
 * no retry (Requirement 6.6). Requests are still recorded so a test can assert exactly one attempt.
 */
export class RejectingTransport implements HttpTransport {
  /** Every request routed through this transport, in the order it was attempted. */
  readonly requests: HttpRequest[] = [];

  /** @param error the value the transport rejects with (defaults to an `Error`). */
  constructor(private readonly error: unknown = new Error("transport rejected")) {}

  /** The number of send attempts recorded so far. */
  get callCount(): number {
    return this.requests.length;
  }

  send(request: HttpRequest): Promise<HttpResponse> {
    this.requests.push(request);
    return Promise.reject(this.error);
  }
}
