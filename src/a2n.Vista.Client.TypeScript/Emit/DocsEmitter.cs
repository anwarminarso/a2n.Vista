using System.Text;

namespace a2n.Vista.Client.TypeScript.Emit;

/// <summary>
/// Emits the English usage documentation (<c>README.md</c>) that ships beside the generated client (task
/// 10.8; design §A.7 <c>DocsEmitter</c>). The document explains how to construct a
/// <c>ClientContext</c> (base URL + optional transport + optional <c>AuthProvider</c>), how to call a view's
/// read facets (<c>list</c>/<c>detail</c>/<c>metadata</c>/<c>export</c>) — and, when write generation was
/// enabled, its <c>create</c>/<c>update</c>/<c>delete</c> facets — how the discriminated
/// <c>ClientResult&lt;T&gt;</c> is read, and the secure-by-default posture (HTTPS transport, no embedded
/// credential).
/// </summary>
/// <remarks>
/// <para>
/// <b>English only.</b> The README is a published artifact, so it is authored in English (project
/// non-negotiable). Its examples reference only the real emitted surface — <c>ClientContext</c>, the per-view
/// <c>{View}Client</c> classes, <c>ClientResult&lt;T&gt;</c>, <c>bearerAuth</c>, and
/// <c>fetchTransport</c>/<c>createFetchTransport</c> — so the documentation cannot describe an API the
/// generator does not emit.
/// </para>
/// <para>
/// <b>No credential, no UI/grid.</b> The document embeds no token or secret (Requirement 7.1) and mentions no
/// UI or grid dependency (Requirements 12.5, 12.6): the client it documents depends only on the injected
/// transport.
/// </para>
/// <para>
/// <b>Determinism (Requirement 9).</b> The prose is a fixed template; the only document-derived parts are the
/// worked example's view name (the ordinally-first mapped view, chosen via <see cref="DeterministicOrder"/>)
/// and whether the write-facet section is included (from the emit-time flag). Both are a pure function of the
/// inputs, so the same document and config produce byte-identical output. A fixed <c>\n</c> line terminator
/// and a single trailing newline are used throughout; the emitter performs no I/O.
/// </para>
/// </remarks>
public static class DocsEmitter
{
    /// <summary>The output-directory-relative path of the emitted documentation (forward slashes).</summary>
    public const string RelativePath = "README.md";

    /// <summary>The fixed <c>\n</c> line terminator for emitted content (Requirement 9.1).</summary>
    private const string NewLine = "\n";

    /// <summary>The placeholder view name used in the worked example when the document maps no view.</summary>
    private const string PlaceholderView = "Example";

    /// <summary>
    /// Produces the buffered <see cref="GeneratedFile"/> for <c>README.md</c>. The worked example uses the
    /// ordinally-first view name; the write-facet section is included only when
    /// <paramref name="emitWriteFacets"/> is enabled (matching what the view clients actually emit).
    /// </summary>
    /// <param name="viewNames">
    /// The verbatim mapped-view names; the ordinally-first is used in the example. May be empty, in which
    /// case a neutral placeholder view name is used.
    /// </param>
    /// <param name="emitWriteFacets">
    /// Whether write facets were generated. When <c>true</c>, the README documents
    /// <c>create</c>/<c>update</c>/<c>delete</c>; when <c>false</c>, it notes that write facets are gated off
    /// by default and how to enable them.
    /// </param>
    /// <returns>The emitted <c>README.md</c> file.</returns>
    public static GeneratedFile Emit(IEnumerable<string> viewNames, bool emitWriteFacets)
    {
        ArgumentNullException.ThrowIfNull(viewNames);

        var exampleView = DeterministicOrder.OrderNames(viewNames).FirstOrDefault() ?? PlaceholderView;
        var content = Render(exampleView, emitWriteFacets);
        return new GeneratedFile(RelativePath, Normalize(content));
    }

