using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Resolve;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// The classified kind of a declared security scheme (design §A.5 step 5 "Security posture"). Only the
/// distinction the client's auth emission (task 10.2) and client-context (task 10.5) care about is modeled:
/// whether a scheme is the HTTP <c>bearer</c> scheme for which the default <c>Authorization: Bearer</c>
/// header applies (Requirement 7.2), versus any other declared scheme kind.
/// </summary>
public enum SecuritySchemeKind
{
    /// <summary>
    /// An HTTP <c>bearer</c> scheme (<c>type == "http"</c> and <c>scheme == "bearer"</c>). This is the
    /// scheme for which the emitted client attaches the default <c>Authorization: Bearer &lt;token&gt;</c>
    /// header (Requirement 7.2).
    /// </summary>
    HttpBearer,

    /// <summary>
    /// Any other declared scheme (a non-<c>bearer</c> HTTP scheme, <c>apiKey</c>, <c>oauth2</c>,
    /// <c>openIdConnect</c>, …). It is preserved faithfully so downstream emission can decide how to attach
    /// its credential, but no default bearer header is implied.
    /// </summary>
    Other,
}

/// <summary>
/// One declared security scheme from <c>#/components/securitySchemes</c>, classified for the client's
/// authorization posture. The raw <c>type</c>/<c>scheme</c>/<c>bearerFormat</c> are preserved verbatim so the
/// posture stays faithful to what the document declares (design §A.5 step 5); <see cref="Kind"/> is the
/// derived classification the emitters key off.
/// </summary>
/// <param name="Name">The scheme's declared name (its key under <c>securitySchemes</c>), used verbatim.</param>
/// <param name="Kind">The derived classification (see <see cref="SecuritySchemeKind"/>).</param>
/// <param name="Type">The raw OpenAPI scheme <c>type</c> (e.g. <c>"http"</c>).</param>
/// <param name="Scheme">The raw HTTP <c>scheme</c> (e.g. <c>"bearer"</c>), or <c>null</c>.</param>
/// <param name="BearerFormat">The raw <c>bearerFormat</c> hint (e.g. <c>"JWT"</c>), or <c>null</c>.</param>
public sealed record SecuritySchemeModel(
    string Name,
    SecuritySchemeKind Kind,
    string Type,
    string? Scheme,
    string? BearerFormat)
{
    /// <summary>Whether this is the HTTP <c>bearer</c> scheme the default credential targets (Requirement 7.2).</summary>
    public bool IsHttpBearer => Kind == SecuritySchemeKind.HttpBearer;
}

