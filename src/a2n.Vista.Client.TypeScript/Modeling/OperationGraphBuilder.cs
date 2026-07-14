using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// The operation-graph construction step (task 7.5; design §A.5 step 4 "Operation-graph construction"). It
/// groups the document's <c>paths</c> by view root — the fixed action-style endpoint root a view exposes
/// (<c>{route}</c>, default <c>/api/views/{view}</c>) — and derives, for each <c>Mapped_View</c>, the set of
/// facets actually present in the document (<c>list</c>/<c>detail</c>/<c>metadata</c>/<c>export</c>, plus
/// <c>create</c>/<c>update</c>/<c>delete</c> for a writable view), capturing each facet's HTTP method, path,
/// request/success type references, per-operation secured flag, and its <see cref="ConcurrencyMode"/>
/// (Requirements 4.1, 4.2, 5.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only what the document declares (Requirement 4.1).</b> A facet is emitted only when the corresponding
/// operation is present in the document; an absent read facet is omitted rather than synthesized. Path
/// suffixes the generator does not recognize as a Vista facet (for example the deferred grid-adapter
/// endpoints, requirements Non-goals) are skipped, not modeled.
/// </para>
/// <para>
/// <b>Grouping is robust to naming (design §A.5 step 4).</b> Each operation's facet suffix is the last path
/// segment (<c>{route}/list</c> → <c>list</c>), and its view root is the path with that suffix stripped. All
/// facets that share a view root form one view. The view name is derived from the operation id
/// (<c>{viewName}_{suffix}</c>, e.g. <c>Customers_list</c> → <c>Customers</c>), falling back to the view
/// root's last segment when the operation id is absent or malformed — so grouping never depends on a single
/// convention holding.
/// </para>
/// <para>
/// <b>Success/request typing (Requirements 4.2–4.7, 5.4).</b> Each facet's request and success types are read
/// from the operation's own request body and its lowest-numbered <c>2xx</c> response, referenced by name
/// (Requirement 2.5). A list success whose response component is a monomorphized <c>ViewListResult_*</c> is
/// collapsed to the single generic <c>ViewListResult&lt;TRow&gt;</c> (Requirement 2.6) via the supplied
/// <see cref="EnvelopeReLiftResult"/>; a recognized-but-unlisted component falls back to a plain named
/// reference. A success response whose body is an inline (unnamed) schema — as the <c>export</c> facet's raw
/// octet-stream payload is — is represented as the runtime <see cref="RawPayloadTypeName"/> reference,
/// preserving the body as a raw, unparsed payload (Requirement 4.7).
/// </para>
/// <para>
/// <b>Concurrency (Requirement 5.6).</b> A facet whose operation documents a <c>428</c> (missing required
/// precondition) and/or <c>409</c> (precondition/version mismatch) response is
/// <see cref="ConcurrencyMode.TokenBearing"/> — this is how a writable view's <c>update</c>/<c>delete</c>
/// operations advertise their concurrency token; every other facet is <see cref="ConcurrencyMode.None"/>.
/// </para>
/// <para>
/// <b>Row/crud binding.</b> A view's <c>RowType</c> is taken from its re-lifted list envelope's row
/// component, falling back to the <c>detail</c> success reference; its <c>CrudType</c> (present only for a
/// writable view) is taken from the <c>create</c> operation's request body reference, falling back to
/// <c>update</c>'s. The by-name references are produced through <see cref="DtoModelBuilder"/>.
/// </para>
/// <para>
/// <b>Deterministic and pure (Requirement 9.2).</b> Views are returned pre-sorted by view name and each
/// view's facets pre-sorted by suffix, using the fixed ordinal, case-sensitive
/// <see cref="DeterministicOrder"/> comparison, independent of the document's path/method enumeration order.
/// The builder performs no I/O and mutates nothing except the supplied <see cref="NoticeCollector"/>. It has
/// no fatal path: missing required envelopes/DTOs are the concern of the earlier binding steps
/// (<see cref="EnvelopeCatalog"/>, <see cref="DtoModelBuilder"/>), so grouping the operations that are
/// present never fails.
/// </para>
/// </remarks>
public sealed class OperationGraphBuilder
{
    /// <summary>The <c>list</c> read facet suffix.</summary>
    public const string ListSuffix = "list";

