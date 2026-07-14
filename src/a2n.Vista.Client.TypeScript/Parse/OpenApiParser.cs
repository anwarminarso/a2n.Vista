using System.Text;
using System.Text.Json;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Parse;

/// <summary>
/// The parse stage of the generator pipeline (design §A.3): turns the raw, acquired document bytes into
/// the internal <see cref="OpenApiDocument"/> model using <see cref="System.Text.Json"/>.
/// </summary>
/// <remarks>
/// <para>
/// The model is minimal and hand-shaped, so the parser reads the JSON directly through
/// <see cref="JsonDocument"/>/<see cref="JsonElement"/> rather than deserializing into an intermediate
/// object graph. YAML is out of scope for v1 (M18 emits JSON).
/// </para>
/// <para>
/// The parser never throws for expected parse failures. It catches <see cref="JsonException"/> and returns
/// a typed <see cref="ParseError"/>:
/// </para>
/// <list type="bullet">
///   <item>
///     A declared <c>openapi</c> version outside the supported <c>3.0.x</c>–<c>3.1.x</c> range yields
///     <see cref="ParseError.UnsupportedVersion"/> (Requirement 1.5).
///   </item>
///   <item>
///     Invalid JSON or a structurally malformed document (for example, a missing <c>openapi</c> field)
///     yields <see cref="ParseError.Malformed"/> identifying the location and nature of the failure
///     (Requirement 1.6).
///   </item>
/// </list>
/// <para>
/// The parser records each <c>$ref</c> verbatim on <see cref="OpenApiSchema.Ref"/>. It does <em>not</em>
/// resolve references — that is the resolve stage's responsibility (task 5.1). Per OpenAPI 3.0 semantics,
/// siblings of a <c>$ref</c> are ignored.
/// </para>
/// </remarks>
public static class OpenApiParser
{
    /// <summary>Parses the raw document bytes into the internal <see cref="OpenApiDocument"/> model.</summary>
    /// <param name="bytes">The acquired UTF-8 document bytes.</param>
    /// <returns>
    /// A successful result carrying the parsed <see cref="OpenApiDocument"/>, or a typed
    /// <see cref="ParseError"/> describing the failure.
    /// </returns>
    public static Result<OpenApiDocument, ParseError> Parse(ReadOnlyMemory<byte> bytes)
    {
        JsonDocument json;
        try
        {
            // AllowTrailingCommas/comment skipping is intentionally left at the strict defaults so that a
            // malformed document is reported rather than silently tolerated (Requirement 1.6).
            json = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            return Result<OpenApiDocument, ParseError>.Err(
                new ParseError.Malformed(DescribeLocation(ex), ex.Message));
        }

        using (json)
        {
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<OpenApiDocument, ParseError>.Err(
                    new ParseError.Malformed("$", "The document root is not a JSON object."));
            }

            // Version gating happens before any structural mapping so an unsupported document aborts
            // cheaply with the declared version named (Requirement 1.5).
            if (!root.TryGetProperty("openapi", out JsonElement versionElement) ||
                versionElement.ValueKind != JsonValueKind.String)
            {
                return Result<OpenApiDocument, ParseError>.Err(
                    new ParseError.Malformed("openapi", "Missing required 'openapi' version string."));
            }

            string declaredVersion = versionElement.GetString()!;
            if (!IsSupportedVersion(declaredVersion))
            {
                return Result<OpenApiDocument, ParseError>.Err(
                    new ParseError.UnsupportedVersion(declaredVersion));
            }

            try
            {
                OpenApiDocument document = ReadDocument(root, declaredVersion);
                return Result<OpenApiDocument, ParseError>.Ok(document);
            }
            catch (JsonException ex)
            {
                // Defensive: structural type mismatches surfaced while walking the tree are treated as
                // malformed rather than allowed to escape as an exception (Requirement 1.6).
                return Result<OpenApiDocument, ParseError>.Err(
                    new ParseError.Malformed(DescribeLocation(ex), ex.Message));
            }
        }
    }

    /// <summary>Parses the raw document bytes into the internal <see cref="OpenApiDocument"/> model.</summary>
    /// <param name="bytes">The acquired UTF-8 document bytes.</param>
    public static Result<OpenApiDocument, ParseError> Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Parse(bytes.AsMemory());
    }

    /// <summary>Parses the document text into the internal <see cref="OpenApiDocument"/> model.</summary>
    /// <param name="json">The document text.</param>
    public static Result<OpenApiDocument, ParseError> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Returns <c>true</c> when the declared <c>openapi</c> version is within the supported
    /// <c>3.0.x</c>–<c>3.1.x</c> range: major <c>3</c> and minor <c>0</c> or <c>1</c>. Any other value —
    /// including <c>2.x</c>, <c>3.2</c>+, or an unparseable string — is unsupported (Requirement 1.5).
    /// </summary>
    private static bool IsSupportedVersion(string declared)
    {
        // The version is a dotted string such as "3.0.4" or "3.1". Only the major and minor components
        // gate support; the patch component (if any) is ignored.
        string[] parts = declared.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
        {
            return false;
        }

        return major == 3 && minor is 0 or 1;
    }

    private static OpenApiDocument ReadDocument(JsonElement root, string version)
    {
        OpenApiInfo info = ReadInfo(root);
        IReadOnlyDictionary<string, OpenApiPathItem> paths = ReadPaths(root);
        OpenApiComponents components = ReadComponents(root);
        // The root-level "security" array has the same shape as a per-operation one, so it is read with the
        // shared helper. It applies to every operation unless a per-operation "security" overrides it.
        IReadOnlyList<OpenApiSecurityRequirement> security = ReadSecurity(root);
        return new OpenApiDocument(version, info, paths, components, security);
    }

    private static OpenApiInfo ReadInfo(JsonElement root)
    {
        // Info is read leniently: an absent info object (or absent sub-fields) yields empty strings rather
        // than a fatal parse error, since the generator only reports these values, never gates on them.
        if (!root.TryGetProperty("info", out JsonElement info) || info.ValueKind != JsonValueKind.Object)
        {
            return new OpenApiInfo(string.Empty, string.Empty);
        }

        string title = ReadStringOrDefault(info, "title");
        string version = ReadStringOrDefault(info, "version");
        return new OpenApiInfo(title, version);
    }

    private static IReadOnlyDictionary<string, OpenApiPathItem> ReadPaths(JsonElement root)
    {
        var paths = new Dictionary<string, OpenApiPathItem>(StringComparer.Ordinal);
        if (!root.TryGetProperty("paths", out JsonElement pathsElement) ||
            pathsElement.ValueKind != JsonValueKind.Object)
        {
            return paths;
        }

        foreach (JsonProperty path in pathsElement.EnumerateObject())
        {
            paths[path.Name] = ReadPathItem(path.Value);
        }

        return paths;
    }

    private static OpenApiPathItem ReadPathItem(JsonElement pathItem)
    {
        var operations = new Dictionary<string, OpenApiOperation>(StringComparer.Ordinal);
        if (pathItem.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty member in pathItem.EnumerateObject())
            {
                // Only the HTTP method verbs are operations; path-level members such as "parameters" or
                // "summary" are not consumed by the generator.
                if (IsHttpMethod(member.Name) && member.Value.ValueKind == JsonValueKind.Object)
                {
                    operations[member.Name] = ReadOperation(member.Value);
                }
            }
        }

        return new OpenApiPathItem(operations);
    }

    private static OpenApiOperation ReadOperation(JsonElement operation)
    {
        string operationId = ReadStringOrDefault(operation, "operationId");
        OpenApiRequestBody? requestBody = ReadRequestBody(operation);
        IReadOnlyDictionary<string, OpenApiResponse> responses = ReadResponses(operation);
        IReadOnlyList<OpenApiSecurityRequirement> security = ReadSecurity(operation);
        return new OpenApiOperation(operationId, requestBody, responses, security);
    }

    private static OpenApiRequestBody? ReadRequestBody(JsonElement operation)
    {
        if (!operation.TryGetProperty("requestBody", out JsonElement requestBody) ||
            requestBody.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        bool required = requestBody.TryGetProperty("required", out JsonElement requiredElement) &&
                        requiredElement.ValueKind == JsonValueKind.True;
        OpenApiSchema? schema = ReadContentSchema(requestBody);
        return new OpenApiRequestBody(required, schema);
    }

    private static IReadOnlyDictionary<string, OpenApiResponse> ReadResponses(JsonElement operation)
    {
        var responses = new Dictionary<string, OpenApiResponse>(StringComparer.Ordinal);
        if (!operation.TryGetProperty("responses", out JsonElement responsesElement) ||
            responsesElement.ValueKind != JsonValueKind.Object)
        {
            return responses;
        }

        foreach (JsonProperty response in responsesElement.EnumerateObject())
        {
            OpenApiSchema? schema = ReadContentSchema(response.Value);
            responses[response.Name] = new OpenApiResponse(schema);
        }

        return responses;
    }

    /// <summary>
    /// Reads the content schema from a request body or response object, preferring <c>application/json</c>,
    /// then <c>application/problem+json</c>, then any first declared media type (so an export operation's
    /// <c>application/octet-stream</c> schema is still captured).
    /// </summary>
    private static OpenApiSchema? ReadContentSchema(JsonElement container)
    {
        if (!container.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryReadMediaTypeSchema(content, "application/json", out OpenApiSchema? jsonSchema))
        {
            return jsonSchema;
        }

        if (TryReadMediaTypeSchema(content, "application/problem+json", out OpenApiSchema? problemSchema))
        {
            return problemSchema;
        }

        // Fall back to the first media type that carries a schema (e.g. application/octet-stream).
        foreach (JsonProperty mediaType in content.EnumerateObject())
        {
            if (mediaType.Value.ValueKind == JsonValueKind.Object &&
                mediaType.Value.TryGetProperty("schema", out JsonElement schema) &&
                schema.ValueKind == JsonValueKind.Object)
            {
                return ReadSchema(schema);
            }
        }

        return null;
    }

    private static bool TryReadMediaTypeSchema(JsonElement content, string mediaType, out OpenApiSchema? schema)
    {
        schema = null;
        if (content.TryGetProperty(mediaType, out JsonElement mediaTypeElement) &&
            mediaTypeElement.ValueKind == JsonValueKind.Object &&
            mediaTypeElement.TryGetProperty("schema", out JsonElement schemaElement) &&
            schemaElement.ValueKind == JsonValueKind.Object)
        {
            schema = ReadSchema(schemaElement);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a <c>security</c> requirements array from any object that may declare one — a per-operation
    /// object or the root document object share the same shape (an array of requirement objects mapping a
    /// scheme name to its scope list). Returns an empty list when absent.
    /// </summary>
    private static IReadOnlyList<OpenApiSecurityRequirement> ReadSecurity(JsonElement owner)
    {
        if (!owner.TryGetProperty("security", out JsonElement security) ||
            security.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var requirements = new List<OpenApiSecurityRequirement>();
        foreach (JsonElement requirement in security.EnumerateArray())
        {
            if (requirement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Each requirement object maps a scheme name to its (possibly empty) scope list.
            foreach (JsonProperty scheme in requirement.EnumerateObject())
            {
                requirements.Add(new OpenApiSecurityRequirement(scheme.Name, ReadStringList(scheme.Value)));
            }
        }

        return requirements;
    }

    private static OpenApiComponents ReadComponents(JsonElement root)
    {
        var schemas = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
        var securitySchemes = new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal);

        if (root.TryGetProperty("components", out JsonElement components) &&
            components.ValueKind == JsonValueKind.Object)
        {
            if (components.TryGetProperty("schemas", out JsonElement schemasElement) &&
                schemasElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty schema in schemasElement.EnumerateObject())
                {
                    schemas[schema.Name] = ReadSchema(schema.Value);
                }
            }

            if (components.TryGetProperty("securitySchemes", out JsonElement schemesElement) &&
                schemesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty scheme in schemesElement.EnumerateObject())
                {
                    securitySchemes[scheme.Name] = ReadSecurityScheme(scheme.Value);
                }
            }
        }

        return new OpenApiComponents(schemas, securitySchemes);
    }

    private static OpenApiSecurityScheme ReadSecurityScheme(JsonElement scheme)
    {
        string type = ReadStringOrDefault(scheme, "type");
        string? schemeName = ReadStringOrNull(scheme, "scheme");
        string? bearerFormat = ReadStringOrNull(scheme, "bearerFormat");
        return new OpenApiSecurityScheme(type, schemeName, bearerFormat);
    }

    /// <summary>
    /// Reads a single JSON schema node into <see cref="OpenApiSchema"/>. Records the <c>$ref</c> verbatim
    /// without resolving it, and captures type/format/nullability, required members, object properties,
    /// array items, <c>oneOf</c> variants, string enums, and the permissive flag.
    /// </summary>
    private static OpenApiSchema ReadSchema(JsonElement schema)
    {
        string? refValue = ReadStringOrNull(schema, "$ref");
        string? type = ReadStringOrNull(schema, "type");
        string? format = ReadStringOrNull(schema, "format");
        bool nullable = schema.TryGetProperty("nullable", out JsonElement nullableElement) &&
                        nullableElement.ValueKind == JsonValueKind.True;

        IReadOnlyList<string> required = schema.TryGetProperty("required", out JsonElement requiredElement)
            ? ReadStringList(requiredElement)
            : [];

        IReadOnlyDictionary<string, OpenApiSchema>? properties = null;
        if (schema.TryGetProperty("properties", out JsonElement propertiesElement) &&
            propertiesElement.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
            foreach (JsonProperty property in propertiesElement.EnumerateObject())
            {
                map[property.Name] = ReadSchema(property.Value);
            }

            properties = map;
        }

        OpenApiSchema? items = null;
        if (schema.TryGetProperty("items", out JsonElement itemsElement) &&
            itemsElement.ValueKind == JsonValueKind.Object)
        {
            items = ReadSchema(itemsElement);
        }

        IReadOnlyList<OpenApiSchema>? oneOf = null;
        if (schema.TryGetProperty("oneOf", out JsonElement oneOfElement) &&
            oneOfElement.ValueKind == JsonValueKind.Array)
        {
            var variants = new List<OpenApiSchema>();
            foreach (JsonElement variant in oneOfElement.EnumerateArray())
            {
                if (variant.ValueKind == JsonValueKind.Object)
                {
                    variants.Add(ReadSchema(variant));
                }
            }

            oneOf = variants;
        }

        IReadOnlyList<string>? enumValues = null;
        if (schema.TryGetProperty("enum", out JsonElement enumElement) &&
            enumElement.ValueKind == JsonValueKind.Array)
        {
            var values = new List<string>();
            foreach (JsonElement value in enumElement.EnumerateArray())
            {
                // Only string enums are modeled; each value is captured in document order (Requirement 3.2).
                values.Add(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
            }

            enumValues = values;
        }

        bool additionalPropertiesOpen = ReadAdditionalPropertiesOpen(
            schema, refValue, type, properties, items, oneOf, enumValues);

        return new OpenApiSchema(
            refValue,
            type,
            format,
            nullable,
            required,
            properties,
            items,
            oneOf,
            enumValues,
            additionalPropertiesOpen);
    }

    /// <summary>
    /// Determines whether a schema is permissive (degrades to <c>unknown</c> in the type mapper): either an
    /// explicit <c>additionalProperties: true</c>, or a bare <c>{}</c> schema that constrains nothing
    /// (no <c>$ref</c>, <c>type</c>, <c>properties</c>, <c>items</c>, <c>oneOf</c>, or <c>enum</c>).
    /// </summary>
    private static bool ReadAdditionalPropertiesOpen(
        JsonElement schema,
        string? refValue,
        string? type,
        IReadOnlyDictionary<string, OpenApiSchema>? properties,
        OpenApiSchema? items,
        IReadOnlyList<OpenApiSchema>? oneOf,
        IReadOnlyList<string>? enumValues)
    {
        if (schema.TryGetProperty("additionalProperties", out JsonElement additional) &&
            additional.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        bool constrainsShape =
            refValue is not null ||
            type is not null ||
            properties is not null ||
            items is not null ||
            oneOf is not null ||
            enumValues is not null;

        return !constrainsShape;
    }

    private static bool IsHttpMethod(string name) => name switch
    {
        "get" or "put" or "post" or "delete" or "options" or "head" or "patch" or "trace" => true,
        _ => false,
    };

    private static IReadOnlyList<string> ReadStringList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                values.Add(item.GetString()!);
            }
        }

        return values;
    }

    private static string ReadStringOrDefault(JsonElement element, string propertyName) =>
        ReadStringOrNull(element, propertyName) ?? string.Empty;

    private static string? ReadStringOrNull(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string DescribeLocation(JsonException ex)
    {
        // System.Text.Json reports the JSON path/line/position on the exception; surface the path when
        // available so the malformed-document report points at the failure site (Requirement 1.6).
        if (!string.IsNullOrEmpty(ex.Path))
        {
            return ex.LineNumber is { } line
                ? $"{ex.Path} (line {line}, position {ex.BytePositionInLine ?? 0})"
                : ex.Path;
        }

        return ex.LineNumber is { } lineNo
            ? $"line {lineNo}, position {ex.BytePositionInLine ?? 0}"
            : "$";
    }
}
