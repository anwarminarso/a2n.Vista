using System.Text.Json;
using System.Text.Json.Serialization;
using a2n.Vista.OpenApi.Model;

namespace a2n.Vista.OpenApi.Serialization;

/// <summary>
/// The source-generated <see cref="JsonSerializerContext"/> over the Vista OpenAPI object model
/// (Decision Log D127). Because it is real source, the built-in System.Text.Json source generator emits
/// the metadata at compile time, so the document serializes <b>AOT-clean</b> (no <c>IL2026</c>/<c>IL3050</c>)
/// and <b>byte-stable</b>.
/// </summary>
/// <remarks>
/// <para>
/// The generation options pin the wire shape the OpenAPI specification expects and keep the document clean:
/// </para>
/// <list type="bullet">
///   <item><description><c>PropertyNamingPolicy = CamelCase</c> — OpenAPI field names (<c>openapi</c>,
///   <c>requestBody</c>, <c>securitySchemes</c>, <c>oneOf</c>, ...); the <c>$ref</c> member is pinned with
///   an explicit <see cref="JsonPropertyNameAttribute"/> on <see cref="OpenApiSchema.Ref"/>.</description></item>
///   <item><description><c>DefaultIgnoreCondition = WhenWritingNull</c> — unset members are omitted, so an
///   all-null <see cref="OpenApiSchema"/> serializes to the permissive empty schema <c>{}</c>.</description></item>
/// </list>
/// <para>
/// Byte-stability additionally relies on every map being populated with an ordinal-ordered dictionary
/// (see <see cref="OpenApiCollections"/>); the source generator serializes the map in its runtime
/// enumeration order, which for those dictionaries is <see cref="StringComparer.Ordinal"/> key order.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenApiDocument))]
[JsonSerializable(typeof(IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, IReadOnlyList<string>>))]
internal sealed partial class VistaOpenApiJsonContext : JsonSerializerContext
{
}

/// <summary>
/// AOT-clean, byte-stable serialization of the Vista <see cref="OpenApiDocument"/> object model, driven by
/// <see cref="VistaOpenApiJsonContext"/>. The same document instance always produces identical bytes
/// (Requirement 9.1).
/// </summary>
public static class VistaOpenApiJson
{
    /// <summary>
    /// Serializes <paramref name="document"/> to its deterministic OpenAPI JSON representation.
    /// </summary>
    /// <param name="document">The document to serialize.</param>
    /// <param name="writeIndented">
    /// When <see langword="true"/>, emits human-readable indented JSON; when <see langword="false"/>
    /// (the default), emits compact JSON. Both forms are byte-stable for a given document.
    /// </param>
    public static string Serialize(OpenApiDocument document, bool writeIndented = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        var context = writeIndented ? Indented : VistaOpenApiJsonContext.Default;
        return JsonSerializer.Serialize(document, context.OpenApiDocument);
    }

    // The indented context replicates the [JsonSourceGenerationOptions] wire settings explicitly, because a
    // context constructed with a caller-supplied JsonSerializerOptions does not inherit the attribute's
    // naming policy / ignore condition (only the generated Default instance does).
    private static readonly VistaOpenApiJsonContext Indented = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    });
}
