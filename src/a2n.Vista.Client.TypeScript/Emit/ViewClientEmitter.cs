using System.Text;
using a2n.Vista.Client.TypeScript.Emit.Runtime;
using a2n.Vista.Client.TypeScript.Modeling;

namespace a2n.Vista.Client.TypeScript.Emit;

/// <summary>
/// Emits one <c>views/{view}.ts</c> per <c>Mapped_View</c> (task 10.6; design §B.4): a
/// <c>{View}Client</c> class exposing exactly the <em>read</em> facets present in the view's operation set —
/// <c>list</c>, <c>detail</c>, <c>metadata</c>, and <c>export</c> — over the shared
/// <c>ClientContext</c> (Requirements 4.1–4.7). Absent read facets are omitted (Requirement 4.1); each
/// emitted method sends the exact HTTP method and path the document declares (Requirement 4.2); a secured
/// operation obtains its credential from the context and short-circuits with a typed <c>unauthorized</c>
/// result <em>before</em> sending when none is available (Requirements 7.2–7.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this task emits.</b> Only the read facets are emitted here. The gated write facets
/// (<c>create</c>/<c>update</c>/<c>delete</c>) are the concern of task 10.7; this emitter deliberately skips
/// any non-read facet (see <see cref="ReadFacetSuffixes"/>) so it can be extended by 10.7 without changing
/// the read surface. A view with no read facet still yields a file with the class shell (constructor only)
/// so the aggregate barrel (task 10.8) and the write-facet emitter (10.7) have a stable, per-view file to
/// build on.
/// </para>
/// <para>
/// <b>Method shape (design §B.4).</b> Each read method returns a <c>Promise&lt;ClientResult&lt;T&gt;&gt;</c>
/// and never throws. <c>list</c> takes a <c>VistaListRequestBody</c> and returns
/// <c>ViewListResult&lt;TRow&gt;</c> (Requirement 4.3); <c>detail</c> takes the typed key body and returns the
/// row, with a documented <c>404</c> classified into the distinct <c>not-found</c> outcome by the shared
/// classifier (Requirements 4.4/4.5); <c>metadata</c> takes no argument and returns
/// <c>VistaMetadataResponse</c> (Requirement 4.6); <c>export</c> returns the response body preserved as the
/// raw, unparsed <see cref="RawPayloadEmitter">RawPayload</see> (Requirement 4.7). The request/success types
/// are taken verbatim from the <see cref="FacetModel"/> the operation-graph step produced, so the emitted
/// signatures track the document.
/// </para>
/// <para>
/// <b>Secure by default (Requirements 7.2–7.5).</b> A secured facet asks the context for a credential before
/// sending: a <c>null</c> result — no provider supplied (7.3) or the provider yielded none or threw (7.4) —
/// short-circuits to a typed <c>unauthorized</c> result and sends nothing; otherwise the credential's header
/// is attached. An anonymous facet never asks for a credential (7.5). A rejecting transport becomes a
/// <c>transport-error</c> result with no retry (Requirement 6.6); the raw response is adapted into the
/// classifier's <c>ClassifiableResponse</c> shape, resolving the <c>Content-Type</c> header
/// case-insensitively.
/// </para>
/// <para>
/// <b>Determinism (Requirement 9).</b> Methods are emitted in the model's fixed facet order (ordinal by
/// suffix), the import block is assembled in a fixed section order with each name list ordered ordinally,
/// and the output uses a fixed <c>\n</c> line terminator with a single trailing newline, independent of the
/// document's enumeration order or the host operating system. The emitter reads only the in-memory
/// <see cref="ViewModel"/> and performs no I/O.
/// </para>
/// </remarks>
public static class ViewClientEmitter
{
    /// <summary>The fixed <c>\n</c> line terminator for emitted source (Requirement 9.1).</summary>
    private const string NewLine = "\n";

    /// <summary>Two-space indent for class members; matches the sibling emitters' convention.</summary>
    private const string Indent = "  ";

    /// <summary>The JSON media type set on a request that carries a serialized body.</summary>
    private const string JsonMediaType = "application/json";

