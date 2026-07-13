using System.Text.Json.Serialization;

namespace a2n.Vista.OpenApi.Model;

// Hand-authored, minimal OpenAPI v3.x object model (Decision Log D127).
//
// This model covers *exactly* the subset the Vista emitter produces — no more — so that it can be
// serialized AOT-clean and byte-deterministic through a source-generated JsonSerializerContext
// (see VistaOpenApiJsonContext), avoiding a heavy transitive Microsoft.OpenApi dependency.
//
// Determinism (Requirement 9): every map on this model is typed as IReadOnlyDictionary<string, T> and is
// expected to be populated with an ordinal-ordered dictionary (see OpenApiCollections.CreateMap), so the
// serialized key order is independent of insertion order and stable across processes and cultures.
//
// Cleanliness: null / empty members are omitted from the JSON output (the serializer context sets
// DefaultIgnoreCondition = WhenWritingNull), so an unset property never widens the document. Callers set a
// collection to null (never to an empty instance) when it has no members.

/// <summary>
/// The root OpenAPI v3.x document (<c>openapi</c>, <c>info</c>, <c>paths</c>, <c>components</c>,
/// top-level <c>security</c>). Serialized deterministically via <c>VistaOpenApiJsonContext</c>.
/// </summary>
public sealed record OpenApiDocument
{
    /// <summary>The OpenAPI specification version this document targets (for example <c>3.0.4</c>).</summary>
    public required string Openapi { get; init; }

    /// <summary>Document metadata (title + version); always populated.</summary>
    public required OpenApiInfo Info { get; init; }

    /// <summary>
    /// The available paths keyed by the full route (for example <c>/api/views/widgets/list</c>), each
    /// carrying its per-method operations. Populated with an ordinal-ordered map for determinism.
    /// </summary>
    public IReadOnlyDictionary<string, OpenApiPathItem>? Paths { get; init; }

    /// <summary>The reusable component bag (<c>schemas</c>, <c>securitySchemes</c>), or <see langword="null"/>.</summary>
    public OpenApiComponents? Components { get; init; }

    /// <summary>
    /// An optional document-level security requirement. Each requirement maps a security-scheme name to its
    /// list of scopes (empty for HTTP bearer). <see langword="null"/> when the app is anonymous.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>? Security { get; init; }
}

/// <summary>The OpenAPI <c>info</c> object: the document's title and version (Requirement 8.1).</summary>
public sealed record OpenApiInfo
{
    /// <summary>The human-readable document title.</summary>
    public required string Title { get; init; }

    /// <summary>The document version (host option, defaulting to the emitting assembly version).</summary>
    public required string Version { get; init; }

    /// <summary>An optional document description.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// The reusable <c>components</c> bag. Vista emits only <c>schemas</c> (envelopes, <c>FilterNode</c>,
/// <c>ProblemDetails</c>, and per-view DTOs) and <c>securitySchemes</c>.
/// </summary>
public sealed record OpenApiComponents
{
    /// <summary>The shared schemas keyed by schema name; ordinal-ordered for determinism.</summary>
    public IReadOnlyDictionary<string, OpenApiSchema>? Schemas { get; init; }

    /// <summary>The security schemes keyed by scheme name; ordinal-ordered for determinism.</summary>
    public IReadOnlyDictionary<string, OpenApiSecurityScheme>? SecuritySchemes { get; init; }
}

/// <summary>
/// A single path entry. Vista emits only <c>get</c> (metadata) and <c>post</c> (all other facets); no other
/// HTTP methods and no path-level parameters are used by the action-style surface (Decision Log D110).
/// </summary>
public sealed record OpenApiPathItem
{
    /// <summary>The <c>GET</c> operation on this path (the metadata facet), or <see langword="null"/>.</summary>
    public OpenApiOperation? Get { get; init; }

    /// <summary>The <c>POST</c> operation on this path (list/detail/export/write facets), or <see langword="null"/>.</summary>
    public OpenApiOperation? Post { get; init; }
}

/// <summary>A single operation (one facet of one view) under a path/method.</summary>
public sealed record OpenApiOperation
{
    /// <summary>The unique operation id (<c>{viewName}_{facet}</c>).</summary>
    public string? OperationId { get; init; }

    /// <summary>An optional short summary.</summary>
    public string? Summary { get; init; }

    /// <summary>An optional longer description.</summary>
    public string? Description { get; init; }

    /// <summary>Optional grouping tags (for example the view name).</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Header/query parameters. The action-style surface uses no path parameters; headers such as
    /// <c>If-None-Match</c> appear here for the cached metadata operation.
    /// </summary>
    public IReadOnlyList<OpenApiParameter>? Parameters { get; init; }

    /// <summary>The request body descriptor, or <see langword="null"/> (for example the GET metadata operation).</summary>
    public OpenApiRequestBody? RequestBody { get; init; }

    /// <summary>The responses keyed by status code string (for example <c>"200"</c>); ordinal-ordered.</summary>
    public IReadOnlyDictionary<string, OpenApiResponse>? Responses { get; init; }

    /// <summary>
    /// The per-operation security requirement. <see langword="null"/> when the app is anonymous; otherwise
    /// the same one-door requirement attached to every facet of the view.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>? Security { get; init; }
}

/// <summary>A header or query parameter descriptor (the action surface declares no path parameters).</summary>
public sealed record OpenApiParameter
{
    /// <summary>The parameter name (for example <c>If-None-Match</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The parameter location: <c>header</c>, <c>query</c>, or <c>path</c>.</summary>
    public required string In { get; init; }

    /// <summary>An optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the parameter is required. Omitted when <see langword="null"/>.</summary>
    public bool? Required { get; init; }