/// <summary>
/// The document-level security posture (design "The <c>ClientModel</c> IR" — <c>SecurityPosture Security</c>):
/// the declared security schemes and the classification of any operation as <em>secured</em> or
/// <em>anonymous</em>. It is produced by <see cref="SecurityPostureBuilder"/> and consumed by the auth
/// emitter (task 10.2) and client-context (task 10.5) to decide, per operation, whether the client attaches a
/// credential via the consumer-supplied <c>AuthProvider</c> (default an HTTP bearer <c>Authorization</c>
/// header, Requirement 7.2) or sends the request without a credential (Requirement 7.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Classification (Requirements 7.2, 7.5).</b> An operation is <em>secured</em> when it — or, when the
/// operation itself declares none, the document-level default requirement — requires at least one
/// <em>declared</em> scheme. An operation that requires nothing (and inherits no document default) is
/// <em>anonymous</em>. A requirement that names a scheme absent from <see cref="Schemes"/> does not by itself
/// secure an operation, so the posture never claims a credential is needed for a scheme the document does not
/// declare.
/// </para>
/// <para>
/// <b>Document-level default (OpenAPI top-level <c>security</c>).</b> OpenAPI lets a document declare a
/// top-level <c>security</c> requirement that applies to every operation unless the operation overrides it.
/// <see cref="DocumentDefaultSchemeNames"/> carries those default scheme names. The internal
/// <see cref="OpenApiDocument"/> model captures the top-level <c>security</c> array in
/// <see cref="OpenApiDocument.Security"/>, and <see cref="SecurityPostureBuilder.Build(ResolvedDocument)"/>
/// passes it through, so a document that secures its operations <em>only</em> at the top level (as the
/// canonical Vista fixture does) classifies every operation as secured. The
/// <see cref="SecurityPostureBuilder.Build(ResolvedDocument, IReadOnlyList{OpenApiSecurityRequirement})"/>
/// overload still accepts an explicit document default for callers that override it.
/// </para>
/// <para>
/// Because the current model cannot distinguish an operation that omits <c>security</c> from one that sets an
/// explicit empty <c>security: []</c> (an explicit opt-out), an operation with no requirements inherits the
/// document default when one is supplied. This matches the common Vista shape (uniform posture across a
/// view's facets) and is the conservative, secure-by-default choice.
/// </para>
/// <para>
/// <b>Deterministic and pure (Requirement 9.2).</b> <see cref="Schemes"/> and
/// <see cref="DocumentDefaultSchemeNames"/> are stored pre-sorted by the fixed ordinal, case-sensitive
/// <see cref="DeterministicOrder"/> comparison, independent of the document's enumeration order. All members
/// are pure and perform no I/O.
/// </para>
/// </remarks>
/// <param name="Schemes">The declared security schemes, ordered by name (Requirement 9.2).</param>
/// <param name="DocumentDefaultSchemeNames">
/// The scheme names of the document-level default <c>security</c> requirement, ordered and de-duplicated;
/// empty when the document declares none or the top-level requirement is not modeled (see remarks).
/// </param>
public sealed record SecurityPosture(
    IReadOnlyList<SecuritySchemeModel> Schemes,
    IReadOnlyList<string> DocumentDefaultSchemeNames)
{
    /// <summary>A posture for a document that declares no security scheme at all (fully anonymous).</summary>
    public static SecurityPosture Anonymous { get; } = new([], []);

    /// <summary>
    /// Whether the document declares <em>any</em> security scheme under <c>components.securitySchemes</c>
    /// (Requirement 7.2 vs 7.5). This is a document-wide fact; an individual operation may still be
    /// anonymous — use <see cref="IsSecured"/> for the per-operation decision.
    /// </summary>
    public bool HasAnySecurityScheme => Schemes.Count > 0;

    /// <summary>
    /// The declared HTTP <c>bearer</c> scheme the default credential targets (Requirement 7.2), or
    /// <c>null</c> when the document declares no bearer scheme. When several bearer schemes are declared, the
    /// ordinally-first by name is returned so the choice is deterministic (Requirement 9.2).
    /// </summary>
    public SecuritySchemeModel? DefaultBearerScheme =>
        Schemes.FirstOrDefault(scheme => scheme.Kind == SecuritySchemeKind.HttpBearer);

    /// <summary>
    /// Classifies a single operation as secured (<c>true</c>) or anonymous (<c>false</c>) per Requirements
    /// 7.2 and 7.5. The operation is secured when its own <c>security</c> requires at least one declared
    /// scheme, or — when the operation declares none — when the document-level default
    /// (<see cref="DocumentDefaultSchemeNames"/>) does. See the type remarks for the current top-level
    /// modeling caveat.
    /// </summary>
    /// <param name="operation">The operation to classify.</param>
    /// <returns><c>true</c> when the operation is secured; otherwise <c>false</c>.</returns>
    public bool IsSecured(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // A per-operation requirement that names a declared scheme secures the operation and overrides any
        // document-level default (OpenAPI operation-level override semantics).
        if (RequiresDeclaredScheme(operation.Security))
        {
            return true;
        }

        // No per-operation requirement: inherit the document-level default (when modeled/supplied).
        return operation.Security.Count == 0 && DocumentDefaultSchemeNames.Any(IsDeclaredScheme);
    }

    // True when any requirement in the set names a scheme this posture actually declares.
    private bool RequiresDeclaredScheme(IReadOnlyList<OpenApiSecurityRequirement> requirements) =>
        requirements.Any(requirement => IsDeclaredScheme(requirement.SchemeName));

    // True when the given name matches a declared scheme (ordinal, case-sensitive as declared).
    private bool IsDeclaredScheme(string name) =>
        Schemes.Any(scheme => string.Equals(scheme.Name, name, StringComparison.Ordinal));
}
