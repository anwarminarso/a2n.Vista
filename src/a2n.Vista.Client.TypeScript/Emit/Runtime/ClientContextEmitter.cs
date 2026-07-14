namespace a2n.Vista.Client.TypeScript.Emit.Runtime;

/// <summary>
/// Emits <c>runtime/client-context.ts</c> — the framework-agnostic composition root the per-view clients
/// (task 10.6) consume. The emitted <c>ClientContext</c> carries the validated base URL, the resolved
/// <c>HttpTransport</c>, the optional <c>AuthProvider</c>, and the seam through which per-operation security
/// metadata (an <c>OperationInfo</c>) flows to the provider. It validates the base URL at construction and,
/// on failure, throws before resolving a transport so no request can be issued (Requirements 6.3, 6.5, 7.6,
/// 7.7, 6.7).
/// </summary>
/// <remarks>
/// <para>
/// The file content is a fixed template — it does not vary with the document — so the emitter is a pure
/// function of no input and its output is byte-for-byte identical on every run and operating system
/// (Requirement 9.1). The authored source is normalized to a single <c>\n</c> line terminator with a single
/// trailing newline, regardless of how this C# file is stored on disk.
/// </para>
/// <para>
/// The emitted module composes the sibling runtime files via relative imports and imports no UI, grid, or
/// server package (Requirements 6.4, 12.5, 12.6):
/// </para>
/// <list type="bullet">
///   <item><description><c>./http-transport</c> — <c>HttpTransport</c> (type) and <c>createFetchTransport</c>.</description></item>
///   <item><description><c>./auth</c> — <c>AuthProvider</c>, <c>AuthCredential</c>, <c>OperationInfo</c> (types).</description></item>
///   <item><description><c>./url</c> — <c>validateBaseUrl</c> and <c>joinUrl</c>.</description></item>
/// </list>
/// <para>
/// <b>Construction seam (Requirements 6.5/7.6/7.7).</b> The constructor runs <c>validateBaseUrl</c> first and
/// maps its discriminated outcome: <c>error</c> -&gt; throw (construction fails, no request issued);
/// <c>warn</c> -&gt; <c>console.warn(warning)</c> and continue; <c>ok</c> -&gt; continue. Only after the base
/// URL is accepted does it resolve the transport, so an invalid base URL never reaches the transport.
/// </para>
/// <para>
/// <b>Transport resolution (Requirements 6.2/6.7).</b> When no transport is supplied, the context calls
/// <c>createFetchTransport()</c>, which throws <c>FetchUnavailableError</c> eagerly when the platform global
/// <c>fetch</c> is unavailable — so a context constructed without a transport fails at construction time in a
/// runtime that cannot perform requests, rather than on the first call.
/// </para>
/// <para>
/// <b>Shape the view clients consume.</b> The per-view clients need four things, all exposed here:
/// </para>
/// <list type="bullet">
///   <item><description><c>transport</c> — the resolved <c>HttpTransport</c> to route each request through (Requirement 6.1).</description></item>
///   <item><description><c>resolveUrl(path)</c> — composes an absolute request URL via <c>joinUrl(baseUrl, path)</c> with exactly one <c>/</c> separator (Requirement 6.3).</description></item>
///   <item><description><c>auth</c> — the optional <c>AuthProvider</c> (or <c>null</c>).</description></item>
///   <item><description><c>getCredential(operation)</c> — the per-operation credential seam. The view client passes the operation's <c>OperationInfo</c> (which carries the emit-time <c>secured</c> metadata); a <c>null</c> result means "no credential available", which the view client surfaces as a typed unauthorized failure without sending (Requirements 7.3, 7.4). An anonymous document simply never calls this for its operations (Requirement 7.5).</description></item>
/// </list>
/// <para>
/// No credential, token, or secret value is embedded in the emitted file (Requirement 7.1); the context only
/// holds the consumer-supplied provider.
/// </para>
/// </remarks>
public static class ClientContextEmitter
{
    /// <summary>The output-directory-relative path of the emitted file (forward-slash separators).</summary>
    public const string RelativePath = "runtime/client-context.ts";

    /// <summary>
    /// Produces the buffered <see cref="GeneratedFile"/> for <c>runtime/client-context.ts</c> with a fixed
    /// <c>\n</c> line terminator and a single trailing newline, independent of the host operating system
    /// (Requirement 9.1).
    /// </summary>
    /// <returns>The emitted <c>runtime/client-context.ts</c> file.</returns>
    public static GeneratedFile Emit() => new(RelativePath, Content);

    /// <summary>The complete, normalized (<c>\n</c>) TypeScript source for the client-context runtime file.</summary>
    private static string Content { get; } = Normalize(Source);

