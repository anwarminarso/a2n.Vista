namespace a2n.Vista.Client.TypeScript.Emit.Runtime;

/// <summary>
/// Emits <c>runtime/auth.ts</c> — the framework-agnostic authorization contracts of the generated client
/// (design §B.2): the <c>AuthCredential</c> and <c>AuthProvider</c> interfaces, the minimal
/// <c>OperationInfo</c> the provider is called with, and the default <c>bearerAuth</c> helper that yields an
/// <c>Authorization: Bearer &lt;token&gt;</c> header (Requirement 7.2).
/// </summary>
/// <remarks>
/// <para>
/// This emitter owns exactly one file so it never collides with the sibling runtime emitters
/// (<c>http-transport.ts</c>, <c>result.ts</c>, <c>url.ts</c>, <c>client-context.ts</c>). The content is a
/// fixed template — it does not vary with the document — so the output is trivially deterministic
/// (Requirement 9.1). The emitted TypeScript imports no UI or grid package and no transport (Requirements
/// 6.4, 12.5, 12.6); it defines only the auth contracts.
/// </para>
/// <para>
/// <b>OperationInfo.</b> The design §B.2 signature <c>getCredential(operation: OperationInfo)</c> references
/// an <c>OperationInfo</c> shape the design does not itself declare. It is defined here (the auth file is
/// self-contained) and exported so the downstream runtime modules — client-context (task 10.5) and the
/// per-view clients (task 10.6) — share this single shape rather than redeclaring it. It is kept minimal:
/// the view name, the facet suffix, and whether the operation is secured.
/// </para>
/// <para>
/// <b>Secure by default.</b> No token, secret, or credential value is embedded in the emitted file
/// (Requirement 7.1); <c>bearerAuth</c> takes a consumer-supplied token provider. An anonymous document (no
/// security scheme) simply never causes the client to call the provider (Requirement 7.5) — that decision
/// lives in the view clients/client-context; this file only provides the contracts and the default bearer
/// implementation.
/// </para>
/// </remarks>
public static class AuthEmitter
{
    /// <summary>The output-directory-relative path of the emitted file (forward-slash separators).</summary>
    public const string RelativePath = "runtime/auth.ts";

    // The fixed file body. Authored with whatever line endings this source file happens to use; Emit()
    // normalizes to a single '\n' terminator so the output is byte-identical on every OS (Requirement 9.1).
    private const string Template = """
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

        """;

    /// <summary>
    /// Builds the <see cref="GeneratedFile"/> for <c>runtime/auth.ts</c>. The content is fixed and
    /// deterministic, with a single trailing newline and <c>\n</c> line terminators throughout
    /// (Requirement 9.1).
    /// </summary>
    /// <returns>The emitted <c>runtime/auth.ts</c> file.</returns>
    public static GeneratedFile Emit()
    {
        // Normalize any CRLF (introduced by the source checkout) to '\n' so the emitted bytes are identical
        // regardless of the host OS or git line-ending settings (Requirement 9.1).
        var content = Template.Replace("\r\n", "\n").Replace("\r", "\n");
        return new GeneratedFile(RelativePath, content);
    }
}
