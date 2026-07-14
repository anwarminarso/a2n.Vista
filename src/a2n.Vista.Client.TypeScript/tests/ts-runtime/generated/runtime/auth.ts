// runtime/auth.ts
// Authorization contracts for the generated Vista client (secure by default).
//
// Framework-agnostic: this module imports no UI, grid, or transport package. The consumer supplies
// an AuthProvider; the generated client never embeds a credential, token, or secret (Requirement
// 7.1). For an anonymous document (no declared security scheme) the client sends requests without a
// credential and never calls the provider (Requirement 7.5).

/**
 * A minimal description of the operation a credential is being requested for. It lets an
 * AuthProvider vary the credential per view or facet, or by security posture, without coupling to
 * the transport or the generated view clients.
 *
 * Defined here and re-exported by the client so the runtime modules (client context and the
 * per-view clients) share a single OperationInfo shape.
 */
export interface OperationInfo {
  /** The view the operation belongs to, exactly as named by the document. */
  readonly view: string;
  /**
   * The facet suffix of the operation, e.g. "list" | "detail" | "metadata" | "export"
   * | "create" | "update" | "delete".
   */
  readonly facet: string;
  /** Whether the operation is secured. Anonymous operations are `false` (Requirement 7.5). */
  readonly secured: boolean;
}

/**
 * A credential to attach to a secured request as a single HTTP header. The default bearer scheme
 * produces `Authorization: Bearer <token>` (Requirement 7.2).
 */
export interface AuthCredential {
  /** The header name to set. Default: "Authorization". */
  readonly headerName: string;
  /** The header value to set. Default: "Bearer <token>". */
  readonly headerValue: string;
}

/**
 * The consumer-supplied hook the client calls to obtain a credential for a secured operation.
 *
 * Returning `null` signals "no credential available": the client then surfaces a typed unauthorized
 * failure and does not send the request (Requirement 7.4). A secured operation with no provider is
 * treated the same way (Requirement 7.3). An anonymous document never triggers this call at all
 * (Requirement 7.5).
 */
export interface AuthProvider {
  /** Returns a credential to attach, or `null` when no credential is available. */
  getCredential(operation: OperationInfo): Promise<AuthCredential | null>;
}

/** The default HTTP header name for the bearer scheme (Requirement 7.2). */
export const AUTHORIZATION_HEADER = "Authorization";

/**
 * Builds the default bearer AuthProvider. For each secured operation it calls the supplied token
 * provider and yields `Authorization: Bearer <token>` (Requirement 7.2). A nullish or empty token
 * yields `null` (no credential available), which the client surfaces as a typed unauthorized failure
 * without sending the request (Requirement 7.4). No token value is embedded in the generated output
 * (Requirement 7.1).
 *
 * @param tokenProvider Returns the bearer token (or a promise of it) for the current request.
 */
export function bearerAuth(
  tokenProvider: () => string | Promise<string>,
): AuthProvider {
  return {
    async getCredential(_operation: OperationInfo): Promise<AuthCredential | null> {
      const token = await tokenProvider();
      if (token === null || token === undefined || token === "") {
        return null;
      }
      return {
        headerName: AUTHORIZATION_HEADER,
        headerValue: `Bearer ${token}`,
      };
    },
  };
}