    /// <summary>
    /// Normalizes any line terminator that may have crept in from the source-file checkout (a CRLF on
    /// Windows) to the single fixed <c>\n</c> the generator emits, and guarantees exactly one trailing
    /// newline, so the emitted bytes are identical everywhere (Requirement 9.1).
    /// </summary>
    private static string Normalize(string text)
    {
        var lf = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        return lf.EndsWith('\n') ? lf : lf + "\n";
    }

    // The literal is written flush-left so the raw string strips no indentation; the TypeScript's own
    // 2-space indentation is preserved verbatim. English comments only (published artifact).
    private const string Source =
"""
// <auto-generated>
//   This file is generated by the a2n.Vista TypeScript client generator. Do not edit by hand.
//   It defines the ClientContext: the framework-agnostic composition root the per-view clients share.
// </auto-generated>

import { createFetchTransport } from "./http-transport";
import type { HttpTransport } from "./http-transport";
import type { AuthCredential, AuthProvider, OperationInfo } from "./auth";
import { joinUrl, validateBaseUrl } from "./url";

/**
 * Construction options for a {@link ClientContext}.
 *
 * The context never embeds a credential (Requirement 7.1): the consumer supplies an optional
 * `AuthProvider`, and requests for an anonymous document are sent without one.
 */
export interface ClientContextOptions {
  /** The base URL every request is composed against. Validated at construction (Requirements 6.5/7.7). */
  readonly baseUrl: string;
  /**
   * The transport every request is routed through (Requirement 6.1). When omitted, the context resolves
   * the default `fetch`-backed transport and fails construction if `fetch` is unavailable (Requirement 6.7).
   */
  readonly transport?: HttpTransport;
  /**
   * The provider consulted for a credential on each secured operation. When omitted, a secured operation
   * yields no credential and the view client surfaces a typed unauthorized failure (Requirement 7.3).
   */
  readonly auth?: AuthProvider;
}

/**
 * The composition root shared by the generated per-view clients. It carries the validated base URL, the
 * resolved transport, and the optional auth provider, and it is the single seam through which per-operation
 * security metadata (an {@link OperationInfo}) flows to the provider.
 *
 * Construction fails — and no request is ever issued — when the base URL is invalid (Requirements 6.5/7.7)
 * or when no transport is supplied and the platform `fetch` is unavailable (Requirement 6.7).
 */
export class ClientContext {
  private readonly _baseUrl: string;
  private readonly _transport: HttpTransport;
  private readonly _auth: AuthProvider | null;

  /**
   * Validates the base URL, then resolves the transport. The base URL is checked first so an invalid value
   * throws before a transport is resolved and before any request can be issued:
   * - `error` -> throw (construction fails; Requirements 6.5/7.7).
   * - `warn`  -> `console.warn(warning)` and continue (non-HTTPS loopback; Requirement 7.6).
   * - `ok`    -> continue (Requirement 6.3).
   *
   * When no transport is supplied, `createFetchTransport()` resolves the default `fetch`-backed transport
   * and throws `FetchUnavailableError` when `fetch` is unavailable (Requirement 6.7).
   */
  constructor(options: ClientContextOptions) {
    const validation = validateBaseUrl(options.baseUrl);
    if (validation.kind === "error") {
      throw new Error(validation.error);
    }
    if (validation.kind === "warn") {
      console.warn(validation.warning);
    }

    this._baseUrl = validation.url;
    this._transport = options.transport ?? createFetchTransport();
    this._auth = options.auth ?? null;
  }

  /** The validated base URL every request is composed against. */
  get baseUrl(): string {
    return this._baseUrl;
  }

  /** The resolved transport every request is routed through (Requirement 6.1). */
  get transport(): HttpTransport {
    return this._transport;
  }

  /** The auth provider, or `null` when the consumer supplied none. */
  get auth(): AuthProvider | null {
    return this._auth;
  }

  /** Whether an auth provider is available to obtain credentials for secured operations. */
  hasAuthProvider(): boolean {
    return this._auth !== null;
  }

  /**
   * Composes an absolute request URL by joining the base URL with an operation path using exactly one `/`
   * separator (Requirement 6.3).
   */
  resolveUrl(path: string): string {
    return joinUrl(this._baseUrl, path);
  }

  /**
   * Obtains the credential to attach to a secured operation, or `null` when none is available.
   *
   * Intended for secured operations: the view client passes the operation's `OperationInfo` (which carries
   * the emit-time `secured` metadata). A `null` result — whether because no provider was supplied
   * (Requirement 7.3) or because the provider yielded no credential (Requirement 7.4) — signals the view
   * client to surface a typed unauthorized failure without sending the request. An anonymous document never
   * calls this for its operations (Requirement 7.5).
   */
  async getCredential(operation: OperationInfo): Promise<AuthCredential | null> {
    if (this._auth === null) {
      return null;
    }
    return this._auth.getCredential(operation);
  }
}
""";
}
