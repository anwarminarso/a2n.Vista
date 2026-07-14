using a2n.Vista.Client.TypeScript.Model;

namespace a2n.Vista.Client.TypeScript.Resolve;

/// <summary>
/// A parsed <see cref="OpenApiDocument"/> whose every local <c>$ref</c> has been confirmed to resolve to a
/// component under <c>#/components/schemas</c> or <c>#/components/securitySchemes</c> (design §A.4). It is the
/// output of the resolve stage and the input the model builder consumes.
/// </summary>
/// <remarks>
/// <para>
/// The document is <em>not</em> inlined. Named schemas keep their names and are emitted once, referenced by
/// name (Requirement 2.5); recursive references (for example <c>FilterNode</c> → <c>FilterNode</c>) remain
/// <b>by-name edges</b>, so a cycle is a finite edge in a name-keyed graph and never expands infinitely
/// (design §A.4). Downstream code follows an edge by calling <see cref="ResolveSchemaRef"/> /
/// <see cref="ResolveSecuritySchemeRef"/> with the raw <c>$ref</c> string, or looks a name up directly in
/// <see cref="Schemas"/> / <see cref="SecuritySchemes"/>.
/// </para>
/// <para>
/// Because the resolve stage has already verified every reference, the lookups here are guaranteed to
/// succeed for any <c>$ref</c> that appears anywhere in <see cref="Document"/>; a lookup on a name the
/// document never references may still legitimately miss.
/// </para>
/// </remarks>
/// <param name="Document">The underlying parsed document, unchanged.</param>
/// <param name="Schemas">The name-keyed schema graph (<c>#/components/schemas</c>).</param>
/// <param name="SecuritySchemes">The name-keyed security-scheme graph (<c>#/components/securitySchemes</c>).</param>
public sealed record ResolvedDocument(
    OpenApiDocument Document,
    IReadOnlyDictionary<string, OpenApiSchema> Schemas,
    IReadOnlyDictionary<string, OpenApiSecurityScheme> SecuritySchemes)
{
    /// <summary>The local <c>$ref</c> prefix for a component schema.</summary>
    public const string SchemaRefPrefix = "#/components/schemas/";

    /// <summary>The local <c>$ref</c> prefix for a component security scheme.</summary>
    public const string SecuritySchemeRefPrefix = "#/components/securitySchemes/";

    /// <summary>
    /// Follows a schema <c>$ref</c> by name. Returns the target schema, or <c>null</c> when
    /// <paramref name="refValue"/> is not a <c>#/components/schemas/{name}</c> reference or names no schema.
    /// </summary>
    /// <param name="refValue">A raw <c>$ref</c> string (for example <c>"#/components/schemas/FilterNode"</c>).</param>
    public OpenApiSchema? ResolveSchemaRef(string refValue) =>
        TryGetComponentName(refValue, SchemaRefPrefix, out var name) && Schemas.TryGetValue(name, out var schema)
            ? schema
            : null;

    /// <summary>
    /// Follows a security-scheme <c>$ref</c> by name. Returns the target scheme, or <c>null</c> when
    /// <paramref name="refValue"/> is not a <c>#/components/securitySchemes/{name}</c> reference or names no
    /// scheme.
    /// </summary>
    /// <param name="refValue">A raw <c>$ref</c> string.</param>
    public OpenApiSecurityScheme? ResolveSecuritySchemeRef(string refValue) =>
        TryGetComponentName(refValue, SecuritySchemeRefPrefix, out var name)
        && SecuritySchemes.TryGetValue(name, out var scheme)
            ? scheme
            : null;

    /// <summary>
    /// Extracts the component name from a local <c>$ref</c> of the form <paramref name="prefix"/> +
    /// <c>{name}</c>. Returns <c>true</c> and sets <paramref name="name"/> on a match; otherwise
    /// <c>false</c>.
    /// </summary>
    public static bool TryGetComponentName(string refValue, string prefix, out string name)
    {
        ArgumentNullException.ThrowIfNull(refValue);
        ArgumentNullException.ThrowIfNull(prefix);

        if (refValue.StartsWith(prefix, StringComparison.Ordinal) && refValue.Length > prefix.Length)
        {
            name = refValue[prefix.Length..];
            return true;
        }

        name = string.Empty;
        return false;
    }
}
