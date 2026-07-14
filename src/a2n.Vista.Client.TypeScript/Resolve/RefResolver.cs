using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Resolve;

/// <summary>
/// The resolve stage (design §A.4). A pure function that walks a parsed <see cref="OpenApiDocument"/>,
/// verifies every local <c>$ref</c> resolves to a component under <c>#/components/schemas</c> or
/// <c>#/components/securitySchemes</c>, and yields a <see cref="ResolvedDocument"/> whose references are all
/// confirmed (Requirements 1.7, 1.8).
/// </summary>
/// <remarks>
/// <para>
/// Only the two local component forms are accepted: <c>#/components/schemas/{name}</c> and
/// <c>#/components/securitySchemes/{name}</c>. Any other <c>$ref</c> shape (external, or an unsupported
/// internal pointer) targets no known component and is reported as
/// <see cref="ResolveError.Dangling"/> carrying the verbatim <c>$ref</c> value (Requirement 1.8).
/// </para>
/// <para>
/// A <c>$ref</c> node's siblings are ignored, per OpenAPI 3.0 semantics: when a schema carries a
/// <see cref="OpenApiSchema.Ref"/>, only the reference is validated and its sibling members are not walked.
/// </para>
/// <para>
/// References are kept as <b>by-name edges</b>: the walker validates a reference by looking its target name
/// up in the components dictionary and never follows the edge into the target. Because inline schemas are
/// the only nodes recursed into — and each named component is walked exactly once from the top level — a
/// cyclic reference (for example <c>FilterNode</c> → <c>FilterNode</c>) is a finite edge and cannot cause
/// infinite expansion.
/// </para>
/// <para>This stage performs no I/O and never throws for an expected (dangling-reference) failure.</para>
/// </remarks>
public static class RefResolver
{
    /// <summary>
    /// Resolves every local <c>$ref</c> in <paramref name="document"/> to a name-keyed component graph.
    /// </summary>
    /// <param name="document">The parsed document to resolve.</param>
    /// <returns>
    /// <see cref="Result{T, E}.Ok"/> with a <see cref="ResolvedDocument"/> when every reference resolves;
    /// otherwise <see cref="Result{T, E}.Err"/> with <see cref="ResolveError.Dangling"/> for the first
    /// reference that resolves to no component.
    /// </returns>
    public static Result<ResolvedDocument, ResolveError> Resolve(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var components = document.Components;

        // Walk every ref-bearing position in the component schemas (properties, items, oneOf variants,
        // and the schema's own $ref).
        foreach (var schema in components.Schemas.Values)
        {
            if (ValidateSchema(schema, components) is { } schemaError)
            {
                return Result<ResolvedDocument, ResolveError>.Err(schemaError);
            }
        }

        // Walk every ref-bearing position in the path operations: request body schema and response schemas.
        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var operation in pathItem.Operations.Values)
            {
                if (operation.RequestBody?.Schema is { } requestSchema
                    && ValidateSchema(requestSchema, components) is { } requestError)
                {
                    return Result<ResolvedDocument, ResolveError>.Err(requestError);
                }

                foreach (var response in operation.Responses.Values)
                {
                    if (response.Schema is { } responseSchema
                        && ValidateSchema(responseSchema, components) is { } responseError)
                    {
                        return Result<ResolvedDocument, ResolveError>.Err(responseError);
                    }
                }
            }
        }

        var resolved = new ResolvedDocument(document, components.Schemas, components.SecuritySchemes);
        return Result<ResolvedDocument, ResolveError>.Ok(resolved);
    }

    /// <summary>
    /// Validates every reference reachable from <paramref name="schema"/>. Returns the first
    /// <see cref="ResolveError"/> found, or <c>null</c> when all references resolve.
    /// </summary>
    private static ResolveError? ValidateSchema(OpenApiSchema schema, OpenApiComponents components)
    {
        // A $ref node: validate the reference only. Its siblings are ignored (OpenAPI 3.0 semantics), so we
        // do not recurse into them. This is also what keeps by-name edges finite: we never follow the edge.
        if (schema.Ref is { } refValue)
        {
            return ValidateRef(refValue, components);
        }

        // An inline schema: recurse into object properties, array items, and oneOf variants.
        if (schema.Properties is { } properties)
        {
            foreach (var property in properties.Values)
            {
                if (ValidateSchema(property, components) is { } propertyError)
                {
                    return propertyError;
                }
            }
        }

        if (schema.Items is { } items && ValidateSchema(items, components) is { } itemsError)
        {
            return itemsError;
        }

        if (schema.OneOf is { } oneOf)
        {
            foreach (var variant in oneOf)
            {
                if (ValidateSchema(variant, components) is { } variantError)
                {
                    return variantError;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Verifies a single <c>$ref</c> targets an existing component. Accepts only the two local component
    /// forms; anything else — or a name absent from its dictionary — is a dangling reference.
    /// </summary>
    private static ResolveError? ValidateRef(string refValue, OpenApiComponents components)
    {
        if (ResolvedDocument.TryGetComponentName(refValue, ResolvedDocument.SchemaRefPrefix, out var schemaName))
        {
            return components.Schemas.ContainsKey(schemaName)
                ? null
                : new ResolveError.Dangling(refValue);
        }

        if (ResolvedDocument.TryGetComponentName(
                refValue,
                ResolvedDocument.SecuritySchemeRefPrefix,
                out var securitySchemeName))
        {
            return components.SecuritySchemes.ContainsKey(securitySchemeName)
                ? null
                : new ResolveError.Dangling(refValue);
        }

        // Not a supported local component reference (external ref, or an unsupported internal pointer):
        // it targets no known component, so it is reported as dangling with the verbatim value.
        return new ResolveError.Dangling(refValue);
    }
}