    /// <summary>
    /// The read facet suffixes this emitter handles (Requirement 4.1). Any facet whose suffix is not in this
    /// set — the write facets <c>create</c>/<c>update</c>/<c>delete</c> — is skipped here and emitted by task
    /// 10.7.
    /// </summary>
    private static readonly IReadOnlySet<string> ReadFacetSuffixes = new HashSet<string>(StringComparer.Ordinal)
    {
        OperationGraphBuilder.ListSuffix,
        OperationGraphBuilder.DetailSuffix,
        OperationGraphBuilder.MetadataSuffix,
        OperationGraphBuilder.ExportSuffix,
    };

    /// <summary>
    /// The gated write facet suffixes this emitter handles for a writable view when write generation is
    /// enabled (task 10.7; Requirements 5.1–5.6): <c>create</c>/<c>update</c>/<c>delete</c>. They are emitted
    /// only when both the run enables write facets and the view is writable; otherwise no write operation is
    /// emitted (Requirements 5.1, 5.3).
    /// </summary>
    private static readonly IReadOnlySet<string> WriteFacetSuffixes = new HashSet<string>(StringComparer.Ordinal)
    {
        OperationGraphBuilder.CreateSuffix,
        OperationGraphBuilder.UpdateSuffix,
        OperationGraphBuilder.DeleteSuffix,
    };

    /// <summary>The concurrency-token request header a token-bearing write attaches (Requirement 5.6).</summary>
    private const string IfMatchHeader = "If-Match";

    // Relative module specifiers the emitted view file imports from (it lives under views/, so runtime and
    // sibling type modules are one directory up).
    private const string ResultModule = "../runtime/result";
    private const string ClientContextModule = "../runtime/client-context";
    private const string HttpTransportModule = "../runtime/http-transport";
    private const string AuthModule = "../runtime/auth";
    private const string RawPayloadModule = "../runtime/raw-payload";
    private const string TypesModule = "../types";

    /// <summary>
    /// Emits one <c>views/{view}.ts</c> <see cref="GeneratedFile"/> per view in <paramref name="views"/>, in
    /// deterministic order by view name (Requirement 9.2). Convenience over <see cref="Emit(ViewModel)"/> for
    /// the pipeline (task 12.2).
    /// </summary>
    /// <param name="views">The views to emit a client for.</param>
    /// <returns>One generated file per view, ordered by view name.</returns>
    public static IReadOnlyList<GeneratedFile> EmitAll(IEnumerable<ViewModel> views) =>
        EmitAll(views, emitWriteFacets: false);

    /// <summary>
    /// Emits one <c>views/{view}.ts</c> <see cref="GeneratedFile"/> per view in <paramref name="views"/>, in
    /// deterministic order by view name (Requirement 9.2), passing <paramref name="emitWriteFacets"/> through
    /// to each view so the gated write facets are emitted for writable views only when the run opts in
    /// (Requirements 5.1–5.3). The pipeline (task 12.2) supplies
    /// <c>GenerationConfig.EmitWriteFacets</c> here.
    /// </summary>
    /// <param name="views">The views to emit a client for.</param>
    /// <param name="emitWriteFacets">
    /// Whether the gated <c>create</c>/<c>update</c>/<c>delete</c> facets are emitted for writable views.
    /// </param>
    /// <returns>One generated file per view, ordered by view name.</returns>
    public static IReadOnlyList<GeneratedFile> EmitAll(IEnumerable<ViewModel> views, bool emitWriteFacets)
    {
        ArgumentNullException.ThrowIfNull(views);
        return DeterministicOrder
            .ByName(views, view => view.ViewName)
            .Select(view => Emit(view, emitWriteFacets))
            .ToArray();
    }

    /// <summary>
    /// Emits the <c>views/{view}.ts</c> <see cref="GeneratedFile"/> for a single view: the
    /// <c>{View}Client</c> class exposing a typed method for each read facet present on the view
    /// (Requirements 4.1–4.7, 7.2–7.4). The file path is derived from the view name (see
    /// <see cref="FileName"/>).
    /// </summary>
    /// <param name="view">The view whose read client is emitted.</param>
    /// <returns>The emitted view-client file.</returns>
    public static GeneratedFile Emit(ViewModel view) => Emit(view, emitWriteFacets: false);

