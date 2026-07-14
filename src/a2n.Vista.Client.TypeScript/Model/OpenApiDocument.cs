namespace a2n.Vista.Client.TypeScript.Model;

/// <summary>
/// A minimal, JSON-shaped model of the OpenAPI document the generator consumes. It is intentionally
/// <em>not</em> a general OpenAPI object model; it captures exactly the members M17 reads from the
/// M18-emitted document (acquire → parse → resolve → model → emit → write pipeline). No parsing logic
/// lives here — these are pure data records populated by the parse stage.
/// </summary>
/// <param name="OpenApiVersion">The declared <c>openapi</c> version string, e.g. <c>"3.0.4"</c>.</param>
/// <param name="Info">The document <c>info</c> object.</param>
/// <param name="Paths">The <c>paths</c> map, keyed by path (e.g. <c>"/api/views/customers/list"</c>).</param>
/// <param name="Components">The reusable <c>components</c> (schemas and security schemes).</param>
/// <param name="Security">
/// The document-level (root) <c>security</c> requirements that apply to every operation unless overridden by
/// a per-operation <c>security</c>; empty when the document declares no top-level default.
/// </param>
public sealed record OpenApiDocument(
    string OpenApiVersion,
    OpenApiInfo Info,
    IReadOnlyDictionary<string, OpenApiPathItem> Paths,
    OpenApiComponents Components,
    IReadOnlyList<OpenApiSecurityRequirement> Security);

/// <summary>
/// The document <c>info</c> object. Only the members the generator consumes are modeled.
/// </summary>
/// <param name="Title">The API title.</param>
/// <param name="Version">The API version string.</param>
public sealed record OpenApiInfo(
    string Title,
    string Version);

/// <summary>
/// The document <c>components</c> object: the name-keyed reusable schema and security-scheme catalog the
/// model builder binds to by name.
/// </summary>
/// <param name="Schemas">Named schemas under <c>#/components/schemas</c>.</param>
/// <param name="SecuritySchemes">Named security schemes under <c>#/components/securitySchemes</c>.</param>
public sealed record OpenApiComponents(
    IReadOnlyDictionary<string, OpenApiSchema> Schemas,
    IReadOnlyDictionary<string, OpenApiSecurityScheme> SecuritySchemes);

/// <summary>
/// A single <c>paths</c> entry: the HTTP operations available at one path, keyed by lower-case HTTP method
/// (e.g. <c>"post"</c>, <c>"get"</c>).
/// </summary>
/// <param name="Operations">The operations at this path, keyed by HTTP method.</param>
public sealed record OpenApiPathItem(
    IReadOnlyDictionary<string, OpenApiOperation> Operations);

/// <summary>
/// A single HTTP operation. Only the members the generator consumes are modeled.
/// </summary>
/// <param name="OperationId">
/// The operation id, of the form <c>{viewName}_{suffix}</c> (e.g. <c>Customers_list</c>).
/// </param>
/// <param name="RequestBody">The request body, or <c>null</c> when the operation takes none.</param>
/// <param name="Responses">
/// The responses keyed by status code string (e.g. <c>"200"</c>, <c>"400"</c>, <c>"404"</c>,
/// <c>"409"</c>, <c>"428"</c>).
/// </param>
/// <param name="Security">
/// The per-operation <c>security</c> requirements; empty for an anonymous operation.
/// </param>
public sealed record OpenApiOperation(
    string OperationId,
    OpenApiRequestBody? RequestBody,
    IReadOnlyDictionary<string, OpenApiResponse> Responses,
    IReadOnlyList<OpenApiSecurityRequirement> Security);

/// <summary>
/// A JSON-shaped schema node. Captures only the members the generator reads: type/format/nullability,
/// required-member lists, object properties, array items, <c>oneOf</c> variants, string enums, and a flag
/// for permissive (<c>{}</c> / <c>additionalProperties: true</c>) schemas. A <see cref="Ref"/> is a local
/// component reference (e.g. <c>"#/components/schemas/FilterNode"</c>); per OpenAPI 3.0 semantics, siblings
/// of a <c>$ref</c> are ignored.
/// </summary>
/// <param name="Ref">A local component reference, or <c>null</c> when this is an inline schema.</param>
/// <param name="Type">The OpenAPI <c>type</c> (e.g. <c>"string"</c>, <c>"integer"</c>), or <c>null</c>.</param>
/// <param name="Format">The OpenAPI <c>format</c> (e.g. <c>"int64"</c>, <c>"uuid"</c>), or <c>null</c>.</param>
/// <param name="Nullable">Whether the schema is <c>nullable</c>.</param>
/// <param name="Required">The names of required members (empty when none).</param>
/// <param name="Properties">Object properties keyed by verbatim, case-sensitive name; <c>null</c> when not an object.</param>
/// <param name="Items">The item schema for an array; <c>null</c> when not an array.</param>
/// <param name="OneOf">The <c>oneOf</c> variant schemas; <c>null</c> when not a union.</param>
/// <param name="Enum">The allowed string enum values in document order; <c>null</c> when not an enum.</param>
/// <param name="AdditionalPropertiesOpen">
/// <c>true</c> when the schema is permissive (<c>{}</c> or <c>additionalProperties: true</c>), which the
/// type mapper degrades to <c>unknown</c>.
/// </param>
public sealed record OpenApiSchema(
    string? Ref,
    string? Type,
    string? Format,
    bool Nullable,
    IReadOnlyList<string> Required,
    IReadOnlyDictionary<string, OpenApiSchema>? Properties,
    OpenApiSchema? Items,
    IReadOnlyList<OpenApiSchema>? OneOf,
    IReadOnlyList<string>? Enum,
    bool AdditionalPropertiesOpen);

/// <summary>
/// A reusable security scheme under <c>#/components/securitySchemes</c>. Only the members the generator
/// consumes to classify security posture are modeled.
/// </summary>
/// <param name="Type">The scheme type (e.g. <c>"http"</c>).</param>
/// <param name="Scheme">The scheme name for an HTTP scheme (e.g. <c>"bearer"</c>), or <c>null</c>.</param>
/// <param name="BearerFormat">The bearer format hint (e.g. <c>"JWT"</c>), or <c>null</c>.</param>
public sealed record OpenApiSecurityScheme(
    string Type,
    string? Scheme,
    string? BearerFormat);

/// <summary>
/// A single <c>security</c> requirement: a map from security-scheme name to its required scope list (empty
/// for HTTP bearer schemes). An operation's presence of any requirement marks it secured.
/// </summary>
/// <param name="SchemeName">The referenced security-scheme name.</param>
/// <param name="Scopes">The required scopes (empty for bearer schemes).</param>
public sealed record OpenApiSecurityRequirement(
    string SchemeName,
    IReadOnlyList<string> Scopes);

/// <summary>
/// An operation request body. Only the JSON media-type content schema the generator consumes is modeled.
/// </summary>
/// <param name="Required">Whether the request body is required.</param>
/// <param name="Schema">The <c>application/json</c> content schema, or <c>null</c> when absent.</param>
public sealed record OpenApiRequestBody(
    bool Required,
    OpenApiSchema? Schema);

/// <summary>
/// An operation response. Only the JSON media-type content schema the generator consumes is modeled; the
/// response's status-code key lives in the owning operation's <see cref="OpenApiOperation.Responses"/> map.
/// </summary>
/// <param name="Schema">The response content schema, or <c>null</c> for an empty-bodied response.</param>
public sealed record OpenApiResponse(
    OpenApiSchema? Schema);