    /// <summary>The <c>detail</c> read facet suffix.</summary>
    public const string DetailSuffix = "detail";

    /// <summary>The <c>metadata</c> read facet suffix.</summary>
    public const string MetadataSuffix = "metadata";

    /// <summary>The <c>export</c> read facet suffix.</summary>
    public const string ExportSuffix = "export";

    /// <summary>The <c>create</c> write facet suffix.</summary>
    public const string CreateSuffix = "create";

    /// <summary>The <c>update</c> write facet suffix.</summary>
    public const string UpdateSuffix = "update";

    /// <summary>The <c>delete</c> write facet suffix.</summary>
    public const string DeleteSuffix = "delete";

    /// <summary>
    /// The name of the emitted runtime type representing an <c>export</c> facet's raw, unparsed response
    /// payload (design "Emitted TypeScript runtime contracts" — the <c>RawPayload</c> the export operation
    /// returns; Requirement 4.7). It is referenced by name here and declared once by the runtime emitter.
    /// </summary>
    public const string RawPayloadTypeName = "RawPayload";

    /// <summary>
    /// The recognized Vista facet suffixes. A path whose last segment is not one of these is not a Vista
    /// facet (for example a deferred grid-adapter endpoint) and is skipped (Requirement 4.1 — only the
    /// document's facets are modeled).
    /// </summary>
    private static readonly IReadOnlySet<string> KnownFacetSuffixes = new HashSet<string>(StringComparer.Ordinal)
    {
        ListSuffix, DetailSuffix, MetadataSuffix, ExportSuffix, CreateSuffix, UpdateSuffix, DeleteSuffix,
    };

    /// <summary>The concurrency-signalling response status codes (Requirement 5.6).</summary>
    private static readonly IReadOnlyList<string> ConcurrencyStatusCodes = ["428", "409"];

    /// <summary>
    /// Groups the resolved document's <c>paths</c> by view root and builds one <see cref="ViewModel"/> per
    /// view, each carrying the facets present for that view in deterministic order (Requirements 4.1, 4.2,
    /// 5.6, 9.2). Never fails and never throws for an absent facet; a view with no recognized facet is not
    /// produced.
    /// </summary>
    /// <param name="document">The resolved document whose <c>paths</c> and schema graph are read.</param>
    /// <param name="reLift">
    /// The envelope re-lifting outcome (task 7.2), used to collapse a list facet's monomorphized
    /// <c>ViewListResult_*</c> success component into the single generic <c>ViewListResult&lt;TRow&gt;</c>.
    /// </param>
    /// <param name="notices">The collector for any non-fatal notice (reserved; grouping records none today).</param>
    /// <returns>The views in deterministic ordinal order by view name (Requirement 9.2).</returns>
    /// <remarks>
    /// This overload classifies each facet's <see cref="FacetModel.Secured"/> flag from the operation's own
    /// per-operation <c>security</c> only, against an <see cref="SecurityPosture.Anonymous"/> posture. It is
    /// retained for backward compatibility with callers/tests that do not thread a document-level posture;
    /// the buffered pipeline (task 12.2) uses the posture-aware
    /// <see cref="Build(ResolvedDocument, EnvelopeReLiftResult, NoticeCollector, SecurityPosture)"/> overload
    /// so that operations secured only by the document-level default are correctly classified as secured
    /// (Requirements 7.2, 7.5).
    /// </remarks>
    public IReadOnlyList<ViewModel> Build(
        ResolvedDocument document,
        EnvelopeReLiftResult reLift,
        NoticeCollector notices) =>
        Build(document, reLift, notices, SecurityPosture.Anonymous);