    /// <summary>
    /// Emits the <c>views/{view}.ts</c> <see cref="GeneratedFile"/> for a single view: the
    /// <c>{View}Client</c> class exposing a typed method for each read facet present on the view
    /// (Requirements 4.1–4.7, 7.2–7.4) and, when <paramref name="emitWriteFacets"/> is enabled and the view
    /// is writable, the gated <c>create</c>/<c>update</c>/<c>delete</c> methods (Requirements 5.1–5.6). When
    /// write facets are disabled or the view is read-only, no write operation is emitted (Requirements 5.1,
    /// 5.3) and the output is byte-for-byte the read-only surface.
    /// </summary>
    /// <param name="view">The view whose client is emitted.</param>
    /// <param name="emitWriteFacets">
    /// Whether the gated write facets are emitted for a writable view (Requirements 5.1–5.3).
    /// </param>
    /// <returns>The emitted view-client file.</returns>
    public static GeneratedFile Emit(ViewModel view, bool emitWriteFacets)
    {
        ArgumentNullException.ThrowIfNull(view);

        // The facets are already ordered by suffix in the model; keep only the read facets (Requirement 4.1).
        var readFacets = view.Facets
            .Where(facet => ReadFacetSuffixes.Contains(facet.Suffix))
            .ToArray();

        // The gated write facets: emitted only when the run opts in AND the view is writable (has any write
        // facet present). Otherwise none are emitted (Requirements 5.1, 5.3).
        var writeFacets = emitWriteFacets
            ? view.Facets.Where(facet => WriteFacetSuffixes.Contains(facet.Suffix)).ToArray()
            : Array.Empty<FacetModel>();

        // Whether any emitted facet (read or write) is secured. When none is, the emitted client is
        // completely auth-free: no auth imports, no `secured` parameter, and no credential block
        // (Requirement 7.5).
        var emitAuth = readFacets.Concat(writeFacets).Any(facet => facet.Secured);

        // Whether any emitted write facet carries a concurrency token. When one does, the shared send helper
        // takes a trailing `If-Match` value and the token-bearing write methods thread the caller-supplied
        // ETag through it (Requirement 5.6).
        var emitIfMatch = writeFacets.Any(facet => facet.Concurrency == ConcurrencyMode.TokenBearing);

        var hasAnyFacet = readFacets.Length > 0 || writeFacets.Length > 0;
        var className = view.ViewName + "Client";
        var body = new StringBuilder();

        body.Append(Header(view.ViewName));
        body.Append(NewLine);

        var imports = BuildImports(readFacets, writeFacets, view.CrudType, emitAuth, emitIfMatch);
        if (imports.Length > 0)
        {
            body.Append(imports);
            body.Append(NewLine);
        }

        body.Append(RenderClass(view, className, readFacets, writeFacets, emitAuth, emitIfMatch));

        // A trailing helper the class methods call, and a single trailing newline.
        if (hasAnyFacet)
        {
            body.Append(NewLine);
            body.Append(ContentTypeHelper());
        }

        var content = Normalize(body.ToString());
        return new GeneratedFile($"views/{FileName(view.ViewName)}.ts", content);
    }

