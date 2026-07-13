using System.Collections.Generic;

namespace a2n.Vista.OpenApi;

/// <summary>
/// The seven core Vista HTTP facets exposed per view (Decision Log D110). Read facets
/// (<see cref="List"/>/<see cref="Detail"/>/<see cref="Metadata"/>/<see cref="Export"/>) exist for every
/// view; the write facets (<see cref="Create"/>/<see cref="Update"/>/<see cref="Delete"/>) exist only for a
/// writable view (<c>!IsReadOnly</c>).
/// </summary>
public enum Facet
{
    /// <summary><c>POST {Route}/list</c> — the paged, filtered list.</summary>
    List,

    /// <summary><c>POST {Route}/detail</c> — a single row by key.</summary>
    Detail,

    /// <summary><c>GET {Route}/metadata</c> — the view's shape/limits descriptor.</summary>
    Metadata,

    /// <summary><c>POST {Route}/export</c> — the list projected to an export format.</summary>
    Export,

    /// <summary><c>POST {Route}/create</c> — create a row (writable views only).</summary>
    Create,

    /// <summary><c>POST {Route}/update</c> — update a row (writable views only).</summary>
    Update,

    /// <summary><c>POST {Route}/delete</c> — delete a row (writable views only).</summary>
    Delete,
}

/// <summary>The request-body envelope a facet accepts.</summary>
public enum FacetRequestBody
{
    /// <summary>No request body (the metadata GET).</summary>
    None,

    /// <summary><c>VistaListRequestBody</c> (list and export; export adds a <c>format</c>).</summary>
    List,

    /// <summary><c>VistaDetailRequestBody</c> (detail).</summary>
    Detail,

    /// <summary><c>VistaWriteRequestBody</c> (create/update/delete).</summary>
    Write,
}

/// <summary>The success-response body shape a facet returns.</summary>
public enum FacetSuccessBody
{
    /// <summary><c>ViewListResult&lt;TRow&gt;</c> (list; export may return this or a file).</summary>
    ViewListResult,

    /// <summary>The view's <c>TRow</c> (detail).</summary>
    Row,

    /// <summary><c>VistaMetadataResponse</c> (metadata).</summary>
    Metadata,

    /// <summary><c>VistaWriteResponse</c> — the created key (create).</summary>
    WriteResponse,

    /// <summary>A <c>200</c>/<c>204</c> with no body of interest (update/delete).</summary>
    NoContentOr200,
}

/// <summary>Whether a facet is present on every view or only on writable views.</summary>
public enum FacetAvailability
{
    /// <summary>Present on every mapped view.</summary>
    Always,

    /// <summary>Present only when the view is writable (<c>!IsReadOnly</c>).</summary>
    WritableOnly,
}

/// <summary>
/// One immutable row of the facet→operation table (spec openapi-emitter, task 5.1; Data Models). This is the
/// single endpoint-parity source the document builder (task 5.2) iterates: it fully describes a facet's HTTP
/// method, path suffix, request/response body kind, always-present error codes, and the conditional error
/// posture, so the builder never re-derives any of this ad hoc.
/// </summary>
/// <remarks>
/// <para>
/// The <c>403</c> (forbidden) response is uniform: it applies to <b>every</b> facet when the app is not
/// anonymous, so it is expressed as <see cref="ForbiddenWhenNotAnonymous"/> (true for all rows) rather than
/// embedded in <see cref="AlwaysErrorCodes"/>. The task 5.3 error layer attaches it uniformly.
/// </para>
/// <para>
/// The concurrency responses (<c>428</c> Precondition Required and <c>409</c> Conflict) apply only to
/// <see cref="Facet.Update"/>/<see cref="Facet.Delete"/> and only when the writable view declares a
/// concurrency token; they are gated by <see cref="ConcurrencyErrorsWhenTokenDeclared"/> so the builder
/// layers them conditionally.
/// </para>
/// </remarks>
public sealed record FacetOperation
{
    /// <summary>The facet this row describes.</summary>
    public required Facet Facet { get; init; }

    /// <summary>The HTTP method (<c>GET</c> for metadata, <c>POST</c> for every other facet).</summary>
    public required string HttpMethod { get; init; }

    /// <summary>
    /// The path suffix appended to the view's <c>Route</c> to form the operation path (for example
    /// <c>list</c> → <c>{Route}/list</c>). Also the facet token in the <c>operationId</c>
    /// (<c>{viewName}_{PathSuffix}</c>).
    /// </summary>
    public required string PathSuffix { get; init; }

    /// <summary>The request-body envelope this facet accepts.</summary>
    public required FacetRequestBody RequestBody { get; init; }

    /// <summary>The success-response body shape this facet returns.</summary>
    public required FacetSuccessBody SuccessBody { get; init; }

    /// <summary>Whether this facet exists on every view or only on writable views.</summary>
    public required FacetAvailability Availability { get; init; }

    /// <summary>
    /// The unconditionally documented error status codes for this facet (a <c>400</c> on every
    /// body-bearing operation and a <c>404</c> on detail/update/delete). Excludes the uniform <c>403</c>
    /// (see <see cref="ForbiddenWhenNotAnonymous"/>) and the token-gated <c>428</c>/<c>409</c> (see
    /// <see cref="ConcurrencyErrorsWhenTokenDeclared"/>), which the builder layers conditionally.
    /// </summary>
    public required IReadOnlyList<int> AlwaysErrorCodes { get; init; }

    /// <summary>Whether a <c>403</c> response is documented when the app is not anonymous (true for every facet).</summary>
    public required bool ForbiddenWhenNotAnonymous { get; init; }