    /// <summary>
    /// Groups the resolved document's <c>paths</c> by view root and builds one <see cref="ViewModel"/> per
    /// view, classifying each facet's <see cref="FacetModel.Secured"/> flag through the supplied
    /// <paramref name="posture"/> so that an operation secured only by the document-level default
    /// <c>security</c> (as the canonical Vista fixture is) is correctly marked secured (Requirements 7.2,
    /// 7.5). This is the overload the buffered pipeline (task 12.2) calls.
    /// </summary>
    /// <param name="document">The resolved document whose <c>paths</c> and schema graph are read.</param>
    /// <param name="reLift">
    /// The envelope re-lifting outcome (task 7.2), used to collapse a list facet's monomorphized
    /// <c>ViewListResult_*</c> success component into the single generic <c>ViewListResult&lt;TRow&gt;</c>.
    /// </param>
    /// <param name="notices">The collector for any non-fatal notice (reserved; grouping records none today).</param>
    /// <param name="posture">
    /// The document-level security posture used to classify each operation as secured or anonymous
    /// (Requirements 7.2, 7.5). It honors both per-operation <c>security</c> and the document-level default.
    /// </param>
    /// <returns>The views in deterministic ordinal order by view name (Requirement 9.2).</returns>
    public IReadOnlyList<ViewModel> Build(
        ResolvedDocument document,
        EnvelopeReLiftResult reLift,
        NoticeCollector notices,
        SecurityPosture posture)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reLift);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(posture);

        // 1. Flatten every (path, method) into a raw facet, skipping suffixes that are not Vista facets.
        var rawFacets = CollectRawFacets(document);

        // 2. Group the raw facets by view root; each group is one Mapped_View.
        var groups = new Dictionary<string, List<RawFacet>>(DeterministicOrder.Comparer);
        foreach (var facet in rawFacets)
        {
            if (!groups.TryGetValue(facet.ViewRoot, out var list))
            {
                list = new List<RawFacet>();
                groups[facet.ViewRoot] = list;
            }

            list.Add(facet);
        }

        // 3. Build a ViewModel per group.
        var views = new List<ViewModel>(groups.Count);
        foreach (var (viewRoot, facets) in groups)
        {
            views.Add(BuildView(viewRoot, facets, reLift, posture));
        }

        // 4. Views pre-sorted by view name (Requirement 9.2). Facets were already sorted per view.
        return DeterministicOrder.ByName(views, view => view.ViewName);
    }

    // Flattens the document's paths into raw facets, keeping only recognized Vista facet suffixes.
    private static List<RawFacet> CollectRawFacets(ResolvedDocument document)
    {
        var rawFacets = new List<RawFacet>();

        foreach (var (path, pathItem) in document.Document.Paths)
        {
            var (viewRoot, suffix) = SplitPath(path);
            if (suffix.Length == 0 || !KnownFacetSuffixes.Contains(suffix))
            {
                // Not a Vista facet endpoint (e.g. a deferred grid-adapter route) — skip it.
                continue;
            }

            foreach (var (method, operation) in pathItem.Operations)
            {
                var viewName = DeriveViewName(operation.OperationId, suffix, viewRoot);
                rawFacets.Add(new RawFacet(
                    viewRoot,
                    viewName,
                    suffix,
                    method.ToUpperInvariant(),
                    path,
                    operation));
            }
        }

        return rawFacets;
    }

    // Builds one view from its grouped raw facets: the row/crud bindings and the ordered facet models.
    private ViewModel BuildView(
        string viewRoot,
        List<RawFacet> facets,
        EnvelopeReLiftResult reLift,
        SecurityPosture posture)
    {
        // The view name is shared across a group's facets by construction; pick it deterministically (the
        // ordinally-smallest candidate) so a malformed operation id in one facet cannot perturb the result.
        var viewName = facets
            .Select(facet => facet.ViewName)
            .OrderBy(name => name, DeterministicOrder.Comparer)
            .First();

        var rowTypeName = DeriveRowTypeName(facets, reLift);
        var rowType = rowTypeName is null ? TsType.Unknown : DtoModelBuilder.Reference(rowTypeName);
        var crudType = DeriveCrudType(facets);

        var facetModels = facets
            .Select(facet => BuildFacet(facet, reLift, posture))
            .ToArray();

        // Facets pre-sorted by suffix, then method as a stable tie-break for the unusual case of two methods
        // sharing a suffix (Requirement 9.2).
        var orderedFacets = facetModels
            .OrderBy(facet => facet.Suffix, DeterministicOrder.Comparer)
            .ThenBy(facet => facet.HttpMethod, DeterministicOrder.Comparer)
            .ToArray();

        return new ViewModel(viewName, viewRoot, rowType, crudType, orderedFacets);
    }

    // Builds a single facet model from a raw facet, resolving its request/success types and concurrency.
    private FacetModel BuildFacet(RawFacet facet, EnvelopeReLiftResult reLift, SecurityPosture posture)
    {
        var requestType = ResolveRequestType(facet.Operation);
        var successType = ResolveSuccessType(facet, reLift);

        // Classify via the posture so an operation secured only by the document-level default `security`
        // (no per-operation `security`) is still marked secured (Requirements 7.2, 7.5). The Anonymous
        // posture used by the 3-arg overload preserves the prior per-operation-only behavior.
        var secured = posture.IsSecured(facet.Operation);
        var concurrency = DetermineConcurrency(facet.Operation);

        return new FacetModel(
            facet.Suffix,
            facet.HttpMethod,
            facet.Path,
            requestType,
            successType,
            secured,
            concurrency);
    }

    // The request type is the operation's request-body component reference, or null when it takes no body
    // (e.g. metadata) or declares an inline, unnamed body.
    private static TsType? ResolveRequestType(OpenApiOperation operation)
    {
        var schema = operation.RequestBody?.Schema;
        if (schema is null)
        {
            return null;
        }

        return TryGetSchemaRefName(schema, out var name) ? TsType.Named(name) : null;
    }

    // The success type is the lowest-numbered 2xx response's body: a re-lifted generic list envelope, a
    // plain named reference, or — for an inline/unnamed body such as the export octet-stream — the raw
    // payload runtime type. A facet with no 2xx body maps to the raw payload as a conservative default.
    private static TsType ResolveSuccessType(RawFacet facet, EnvelopeReLiftResult reLift)
    {
        var response = FindSuccessResponse(facet.Operation);
        var schema = response?.Schema;

        if (schema is not null && TryGetSchemaRefName(schema, out var componentName))
        {
            // A monomorphized ViewListResult_* success collapses to the single generic ViewListResult<TRow>.
            if (reLift.TryGetRowType(componentName, out var rowType))
            {
                return TsType.Generic(EnvelopeReLifter.GenericViewListResultName, [DtoModelBuilder.Reference(rowType)]);
            }

            return TsType.Named(componentName);
        }

        // No named body. The metadata facet always has a named body, so this is the export facet's inline
        // raw octet-stream payload (Requirement 4.7) or a write facet with no response body — both surface
        // as the raw payload runtime type.
        return TsType.Named(RawPayloadTypeName);
    }

    // A facet is token-bearing when its operation documents a 428 and/or 409 response (Requirement 5.6).
    private static ConcurrencyMode DetermineConcurrency(OpenApiOperation operation)
    {
        foreach (var status in ConcurrencyStatusCodes)
        {
            if (operation.Responses.ContainsKey(status))
            {
                return ConcurrencyMode.TokenBearing;
            }
        }

        return ConcurrencyMode.None;
    }

    // The view's row type: prefer the re-lifted list envelope's row component; fall back to the detail
    // success reference; otherwise unknown (no row-bearing facet present).
    private static string? DeriveRowTypeName(List<RawFacet> facets, EnvelopeReLiftResult reLift)
    {
        var listFacet = FindFacet(facets, ListSuffix);
        if (listFacet is not null)
        {
            var schema = FindSuccessResponse(listFacet.Operation)?.Schema;
            if (schema is not null && TryGetSchemaRefName(schema, out var componentName)
                && reLift.TryGetRowType(componentName, out var rowType))
            {
                return rowType;
            }
        }

        var detailFacet = FindFacet(facets, DetailSuffix);
        if (detailFacet is not null)
        {
            var schema = FindSuccessResponse(detailFacet.Operation)?.Schema;
            if (schema is not null && TryGetSchemaRefName(schema, out var componentName))
            {
                return componentName;
            }
        }

        return null;
    }

    // The view's write model (TCrud) reference, present only for a writable view: taken from the create
    // operation's request body, falling back to update's. A read-only view has no CrudType.
    private static TsType? DeriveCrudType(List<RawFacet> facets)
    {
        var createFacet = FindFacet(facets, CreateSuffix);
        var updateFacet = FindFacet(facets, UpdateSuffix);
        var writeFacet = createFacet ?? updateFacet;

        if (writeFacet is null)
        {
            // Not writable (Requirement 5.3): no create/update present.
            return null;
        }

        var schema = writeFacet.Operation.RequestBody?.Schema;
        return schema is not null && TryGetSchemaRefName(schema, out var name)
            ? DtoModelBuilder.Reference(name)
            : null;
    }

    private static RawFacet? FindFacet(List<RawFacet> facets, string suffix) =>
        facets.FirstOrDefault(facet => string.Equals(facet.Suffix, suffix, StringComparison.Ordinal));

    // Finds the lowest-numbered 2xx response deterministically (ordinal order over the status keys).
    private static OpenApiResponse? FindSuccessResponse(OpenApiOperation operation)
    {
        string? bestStatus = null;
        foreach (var status in operation.Responses.Keys)
        {
            if (status.StartsWith('2') &&
                (bestStatus is null || string.CompareOrdinal(status, bestStatus) < 0))
            {
                bestStatus = status;
            }
        }

        return bestStatus is null ? null : operation.Responses[bestStatus];
    }

    // Extracts a local component name from a schema's $ref, if any.
    private static bool TryGetSchemaRefName(OpenApiSchema schema, out string name)
    {
        if (!string.IsNullOrEmpty(schema.Ref) &&
            ResolvedDocument.TryGetComponentName(schema.Ref, ResolvedDocument.SchemaRefPrefix, out var found))
        {
            name = found;
            return true;
        }

        name = string.Empty;
        return false;
    }

    // Derives the view name from the operation id "{viewName}_{suffix}", falling back to the view root's
    // last segment when the operation id is missing or does not carry the expected suffix tail.
    private static string DeriveViewName(string operationId, string suffix, string viewRoot)
    {
        if (!string.IsNullOrEmpty(operationId))
        {
            var tail = "_" + suffix;
            if (operationId.EndsWith(tail, StringComparison.OrdinalIgnoreCase)
                && operationId.Length > tail.Length)
            {
                return operationId[..^tail.Length];
            }

            var lastUnderscore = operationId.LastIndexOf('_');
            if (lastUnderscore > 0)
            {
                return operationId[..lastUnderscore];
            }

            return operationId;
        }

        var (_, lastSegment) = SplitPath(viewRoot);
        return lastSegment.Length == 0 ? viewRoot : lastSegment;
    }

    // Splits a path into its view root (everything before the last segment) and its trailing segment
    // (the facet suffix). A trailing slash is ignored so "/a/b/list/" splits like "/a/b/list".
    private static (string ViewRoot, string Suffix) SplitPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return (trimmed, string.Empty);
        }

        return (trimmed[..lastSlash], trimmed[(lastSlash + 1)..]);
    }

    // A flattened (path, method) operation annotated with its parsed view root, view name, and facet suffix.
    private sealed record RawFacet(
        string ViewRoot,
        string ViewName,
        string Suffix,
        string HttpMethod,
        string Path,
        OpenApiOperation Operation);
}