    // Renders the exported client class: the doc comment, the constructor, one method per read facet, and
    // (when any read facet is present) the private send helper.
    private static string RenderClass(
        ViewModel view,
        string className,
        IReadOnlyList<FacetModel> readFacets,
        IReadOnlyList<FacetModel> writeFacets,
        bool emitAuth,
        bool emitIfMatch)
    {
        var builder = new StringBuilder();

        builder.Append("/**").Append(NewLine);
        builder.Append($" * The typed client for the {view.ViewName} view's facets. Construct it with a shared")
            .Append(NewLine);
        builder.Append(" * {@link ClientContext}; each method routes its request through the context's transport and")
            .Append(NewLine);
        builder.Append(" * returns a typed {@link ClientResult} without throwing.").Append(NewLine);
        builder.Append(" */").Append(NewLine);
        builder.Append($"export class {className} {{").Append(NewLine);
        builder.Append(Indent).Append("constructor(private readonly ctx: ClientContext) {}").Append(NewLine);

        // Read facets first (unchanged read surface), then the gated write facets, each block already in
        // ordinal suffix order from the model (Requirement 9.2).
        foreach (var facet in readFacets)
        {
            builder.Append(NewLine);
            builder.Append(RenderMethod(facet, emitAuth));
        }

        foreach (var facet in writeFacets)
        {
            builder.Append(NewLine);
            builder.Append(RenderWriteMethod(facet, view.CrudType, emitAuth, emitIfMatch));
        }

        if (readFacets.Count > 0 || writeFacets.Count > 0)
        {
            builder.Append(NewLine);
            builder.Append(RenderSendHelper(view.ViewName, emitAuth, emitIfMatch));
        }

        builder.Append("}").Append(NewLine);
        return builder.ToString();
    }

    // Renders one read-facet method. The request/success types come verbatim from the facet model, so the
    // signature tracks the document (Requirements 4.2–4.7). The `secured` argument is threaded to the shared
    // send helper only when the client carries auth handling (see <paramref name="emitAuth"/>).
    private static string RenderMethod(FacetModel facet, bool emitAuth)
    {
        var successType = facet.SuccessType.Render();
        var hasBody = facet.RequestType is not null;
        var parameter = hasBody ? $"body: {facet.RequestType!.Render()}" : string.Empty;
        var bodyArgument = hasBody ? "JSON.stringify(body)" : "undefined";
        var parseExpression = IsRawPayload(facet.SuccessType)
            ? "(raw) => raw"
            : $"(raw) => JSON.parse(raw) as {successType}";
        var securedLiteral = facet.Secured ? "true" : "false";
        var argIndent = Indent + Indent + Indent;

        var builder = new StringBuilder();

        builder.Append(Indent).Append("/**").Append(NewLine);
        builder.Append(Indent).Append($" * Calls the {facet.Suffix} facet ({facet.HttpMethod} {facet.Path}).")
            .Append(NewLine);
        builder.Append(Indent).Append(" */").Append(NewLine);

        builder.Append(Indent)
            .Append($"{facet.Suffix}({parameter}): Promise<ClientResult<{successType}>> {{")
            .Append(NewLine);
        builder.Append(Indent).Append(Indent).Append($"return this.send<{successType}>(").Append(NewLine);
        builder.Append(argIndent).Append($"\"{facet.Suffix}\",").Append(NewLine);
        builder.Append(argIndent).Append($"\"{facet.HttpMethod}\",").Append(NewLine);
        builder.Append(argIndent).Append($"\"{facet.Path}\",").Append(NewLine);
        if (emitAuth)
        {
            builder.Append(argIndent).Append($"{securedLiteral},").Append(NewLine);
        }

        builder.Append(argIndent).Append($"{bodyArgument},").Append(NewLine);
        builder.Append(argIndent).Append($"{parseExpression},").Append(NewLine);
        builder.Append(Indent).Append(Indent).Append(");").Append(NewLine);
        builder.Append(Indent).Append("}").Append(NewLine);

        return builder.ToString();
    }