    /// <summary>The parameter's schema.</summary>
    public OpenApiSchema? Schema { get; init; }
}

/// <summary>A request body descriptor with its per-media-type content.</summary>
public sealed record OpenApiRequestBody
{
    /// <summary>An optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the body is required. Omitted when <see langword="null"/>.</summary>
    public bool? Required { get; init; }

    /// <summary>Content keyed by media type (for example <c>application/json</c>); ordinal-ordered.</summary>
    public IReadOnlyDictionary<string, OpenApiMediaType>? Content { get; init; }
}

/// <summary>A single response (success or error) with its headers and per-media-type content.</summary>
public sealed record OpenApiResponse
{
    /// <summary>The response description (required by the OpenAPI specification).</summary>
    public required string Description { get; init; }

    /// <summary>Response headers keyed by header name (for example <c>ETag</c>); ordinal-ordered.</summary>
    public IReadOnlyDictionary<string, OpenApiHeader>? Headers { get; init; }

    /// <summary>Content keyed by media type (<c>application/json</c> or <c>application/problem+json</c>); ordinal-ordered.</summary>
    public IReadOnlyDictionary<string, OpenApiMediaType>? Content { get; init; }
}

/// <summary>The content for one media type: a single schema (referenced or inline).</summary>
public sealed record OpenApiMediaType
{
    /// <summary>The schema describing this media type's payload.</summary>
    public OpenApiSchema? Schema { get; init; }
}

/// <summary>A response header descriptor.</summary>
public sealed record OpenApiHeader
{
    /// <summary>An optional description.</summary>
    public string? Description { get; init; }

    /// <summary>The header's schema.</summary>
    public OpenApiSchema? Schema { get; init; }
}

/// <summary>
/// A security scheme entry under <c>components.securitySchemes</c>. The Vista default is an HTTP
/// <c>bearer</c> scheme; a host may configure a different one (Requirement 7.1/7.2).
/// </summary>
public sealed record OpenApiSecurityScheme
{
    /// <summary>The scheme type (for example <c>http</c>, <c>apiKey</c>).</summary>
    public required string Type { get; init; }

    /// <summary>The HTTP authorization scheme (for example <c>bearer</c>) for <c>http</c>-type schemes.</summary>
    public string? Scheme { get; init; }

    /// <summary>An optional bearer format hint (for example <c>JWT</c>).</summary>
    public string? BearerFormat { get; init; }

    /// <summary>An optional description.</summary>
    public string? Description { get; init; }

    /// <summary>The key/header name for an <c>apiKey</c>-type scheme.</summary>
    public string? Name { get; init; }

    /// <summary>The location (<c>header</c>/<c>query</c>/<c>cookie</c>) for an <c>apiKey</c>-type scheme.</summary>
    public string? In { get; init; }
}

/// <summary>
/// A JSON Schema fragment (OpenAPI 3.0 flavor). Only the members Vista emits are modeled. A schema is
/// either a reference (only <see cref="Ref"/> set) or an inline shape; unset members are omitted so an
/// all-null instance serializes to the permissive empty schema <c>{}</c> (Requirement 4.6).
/// </summary>
public sealed record OpenApiSchema
{
    /// <summary>The JSON type (<c>object</c>/<c>array</c>/<c>string</c>/<c>integer</c>/<c>number</c>/<c>boolean</c>).</summary>
    public string? Type { get; init; }

    /// <summary>The type format (for example <c>int64</c>, <c>uuid</c>, <c>date-time</c>, <c>byte</c>).</summary>
    public string? Format { get; init; }

    /// <summary>Whether the value may be <c>null</c> (OpenAPI 3.0 <c>nullable</c>). Omitted when <see langword="null"/>.</summary>
    public bool? Nullable { get; init; }

    /// <summary>A reference to a component schema (<c>#/components/schemas/{name}</c>), serialized as <c>$ref</c>.</summary>
    [JsonPropertyName("$ref")]
    public string? Ref { get; init; }

    /// <summary>The allowed string values for an enum member (matching <c>JsonStringEnumConverter</c>).</summary>
    public IReadOnlyList<string>? Enum { get; init; }

    /// <summary>The element schema for an <c>array</c> type.</summary>
    public OpenApiSchema? Items { get; init; }

    /// <summary>The property schemas for an <c>object</c> type keyed by property name; ordinal-ordered.</summary>
    public IReadOnlyDictionary<string, OpenApiSchema>? Properties { get; init; }

    /// <summary>The alternative schemas for a polymorphic union (used by <c>FilterNode</c>).</summary>
    public IReadOnlyList<OpenApiSchema>? OneOf { get; init; }

    /// <summary>The discriminator for a polymorphic union.</summary>
    public OpenApiDiscriminator? Discriminator { get; init; }

    /// <summary>The required property names for an <c>object</c> type.</summary>
    public IReadOnlyList<string>? Required { get; init; }

    /// <summary>Whether extra properties are permitted; omitted when <see langword="null"/>.</summary>
    public bool? AdditionalProperties { get; init; }

    /// <summary>An optional schema description.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// A polymorphic discriminator for a <c>oneOf</c> union, expressing how the wire distinguishes node kinds
/// (used by the <c>FilterNode</c> schema, Requirement 5.3).
/// </summary>
public sealed record OpenApiDiscriminator
{
    /// <summary>The property whose value selects the concrete schema.</summary>
    public required string PropertyName { get; init; }

    /// <summary>An optional value-to-schema-reference mapping; ordinal-ordered.</summary>
    public IReadOnlyDictionary<string, string>? Mapping { get; init; }
}