/// <summary>
/// How a facet handles optimistic concurrency (Requirement 5.6). A <see cref="TokenBearing"/> facet's
/// operation documents a <c>428</c>/<c>409</c> response, so its emitted client accepts the caller-supplied
/// <c>ETag</c>/<c>If-Match</c> value and surfaces the missing-precondition and precondition-failed responses
/// as two distinct typed failures.
/// </summary>
public enum ConcurrencyMode
{
    /// <summary>The facet advertises no concurrency token.</summary>
    None,

    /// <summary>The facet documents <c>428</c> and/or <c>409</c> — it carries a concurrency token.</summary>
    TokenBearing,
}

/// <summary>
/// One mapped view's operation graph (design "The <c>ClientModel</c> IR"): its verbatim view name, its
/// endpoint root route, the by-name row type reference, an optional write-model (<c>TCrud</c>) reference for
/// a writable view, and the facets present for the view. Consumed by the view-client emitter (task 10.6/10.7)
/// and the security-posture step (task 7.6).
/// </summary>
/// <param name="ViewName">The view name, used verbatim (e.g. <c>Customers</c>).</param>
/// <param name="Route">The view's endpoint root route (e.g. <c>/api/views/customers</c>).</param>
/// <param name="RowType">The by-name reference to the view's <c>TRow</c> DTO (Requirement 2.5).</param>
/// <param name="CrudType">
/// The by-name reference to the view's <c>TCrud</c> write-model DTO, or <c>null</c> for a read-only view.
/// </param>
/// <param name="Facets">
/// The view's facets, pre-sorted by suffix (Requirement 9.2). Only facets present in the document appear
/// (Requirement 4.1).
/// </param>
public sealed record ViewModel(
    string ViewName,
    string Route,
    TsType RowType,
    TsType? CrudType,
    IReadOnlyList<FacetModel> Facets);