    // Renders one gated write-facet method (Requirements 5.2, 5.4–5.6). create/update take the view's typed
    // TCrud model and send it as the JSON body (Requirements 5.4, 5.5); delete takes no model
    // (Requirement 5.5). A token-bearing write (documented 428/409) additionally accepts a caller-supplied
    // ETag/If-Match through an options object and threads it to the send helper, which surfaces the distinct
    // precondition-required (428) and precondition-failed (409) outcomes via the shared classifier
    // (Requirement 5.6). The success type comes verbatim from the facet model, so create returns the
    // document's VistaWriteResponse (Requirement 5.4). Every outcome is a typed ClientResult; no method
    // throws (Requirements 5.4, 5.5).
    private static string RenderWriteMethod(FacetModel facet, TsType? crudType, bool emitAuth, bool emitIfMatch)
    {
        var successType = facet.SuccessType.Render();
        var carriesModel = string.Equals(facet.Suffix, OperationGraphBuilder.CreateSuffix, StringComparison.Ordinal)
            || string.Equals(facet.Suffix, OperationGraphBuilder.UpdateSuffix, StringComparison.Ordinal);
        var tokenBearing = facet.Concurrency == ConcurrencyMode.TokenBearing;

        // The model parameter is the view's TCrud reference (non-null for a writable view); fall back to
        // `unknown` defensively so the emitted method is always well-formed.
        var modelType = crudType?.Render() ?? "unknown";

        // Assemble the parameter list deterministically: the typed model (create/update), then the optional
        // concurrency options for a token-bearing write.
        var parameters = new List<string>();
        if (carriesModel)
        {
            parameters.Add($"model: {modelType}");
        }

        if (tokenBearing)
        {
            parameters.Add("options?: { readonly ifMatch?: string }");
        }

        var parameterList = string.Join(", ", parameters);
        var bodyArgument = carriesModel ? "JSON.stringify(model)" : "undefined";
        var parseExpression = IsRawPayload(facet.SuccessType)
            ? "(raw) => raw"
            : $"(raw) => JSON.parse(raw) as {successType}";
        var securedLiteral = facet.Secured ? "true" : "false";
        var argIndent = Indent + Indent + Indent;

        var builder = new StringBuilder();

        builder.Append(Indent).Append("/**").Append(NewLine);
        builder.Append(Indent).Append($" * Calls the {facet.Suffix} write facet ({facet.HttpMethod} {facet.Path}).")
            .Append(NewLine);
        if (tokenBearing)
        {
            builder.Append(Indent)
                .Append(" * Pass `options.ifMatch` to supply the concurrency token as the `If-Match` header; a")
                .Append(NewLine);
            builder.Append(Indent)
                .Append(" * missing token surfaces as `precondition-required` (428) and a stale token as")
                .Append(NewLine);
            builder.Append(Indent).Append(" * `precondition-failed` (409).").Append(NewLine);
        }

        builder.Append(Indent).Append(" */").Append(NewLine);

        builder.Append(Indent)
            .Append($"{facet.Suffix}({parameterList}): Promise<ClientResult<{successType}>> {{")
            .Append(NewLine);
        builder.Append(Indent).Append(Indent).Append($"return this.send<{successType}>(").Append(NewLine);
        builder.Append(argIndent).Append($"\"{facet.Suffix}\",").Append(NewLine);
        builder.Append(argIndent).Append($"\"{facet.HttpMethod}\",").Append(NewLine);
        builder.Append(argIndent).Append($"\"{facet.Path}\",").Append(NewLine);
        if (emitAuth)
        {
            builder.Append(argIndent).Append($"{securedLiteral},").Append(NewLine);
        }

        builder.Append(argIndent).Append($"{bodyArgument},").Append(NewLine);
        builder.Append(argIndent).Append($"{parseExpression},").Append(NewLine);

        // Thread the caller-supplied ETag only for a token-bearing write; other writes (and every read) omit
        // the trailing `If-Match` argument, which is a trailing optional on the send helper.
        if (emitIfMatch)
        {
            var ifMatchArgument = tokenBearing ? "options?.ifMatch" : "undefined";
            builder.Append(argIndent).Append($"{ifMatchArgument},").Append(NewLine);
        }

        builder.Append(Indent).Append(Indent).Append(");").Append(NewLine);
        builder.Append(Indent).Append("}").Append(NewLine);

        return builder.ToString();
    }