    private static string Render(string exampleView, bool emitWriteFacets)
    {
        var clientClass = exampleView + "Client";
        var builder = new StringBuilder();

        builder.Append("# a2n.Vista TypeScript client").Append(NewLine).Append(NewLine);
        builder.Append(
                "This directory is a generated, framework-agnostic TypeScript client for an a2n.Vista API. It was "
                + "produced from the API's OpenAPI document and contains the request/response types, the "
                + "polymorphic `FilterNode` filter tree, the RFC 7807 `ProblemDetails` type, a small runtime "
                + "(transport, auth, result, URL helpers), and one typed client class per view. Do not edit these "
                + "files by hand: regenerating overwrites them.")
            .Append(NewLine).Append(NewLine);
        builder.Append(
                "The client imports no UI framework and no grid library. It talks to the server only through an "
                + "HTTP transport you inject (or the default `fetch`-backed one), so it runs in any TypeScript "
                + "runtime.")
            .Append(NewLine).Append(NewLine);

        builder.Append("## Importing").Append(NewLine).Append(NewLine);
        builder.Append(
                "Everything is re-exported from the barrel (`index.ts`), so a single import path reaches the "
                + "context, the runtime helpers, and every view client:")
            .Append(NewLine).Append(NewLine);
        builder.Append("```ts").Append(NewLine);
        builder.Append($"import {{ ClientContext, {clientClass}, bearerAuth }} from \"./generated\";")
            .Append(NewLine);
        builder.Append("```").Append(NewLine).Append(NewLine);

        builder.Append("## Constructing a client").Append(NewLine).Append(NewLine);
        builder.Append(
                "Create one `ClientContext` and share it across the view clients. The context validates the base "
                + "URL at construction and holds the transport and the optional auth provider.")
            .Append(NewLine).Append(NewLine);
        builder.Append("```ts").Append(NewLine);
        builder.Append("const ctx = new ClientContext({").Append(NewLine);
        builder.Append("  baseUrl: \"https://api.example.com\",").Append(NewLine);
        builder.Append("  // transport is optional; when omitted the default fetch-backed transport is used.")
            .Append(NewLine);
        builder.Append("  // auth is optional; supply it only when the API declares secured operations.")
            .Append(NewLine);
        builder.Append("  auth: bearerAuth(() => getAccessToken()),").Append(NewLine);
        builder.Append("});").Append(NewLine).Append(NewLine);
        builder.Append($"const {CamelCase(exampleView)} = new {clientClass}(ctx);").Append(NewLine);
        builder.Append("```").Append(NewLine).Append(NewLine);

        builder.Append(
                "- **`baseUrl`** (required) — the API root. Each request path is joined to it with exactly one "
                + "`/`.")
            .Append(NewLine);
        builder.Append(
                "- **`transport`** (optional) — any `HttpTransport`. Omit it to use the default backed by the "
                + "platform global `fetch`; use `createFetchTransport()` if you want construction to fail fast "
                + "when `fetch` is unavailable, or `fetchTransport` for the lazy singleton.")
            .Append(NewLine);
        builder.Append(
                "- **`auth`** (optional) — an `AuthProvider`. `bearerAuth(tokenProvider)` attaches "
                + "`Authorization: Bearer <token>` to each secured request. The client never embeds a credential; "
                + "you always supply the token.")
            .Append(NewLine).Append(NewLine);

        builder.Append("## Calling read facets").Append(NewLine).Append(NewLine);
        builder.Append(
                "Each view client exposes exactly the read facets the API declares for that view. Absent facets "
                + "are simply not emitted. Every method returns a `Promise<ClientResult<T>>` and never throws.")
            .Append(NewLine).Append(NewLine);
        builder.Append("```ts").Append(NewLine);
        builder.Append("// list: POST {route}/list — a typed request body, a paged result.").Append(NewLine);
        builder.Append($"const listed = await {CamelCase(exampleView)}.list({{ page: 0, pageSize: 20 }});")
            .Append(NewLine);
        builder.Append("if (listed.kind === \"success\") {").Append(NewLine);
        builder.Append("  for (const row of listed.value.page.items) {").Append(NewLine);
        builder.Append("    // row is fully typed").Append(NewLine);
        builder.Append("  }").Append(NewLine);
        builder.Append("}").Append(NewLine).Append(NewLine);
        builder.Append("// detail: POST {route}/detail — a typed key, a single row (404 -> \"not-found\").")
            .Append(NewLine);
        builder.Append($"const detailed = await {CamelCase(exampleView)}.detail({{ key: 1 }});").Append(NewLine);
        builder.Append(NewLine);
        builder.Append("// metadata: GET {route}/metadata — no argument, the view's field metadata.")
            .Append(NewLine);
        builder.Append($"const meta = await {CamelCase(exampleView)}.metadata();").Append(NewLine);
        builder.Append(NewLine);
        builder.Append("// export: POST {route}/export — a typed format union; the body is the raw payload.")
            .Append(NewLine);
        builder.Append($"const exported = await {CamelCase(exampleView)}.export({{ format: \"csv\" }});")
            .Append(NewLine);
        builder.Append("```").Append(NewLine).Append(NewLine);

        builder.Append("## Write facets").Append(NewLine).Append(NewLine);
        if (emitWriteFacets)
        {
            builder.Append(
                    "Write generation is **enabled** for this client. Writable views additionally expose "
                    + "`create`, `update`, and `delete`. Like the read facets, they return a typed "
                    + "`ClientResult<T>` and never throw. Read-only views expose no write method.")
                .Append(NewLine).Append(NewLine);
            builder.Append("```ts").Append(NewLine);
            builder.Append("// create: POST {route}/create — a typed write model, returns the created key.")
                .Append(NewLine);
            builder.Append($"const created = await {CamelCase(exampleView)}.create({{ /* TCrud fields */ }});")
                .Append(NewLine);
            builder.Append("if (created.kind === \"success\") {").Append(NewLine);
            builder.Append("  const key = created.value.key;").Append(NewLine);
            builder.Append("}").Append(NewLine).Append(NewLine);
            builder.Append("// update / delete — when the view declares a concurrency token, pass the ETag as")
                .Append(NewLine);
            builder.Append("// options.ifMatch; a missing token surfaces as \"precondition-required\" (428) and")
                .Append(NewLine);
            builder.Append("// a stale token as \"precondition-failed\" (409).").Append(NewLine);
            builder.Append(
                    $"const updated = await {CamelCase(exampleView)}.update({{ /* TCrud fields */ }}, "
                    + "{ ifMatch: etag });")
                .Append(NewLine);
            builder.Append($"const removed = await {CamelCase(exampleView)}.delete({{ ifMatch: etag }});")
                .Append(NewLine);
            builder.Append("```").Append(NewLine).Append(NewLine);
        }
        else
        {
            builder.Append(
                    "Write facets are **gated off by default**, so no `create`, `update`, or `delete` method is "
                    + "emitted on any view client. To adopt the write surface deliberately, regenerate the client "
                    + "with write-facet generation enabled; writable views then expose typed `create`/`update`/"
                    + "`delete` methods that return a `ClientResult<T>` and never throw.")
                .Append(NewLine).Append(NewLine);
        }

        builder.Append("## Handling results").Append(NewLine).Append(NewLine);
        builder.Append(
                "Every operation returns a single discriminated union, `ClientResult<T>`. Read the `kind` field "
                + "to handle the outcome — you never inspect the HTTP status directly and never catch an "
                + "exception for an HTTP or parse failure.")
            .Append(NewLine).Append(NewLine);
        builder.Append("```ts").Append(NewLine);
        builder.Append($"const result = await {CamelCase(exampleView)}.list({{ page: 0, pageSize: 20 }});")
            .Append(NewLine);
        builder.Append("switch (result.kind) {").Append(NewLine);
        builder.Append("  case \"success\":").Append(NewLine);
        builder.Append("    // result.value is the typed success payload").Append(NewLine);
        builder.Append("    break;").Append(NewLine);
        builder.Append("  case \"problem\":").Append(NewLine);
        builder.Append("    // RFC 7807 body in result.problem, HTTP status in result.status")
            .Append(NewLine);
        builder.Append("    break;").Append(NewLine);
        builder.Append("  case \"not-found\":            // 404, a typed ProblemDetails").Append(NewLine);
        builder.Append("  case \"unauthorized\":         // no credential available; request was not sent")
            .Append(NewLine);
        builder.Append("  case \"precondition-required\": // 428, missing concurrency token").Append(NewLine);
        builder.Append("  case \"precondition-failed\":   // 409, stale concurrency token").Append(NewLine);
        builder.Append("  case \"transport-error\":       // the transport rejected; no retry was performed")
            .Append(NewLine);
        builder.Append("  case \"unexpected\":            // non-2xx / undecodable body; raw body preserved")
            .Append(NewLine);
        builder.Append("    break;").Append(NewLine);
        builder.Append("}").Append(NewLine);
        builder.Append("```").Append(NewLine).Append(NewLine);

        builder.Append("## Secure by default").Append(NewLine).Append(NewLine);
        builder.Append(
                "- **No embedded credential.** The generated output contains no token or secret. You supply "
                + "credentials through the `AuthProvider`; secured requests without one short-circuit to a typed "
                + "`unauthorized` result and are never sent.")
            .Append(NewLine);
        builder.Append(
                "- **HTTPS by default.** A non-HTTPS base URL to a loopback host (`localhost`, `127.0.0.1`, "
                + "`::1`) warns and continues; a non-HTTPS base URL to any other host is rejected at "
                + "construction and no request is issued.")
            .Append(NewLine);
        builder.Append(
                "- **Anonymous APIs.** When the API declares no secured operations, requests are sent without a "
                + "credential and the auth provider is never consulted.")
            .Append(NewLine);

        return builder.ToString();
    }

    // Lower-cases the first character of a view name for a readable example variable name (e.g. Customers ->
    // customers, OrderDetails -> orderDetails). Purely cosmetic and deterministic.
    private static string CamelCase(string viewName)
    {
        if (viewName.Length == 0)
        {
            return viewName;
        }

        return char.ToLowerInvariant(viewName[0]) + viewName[1..];
    }

    // Normalizes any source-authored line endings to the generator's single fixed \n terminator, with a
    // single trailing newline, so emitted bytes never depend on the host OS (Requirement 9.1).
    private static string Normalize(string text)
    {
        var lf = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        return lf.EndsWith('\n') ? lf : lf + NewLine;
    }
}