/// <summary>
/// One facet of a view (design "The <c>ClientModel</c> IR"): its suffix, the HTTP method and full path the
/// document declares (Requirement 4.2), its request/success type references, whether the operation is
/// secured (per-operation <c>security</c>; full posture classification is task 7.6), and its
/// <see cref="ConcurrencyMode"/> (Requirement 5.6).
/// </summary>
/// <param name="Suffix">The facet suffix (<c>list</c>/<c>detail</c>/<c>metadata</c>/<c>export</c>/<c>create</c>/<c>update</c>/<c>delete</c>).</param>
/// <param name="HttpMethod">The uppercased HTTP method the document declares (e.g. <c>POST</c>, <c>GET</c>).</param>
/// <param name="Path">The full operation path from the document (e.g. <c>/api/views/customers/list</c>).</param>
/// <param name="RequestType">
/// The by-name reference to the request body type, or <c>null</c> when the operation takes no body
/// (e.g. <c>metadata</c>).
/// </param>
/// <param name="SuccessType">
/// The success payload type: a named envelope, the re-lifted generic <c>ViewListResult&lt;TRow&gt;</c>, or the
/// raw payload reference for the export facet (Requirement 4.7).
/// </param>
/// <param name="Secured">Whether the operation declares any per-operation <c>security</c> requirement.</param>
/// <param name="Concurrency">The facet's concurrency mode (Requirement 5.6).</param>
public sealed record FacetModel(
    string Suffix,
    string HttpMethod,
    string Path,
    TsType? RequestType,
    TsType SuccessType,
    bool Secured,
    ConcurrencyMode Concurrency);