    // Renders the private send helper shared by every read method: it applies the secure-by-default auth
    // policy, routes the request through the context's transport exactly once, and classifies the response
    // into a typed ClientResult without throwing (Requirements 6.1, 6.6, 7.2–7.5, 8.x).
    private static string RenderSendHelper(string viewName, bool emitAuth, bool emitIfMatch)
    {
        // The view name is a document identifier; embed it as a JSON string literal so any unusual character
        // is escaped deterministically.
        var viewLiteral = ToJsonStringLiteral(viewName);

        var lines = new List<string>
        {
            "/**",
            " * Builds and routes a single request through the shared context and classifies the response",
            " * into a typed {@link ClientResult} without throwing. A rejecting transport becomes a",
            " * `transport-error` result with no retry (Requirement 6.6).",
        };

        if (emitAuth)
        {
            lines.AddRange(new[]
            {
                " *",
                " * For a secured facet it obtains the credential before sending: when none is available — no",
                " * provider (Requirement 7.3) or the provider yielded none or threw (Requirement 7.4) — it",
                " * returns a typed `unauthorized` result and sends nothing (Requirements 7.2–7.4).",
            });
        }

        lines.Add(" */");
        lines.Add("private async send<T>(");
        lines.Add("  facet: string,");
        lines.Add("  method: string,");
        lines.Add("  path: string,");
        if (emitAuth)
        {
            lines.Add("  secured: boolean,");
        }

        lines.Add("  body: string | undefined,");
        lines.Add("  parseSuccess: (raw: string) => T,");
        if (emitIfMatch)
        {
            lines.Add("  ifMatch?: string,");
        }

        lines.Add("): Promise<ClientResult<T>> {");
        lines.Add("  const headers: Record<string, string> = {};");
        lines.Add("  if (body !== undefined) {");
        lines.Add($"    headers[\"Content-Type\"] = \"{JsonMediaType}\";");
        lines.Add("  }");
        if (emitIfMatch)
        {
            lines.Add("  if (ifMatch !== undefined) {");
            lines.Add($"    headers[\"{IfMatchHeader}\"] = ifMatch;");
            lines.Add("  }");
        }

        if (emitAuth)
        {
            lines.AddRange(new[]
            {
                "",
                "  if (secured) {",
                $"    const operation: OperationInfo = {{ view: {viewLiteral}, facet, secured: true }};",
                "    let credential: AuthCredential | null;",
                "    try {",
                "      credential = await this.ctx.getCredential(operation);",
                "    } catch {",
                "      return unauthorized<T>(",
                "        `No credential is available for the secured operation \"${operation.view}.${facet}\" ` +",
                "          \"(the auth provider threw while obtaining one).\",",
                "      );",
                "    }",
                "    if (credential === null) {",
                "      return unauthorized<T>(`No credential is available for the secured operation \"${operation.view}.${facet}\".`);",
                "    }",
                "    headers[credential.headerName] = credential.headerValue;",
                "  }",
            });
        }

        lines.AddRange(new[]
        {
            "",
            "  const request: HttpRequest = {",
            "    method,",
            "    url: this.ctx.resolveUrl(path),",
            "    headers,",
            "    ...(body === undefined ? {} : { body }),",
            "  };",
            "",
            "  let response: HttpResponse;",
            "  try {",
            "    response = await this.ctx.transport.send(request);",
            "  } catch (error) {",
            "    return transportError<T>(error);",
            "  }",
            "",
            "  const classifiable: ClassifiableResponse = {",
            "    status: response.status,",
            "    contentType: readContentType(response.headers),",
            "    body: response.body,",
            "  };",
            "  return classifyResponse(classifiable, parseSuccess);",
            "}",
        });

        return IndentBlock(lines, Indent);
    }

    // Renders the module-level Content-Type resolver the send helper calls. Kept module-private so it is not
    // re-exported by the barrel (task 10.8).
    private static string ContentTypeHelper()
    {
        var lines = new[]
        {
            "/**",
            " * Resolves the `Content-Type` response header case-insensitively (a custom transport may not",
            " * lower-case header names the way the default `fetch` transport does), returning `null` when it",
            " * is absent.",
            " */",
            "function readContentType(headers: Readonly<Record<string, string>>): string | null {",
            "  for (const [name, value] of Object.entries(headers)) {",
            "    if (name.toLowerCase() === \"content-type\") {",
            "      return value;",
            "    }",
            "  }",
            "  return null;",
            "}",
        };

        return string.Join(NewLine, lines) + NewLine;
    }

