using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Resolve;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// The security-posture classification step (task 7.6; design §A.5 step 5 "Security posture"). Reads the
/// resolved document's <c>components.securitySchemes</c>, classifies each declared scheme (identifying the
/// HTTP <c>bearer</c> scheme that informs the default credential — Requirement 7.2), and produces the
/// document-level <see cref="SecurityPosture"/> the auth emitter (task 10.2) and client-context (task 10.5)
/// use to decide, per operation, whether the client attaches a credential (secured) or sends the request
/// without one (Requirement 7.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Faithful to what is declared.</b> A scheme is classified <see cref="SecuritySchemeKind.HttpBearer"/>
/// only when it declares <c>type: http</c> and <c>scheme: bearer</c> (the comparison is case-insensitive, as
/// the HTTP authentication-scheme name is case-insensitive per RFC 7235); every other declared scheme is
/// <see cref="SecuritySchemeKind.Other"/>, preserved with its raw <c>type</c>/<c>scheme</c>/<c>bearerFormat</c>
/// so no declared detail is lost.
/// </para>
/// <para>
/// <b>Document-level default.</b> OpenAPI permits a top-level <c>security</c> requirement that applies to
/// every operation unless overridden. The internal <see cref="OpenApiDocument"/> model now captures that
/// top-level array in <see cref="OpenApiDocument.Security"/>, so <see cref="Build(ResolvedDocument)"/> passes
/// it through as the document-level default. A document that secures its operations <em>only</em> at the top
/// level (as the canonical Vista fixture does) therefore classifies as secured. The
/// <see cref="Build(ResolvedDocument, IReadOnlyList{OpenApiSecurityRequirement})"/> overload still accepts an
/// explicit default for callers that need to override it.
/// </para>
/// <para>
/// <b>Deterministic and pure (Requirement 9.2).</b> The declared schemes and the document-default scheme
/// names are ordered by the fixed ordinal, case-sensitive <see cref="DeterministicOrder"/> comparison,
/// independent of the document's enumeration order. The builder performs no I/O and mutates nothing.
/// </para>
/// </remarks>
public sealed class SecurityPostureBuilder
{
    /// <summary>The OpenAPI scheme <c>type</c> value for an HTTP authentication scheme.</summary>
    private const string HttpSchemeType = "http";

    /// <summary>The HTTP authentication <c>scheme</c> value for a bearer token.</summary>
    private const string BearerSchemeName = "bearer";

    /// <summary>
    /// Builds the security posture from the resolved document's <c>components.securitySchemes</c>, honoring
    /// the document-level (root) <c>security</c> as the default requirement for operations that declare none
    /// of their own. The default is taken from <see cref="OpenApiDocument.Security"/> on the underlying
    /// document, so a document secured only at the top level classifies correctly.
    /// </summary>
    /// <param name="document">The resolved document whose declared schemes and top-level default are read.</param>
    /// <returns>The document-level <see cref="SecurityPosture"/>.</returns>
    public SecurityPosture Build(ResolvedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Build(document, document.Document.Security);
    }

    /// <summary>
    /// Builds the security posture, using <paramref name="documentDefaultSecurity"/> as the document-level
    /// default requirement that applies to operations declaring no <c>security</c> of their own. This overload
    /// is the forward-compatible entry point for when the parse stage carries the top-level <c>security</c>
    /// array (see the type remarks).
    /// </summary>
    /// <param name="document">The resolved document whose declared schemes are read.</param>
    /// <param name="documentDefaultSecurity">
    /// The document-level default <c>security</c> requirements, or <c>null</c>/empty when the document declares
    /// no top-level default.
    /// </param>
    /// <returns>The document-level <see cref="SecurityPosture"/>.</returns>
    public SecurityPosture Build(
        ResolvedDocument document,
        IReadOnlyList<OpenApiSecurityRequirement>? documentDefaultSecurity)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Classify every declared scheme, then order by name for determinism (Requirement 9.2).
        var schemes = document.SecuritySchemes
            .Select(entry => Classify(entry.Key, entry.Value))
            .ToArray();
        var orderedSchemes = DeterministicOrder.ByName(schemes, scheme => scheme.Name);

        // The document-level default scheme names, de-duplicated and ordered for determinism.
        var defaultNames = (documentDefaultSecurity ?? [])
            .Select(requirement => requirement.SchemeName)
            .Distinct(DeterministicOrder.Comparer);
        var orderedDefaultNames = DeterministicOrder.OrderNames(defaultNames);

        return new SecurityPosture(orderedSchemes, orderedDefaultNames);
    }

    // Classifies a single declared scheme, identifying the HTTP bearer scheme the default credential targets.
    private static SecuritySchemeModel Classify(string name, OpenApiSecurityScheme scheme)
    {
        var kind = IsHttpBearer(scheme) ? SecuritySchemeKind.HttpBearer : SecuritySchemeKind.Other;
        return new SecuritySchemeModel(name, kind, scheme.Type, scheme.Scheme, scheme.BearerFormat);
    }

    // An HTTP bearer scheme: type == "http" && scheme == "bearer" (case-insensitive per RFC 7235).
    private static bool IsHttpBearer(OpenApiSecurityScheme scheme) =>
        string.Equals(scheme.Type, HttpSchemeType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(scheme.Scheme, BearerSchemeName, StringComparison.OrdinalIgnoreCase);
}
