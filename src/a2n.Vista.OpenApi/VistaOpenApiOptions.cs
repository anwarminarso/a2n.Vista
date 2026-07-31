using System;

namespace a2n.Vista.OpenApi;

/// <summary>
/// Host-supplied configuration for the Vista OpenAPI emitter (spec openapi-emitter, task 5.1;
/// Requirements 8.4, 11.2, 12.1). Every value carries a safe default so <c>AddVistaOpenApi()</c> works with
/// no arguments; a host overrides only what it needs.
/// </summary>
/// <remarks>
/// <para>
/// The options are validated once, at registration time (<c>AddVistaOpenApi()</c>), via
/// <see cref="Validate"/> — invalid options fail fast with a clear <see cref="ArgumentException"/> rather
/// than surfacing later at request time (see design.md "Error Handling").
/// </para>
/// <para>
/// <see cref="IncludeAdapterEndpoints"/> is <see langword="false"/> in v1: the emitter documents only the
/// core Vista endpoints and treats grid-adapter documentation (D111–D116) as a later, opt-in extension
/// (Requirement 12.1).
/// </para>
/// </remarks>
public sealed class VistaOpenApiOptions
{
    /// <summary>The <c>info.title</c> of the emitted document.</summary>
    public string DocumentTitle { get; set; } = "a2n.Vista API";

    /// <summary>
    /// The <c>info.version</c> of the emitted document, or <see langword="null"/> to default to the
    /// emitting assembly's informational version at build time (Requirement 8.4).
    /// </summary>
    public string? DocumentVersion { get; set; }

    /// <summary>
    /// The OpenAPI specification version the document targets. Defaults to <c>3.0.4</c> (a 3.0.x document);
    /// a 3.1 default is an assumption still to be finalized (Requirement 8.1).
    /// </summary>
    public string OpenApiVersion { get; set; } = "3.0.4";

    /// <summary>
    /// The route the <c>Serve_Endpoint</c> is mapped at (Requirement 11.1). Must be an absolute path
    /// (starts with <c>/</c>).
    /// </summary>
    public string EndpointPath { get; set; } = "/openapi/v1.json";

    /// <summary>
    /// The security scheme to emit and attach to every operation when the app is not anonymous, or
    /// <see langword="null"/> to use the default HTTP <c>bearer</c> scheme (Requirements 7.1, 7.2). When the
    /// app has opted into anonymous access, no scheme is emitted regardless of this value.
    /// </summary>
    public VistaSecurityScheme? Security { get; set; }

    /// <summary>
    /// Whether to document grid-adapter endpoints. Always <see langword="false"/> in v1 (Requirement 12.1);
    /// exposed as an extension hook for a later phase.
    /// </summary>
    public bool IncludeAdapterEndpoints { get; set; }

    /// <summary>
    /// Whether <c>MapVistaOpenApi()</c> attaches <c>RequireAuthorization()</c> to the mapped document
    /// endpoint. Defaults to <see langword="true"/> (secure by default).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An ASP.NET Core endpoint that carries no authorization metadata is <b>anonymous</b> even when
    /// <c>UseAuthentication</c>/<c>UseAuthorization</c> are in the pipeline. Since the document publishes
    /// every mapped view's route, operation set, writability, and row/write schemas, the endpoint defaults
    /// to requiring an authorized caller rather than inheriting an implicitly open posture.
    /// </para>
    /// <para>
    /// The requirement is skipped when the host explicitly opted into anonymous access through the D94
    /// switch (<c>AddVistaEndpoints(b =&gt; b.AllowAnonymousAccess())</c>): in that posture the views
    /// themselves are open by reviewed choice, and there may be no authentication scheme to authorize
    /// against. Set this to <see langword="false"/> to publish the document anonymously while the views
    /// stay authorized — a deliberate, reviewable opt-out.
    /// </para>
    /// </remarks>
    public bool RequireAuthorization { get; set; } = true;

    /// <summary>
    /// Validates the options at configuration time, throwing a descriptive <see cref="ArgumentException"/>
    /// on the first invalid value so misconfiguration fails fast rather than at request time.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="DocumentTitle"/> is empty, <see cref="OpenApiVersion"/> is empty or not a 3.x
    /// version, or <see cref="EndpointPath"/> is empty or not an absolute path.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DocumentTitle))
        {
            throw new ArgumentException(
                "VistaOpenApiOptions.DocumentTitle must be a non-empty document title.",
                nameof(DocumentTitle));
        }

        if (string.IsNullOrWhiteSpace(OpenApiVersion))
        {
            throw new ArgumentException(
                "VistaOpenApiOptions.OpenApiVersion must be a non-empty OpenAPI version string.",
                nameof(OpenApiVersion));
        }

        if (!IsSupportedOpenApiVersion(OpenApiVersion))
        {
            throw new ArgumentException(
                $"VistaOpenApiOptions.OpenApiVersion '{OpenApiVersion}' is not a supported OpenAPI 3.x version.",
                nameof(OpenApiVersion));
        }

        if (string.IsNullOrWhiteSpace(EndpointPath))
        {
            throw new ArgumentException(
                "VistaOpenApiOptions.EndpointPath must be a non-empty endpoint path.",
                nameof(EndpointPath));
        }

        if (!EndpointPath.StartsWith('/'))
        {
            throw new ArgumentException(
                $"VistaOpenApiOptions.EndpointPath '{EndpointPath}' must be an absolute path starting with '/'.",
                nameof(EndpointPath));
        }
    }

    /// <summary>
    /// Determines whether <paramref name="version"/> is a supported OpenAPI 3.x version: a dotted version
    /// whose major component is <c>3</c> (for example <c>3.0.4</c> or <c>3.1.0</c>).
    /// </summary>
    private static bool IsSupportedOpenApiVersion(string version)
    {
        var separator = version.IndexOf('.');
        if (separator <= 0)
        {
            return false;
        }

        var major = version.AsSpan(0, separator);
        return major.SequenceEqual("3");
    }
}

/// <summary>
/// A configurable OpenAPI security scheme (Requirements 7.1, 7.2). The Vista default is an HTTP
/// <c>bearer</c> scheme; a host may supply a different one (for example an API-key or OAuth flow), which the
/// emitter emits under <c>components.securitySchemes</c> and references from every operation.
/// </summary>
/// <param name="Name">The scheme key under <c>components.securitySchemes</c> (for example <c>bearer</c>).</param>
/// <param name="Type">The OpenAPI scheme type (for example <c>http</c> or <c>apiKey</c>).</param>
/// <param name="Scheme">The HTTP authorization scheme for an <c>http</c>-type scheme (for example <c>bearer</c>).</param>
/// <param name="BearerFormat">An optional bearer format hint (for example <c>JWT</c>).</param>
public sealed record VistaSecurityScheme(string Name, string Type, string Scheme, string? BearerFormat);