    /// <summary>
    /// Whether the <c>428</c>/<c>409</c> concurrency responses are documented when the writable view
    /// declares a concurrency token (true for update/delete only).
    /// </summary>
    public required bool ConcurrencyErrorsWhenTokenDeclared { get; init; }

    /// <summary>Whether this facet is a write facet (present only on writable views).</summary>
    public bool IsWriteFacet => Availability == FacetAvailability.WritableOnly;

    /// <summary>Whether this facet carries a request body.</summary>
    public bool HasRequestBody => RequestBody != FacetRequestBody.None;
}

/// <summary>
/// The fixed facet→operation table — the single endpoint-parity source of truth (spec openapi-emitter,
/// task 5.1; design.md "Data Models"). Encodes, once and declaratively, the method, path suffix,
/// request/response body kind, error posture, and availability of each core Vista facet, in the
/// deterministic table order (Requirement 9.2). The document builder (task 5.2) iterates this table so the
/// emitted operation set is, by construction, exactly the live <c>View_Operation_Set</c>.
/// </summary>
public static class FacetOperations
{
    /// <summary>
    /// The seven core facets in deterministic table order (list, detail, metadata, export, create, update,
    /// delete). Adapter endpoints are intentionally absent — v1 documents only core facets
    /// (Requirement 12.1; <see cref="VistaOpenApiOptions.IncludeAdapterEndpoints"/> defaults false).
    /// </summary>
    public static IReadOnlyList<FacetOperation> All { get; } = new[]
    {
        new FacetOperation
        {
            Facet = Facet.List,
            HttpMethod = "POST",
            PathSuffix = "list",
            RequestBody = FacetRequestBody.List,
            SuccessBody = FacetSuccessBody.ViewListResult,
            Availability = FacetAvailability.Always,
            AlwaysErrorCodes = new[] { 400 },
            ForbiddenWhenNotAnonymous = true,
            ConcurrencyErrorsWhenTokenDeclared = false,
        },
        new FacetOperation
        {
            Facet = Facet.Detail,
            HttpMethod = "POST",
            PathSuffix = "detail",
            RequestBody = FacetRequestBody.Detail,
            SuccessBody = FacetSuccessBody.Row,
            Availability = FacetAvailability.Always,
            AlwaysErrorCodes = new[] { 400, 404 },
            ForbiddenWhenNotAnonymous = true,
            ConcurrencyErrorsWhenTokenDeclared = false,
        },
        new FacetOperation
        {
            Facet = Facet.Metadata,
            HttpMethod = "GET",
            PathSuffix = "metadata",
            RequestBody = FacetRequestBody.None,
            SuccessBody = FacetSuccessBody.Metadata,
            Availability = FacetAvailability.Always,
            AlwaysErrorCodes = System.Array.Empty<int>(),
            ForbiddenWhenNotAnonymous = true,
            ConcurrencyErrorsWhenTokenDeclared = false,
        },
        new FacetOperation
        {
            Facet = Facet.Export,
            HttpMethod = "POST",
            PathSuffix = "export",
            RequestBody = FacetRequestBody.List,
            SuccessBody = FacetSuccessBody.ViewListResult,
            Availability = FacetAvailability.Always,
            AlwaysErrorCodes = new[] { 400 },
            ForbiddenWhenNotAnonymous = true,
            ConcurrencyErrorsWhenTokenDeclared = false,
        },
        new FacetOperation
        {
            Facet = Facet.Create,
            HttpMethod = "POST",
            PathSuffix = "create",
            RequestBody = FacetRequestBody.Write,
            SuccessBody = FacetSuccessBody.WriteResponse,
            Availability = FacetAvailability.WritableOnly,
            AlwaysErrorCodes = new[] { 400, 404 },
            ForbiddenWhenNotAnonymous = true,
            ConcurrencyErrorsWhenTokenDeclared = false,
        },
        new FacetOperation
        {
            Facet = Facet.Update,
            HttpMethod = "POST",
            PathSuffix = "update",
            RequestBody = FacetRequestBody.Write,
            SuccessBody = FacetSuccessBody.NoContentOr200,
            Availability = FacetAvailability.WritableOnly,
            AlwaysErrorCodes = new[] { 400, 404 },
            ForbiddenWhenNotAnonymous = true,
            ConcurrencyErrorsWhenTokenDeclared = true,
        },
        new FacetOperation
        {
            Facet = Facet.Delete,
            HttpMethod = "POST",
            PathSuffix = "delete",
            RequestBody = FacetRequestBody.Write,
            SuccessBody = FacetSuccessBody.NoContentOr200,
            Availability = FacetAvailability.WritableOnly,
            AlwaysErrorCodes = new[] { 400, 404 },
            ForbiddenWhenNotAnonymous = true,
            ConcurrencyErrorsWhenTokenDeclared = true,
        },
    };

    /// <summary>
    /// The facets present on a view given its writability: every facet for a writable view, only the
    /// non-write (<see cref="FacetAvailability.Always"/>) facets for a read-only view. Iterating this gives
    /// the exact <c>View_Operation_Set</c> for that view (endpoint parity, Requirement 1).
    /// </summary>
    /// <param name="isReadOnly">Whether the view is read-only (<c>ViewMetadata.IsReadOnly</c>).</param>
    public static IEnumerable<FacetOperation> ForView(bool isReadOnly)
    {
        foreach (var operation in All)
        {
            if (isReadOnly && operation.IsWriteFacet)
            {
                continue;
            }

            yield return operation;
        }
    }
}