    // Assembles the deterministic import block for the emitted read and write facets: a fixed section order,
    // each imported name list ordered ordinally, importing only what the emitted methods use.
    private static string BuildImports(
        IReadOnlyList<FacetModel> readFacets,
        IReadOnlyList<FacetModel> writeFacets,
        TsType? crudType,
        bool emitAuth,
        bool emitIfMatch)
    {
        _ = emitIfMatch; // The concurrency options type is emitted inline; it needs no import.

        if (readFacets.Count == 0 && writeFacets.Count == 0)
        {
            // A facet-less client references only the ClientContext type.
            return $"import type {{ ClientContext }} from \"{ClientContextModule}\";" + NewLine;
        }

        var anySecured = emitAuth;
        var needsRawPayload = readFacets.Concat(writeFacets).Any(facet => IsRawPayload(facet.SuccessType));

        // create/update carry the view's typed TCrud model, so its named reference must be imported.
        var needsCrud = writeFacets.Any(facet =>
            string.Equals(facet.Suffix, OperationGraphBuilder.CreateSuffix, StringComparison.Ordinal)
            || string.Equals(facet.Suffix, OperationGraphBuilder.UpdateSuffix, StringComparison.Ordinal));
        var typeReferences = CollectTypeReferences(readFacets, writeFacets, needsCrud ? crudType : null);

        var builder = new StringBuilder();

        // 1. Runtime value imports from result.ts (classifyResponse + transportError always; unauthorized
        //    only when a secured facet is present).
        var resultValues = new List<string> { "classifyResponse", "transportError" };
        if (anySecured)
        {
            resultValues.Add("unauthorized");
        }

        builder.Append(NamedImport(resultValues, ResultModule, typeOnly: false)).Append(NewLine);

        // 2. Runtime type imports from result.ts.
        builder.Append(NamedImport(new[] { "ClassifiableResponse", "ClientResult" }, ResultModule, typeOnly: true))
            .Append(NewLine);

        // 3. The shared composition root.
        builder.Append(NamedImport(new[] { "ClientContext" }, ClientContextModule, typeOnly: true)).Append(NewLine);

        // 4. The request/response contracts the send helper annotates.
        builder.Append(NamedImport(new[] { "HttpRequest", "HttpResponse" }, HttpTransportModule, typeOnly: true))
            .Append(NewLine);

        // 5. Auth contracts, only when a secured facet attaches a credential.
        if (anySecured)
        {
            builder.Append(NamedImport(new[] { "AuthCredential", "OperationInfo" }, AuthModule, typeOnly: true))
                .Append(NewLine);
        }

        // 6. The raw-payload type, only when an export (raw) facet is present.
        if (needsRawPayload)
        {
            builder.Append(NamedImport(new[] { "RawPayload" }, RawPayloadModule, typeOnly: true)).Append(NewLine);
        }

        // 7. The generated DTO/envelope types the signatures reference.
        if (typeReferences.Count > 0)
        {
            builder.Append(NamedImport(typeReferences, TypesModule, typeOnly: true)).Append(NewLine);
        }

        return builder.ToString();
    }

    // Collects the named type references the emitted signatures use that are declared in ../types: walk each
    // read facet's request and success type; each write facet's success type (write methods take the typed
    // TCrud model rather than the request envelope); and the view's TCrud reference when a create/update is
    // emitted. The runtime-declared RawPayload is dropped (imported separately).
    private static IReadOnlyList<string> CollectTypeReferences(
        IReadOnlyList<FacetModel> readFacets,
        IReadOnlyList<FacetModel> writeFacets,
        TsType? crudType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var facet in readFacets)
        {
            if (facet.RequestType is not null)
            {
                CollectNamedReferences(facet.RequestType, names);
            }

            CollectNamedReferences(facet.SuccessType, names);
        }

        foreach (var facet in writeFacets)
        {
            CollectNamedReferences(facet.SuccessType, names);
        }

        if (crudType is not null)
        {
            CollectNamedReferences(crudType, names);
        }

        names.Remove(OperationGraphBuilder.RawPayloadTypeName);
        return DeterministicOrder.OrderNames(names);
    }

    // Walks a type expression, collecting every named/generic type reference into the accumulator.
    private static void CollectNamedReferences(TsType type, ISet<string> accumulator)
    {
        switch (type)
        {
            case TsNamed named:
                accumulator.Add(named.Name);
                break;

            case TsArray array:
                CollectNamedReferences(array.Element, accumulator);
                break;

            case TsNullable nullable:
                CollectNamedReferences(nullable.Inner, accumulator);
                break;

            case TsGeneric generic:
                accumulator.Add(generic.Name);
                foreach (var argument in generic.Arguments)
                {
                    CollectNamedReferences(argument, accumulator);
                }

                break;

            // Primitives and literal unions carry no named reference.
            default:
                break;
        }
    }

    private static bool IsRawPayload(TsType type) =>
        type is TsNamed named && string.Equals(named.Name, OperationGraphBuilder.RawPayloadTypeName, StringComparison.Ordinal);

    // Renders a single import statement with the names ordered ordinally (Requirement 9.2).
    private static string NamedImport(IEnumerable<string> names, string module, bool typeOnly)
    {
        var ordered = DeterministicOrder.OrderNames(names);
        var keyword = typeOnly ? "import type" : "import";
        return $"{keyword} {{ {string.Join(", ", ordered)} }} from \"{module}\";";
    }

    // The generated-file header (Requirement 9.1); English only (published artifact).
    private static string Header(string viewName) =>
        "// <auto-generated>" + NewLine +
        "//   This file is generated by the a2n.Vista TypeScript client generator. Do not edit by hand." + NewLine +
        $"//   It defines the typed client for the {viewName} view." + NewLine +
        "// </auto-generated>" + NewLine;

    // Indents every non-empty line of a block by the given prefix, leaving blank lines empty, and terminates
    // each line with the fixed newline.
    private static string IndentBlock(IEnumerable<string> lines, string prefix)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                builder.Append(NewLine);
            }
            else
            {
                builder.Append(prefix).Append(line).Append(NewLine);
            }
        }

        return builder.ToString();
    }

    // Escapes a string as a double-quoted JSON/TypeScript string literal so an unusual view name stays a
    // valid, deterministic literal.
    private static string ToJsonStringLiteral(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Derives the deterministic, output-directory-relative file stem for a view from its name: a
    /// kebab-case transform of the verbatim view name (e.g. <c>Customers</c> → <c>customers</c>,
    /// <c>OrderDetails</c> → <c>order-details</c>). The transform inserts a <c>-</c> at each lower→upper (or
    /// acronym→word) boundary and lower-cases the result, so it is stable and file-system friendly across
    /// operating systems (Requirement 9.1/9.2).
    /// </summary>
    /// <param name="viewName">The verbatim view name.</param>
    /// <returns>The kebab-case file stem (without extension).</returns>
    public static string FileName(string viewName)
    {
        ArgumentException.ThrowIfNullOrEmpty(viewName);

        var builder = new StringBuilder(viewName.Length + 8);
        for (var i = 0; i < viewName.Length; i++)
        {
            var c = viewName[i];
            if (char.IsUpper(c))
            {
                var previousIsWord = i > 0 && (char.IsLower(viewName[i - 1]) || char.IsDigit(viewName[i - 1]));
                var acronymBoundary = i > 0 && char.IsUpper(viewName[i - 1])
                    && i + 1 < viewName.Length && char.IsLower(viewName[i + 1]);
                if (builder.Length > 0 && (previousIsWord || acronymBoundary))
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    // Normalizes any source-authored line endings to the generator's single fixed \n terminator, with a
    // single trailing newline, so emitted bytes never depend on the host OS (Requirement 9.1).
    private static string Normalize(string text)
    {
        var lf = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        return lf.EndsWith('\n') ? lf : lf + NewLine;
    }
}
