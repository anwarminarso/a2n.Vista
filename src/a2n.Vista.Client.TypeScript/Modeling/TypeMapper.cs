using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// Pure mapping from an OpenAPI <see cref="OpenApiSchema"/> to a TypeScript <see cref="TsType"/>, per the
/// design "Scalar type mapping table" and its modifier rules (Requirements 3.1–3.7, 2.4).
/// </summary>
/// <remarks>
/// <para>
/// The mapper is a pure function of its inputs. Its only side effect is recording non-fatal notices through
/// the supplied <see cref="NoticeCollector"/> when a member degrades to <c>unknown</c> (Requirements 3.6,
/// 3.7); it never throws for an unmappable member and never omits one.
/// </para>
/// <para>
/// Base mapping:
/// <list type="bullet">
///   <item><c>integer</c> (any format) → <c>number</c></item>
///   <item><c>number</c> (any format) → <c>number</c></item>
///   <item><c>boolean</c> → <c>boolean</c></item>
///   <item><c>string</c> with no format / <c>uuid</c> / <c>date-time</c> / <c>byte</c> → <c>string</c></item>
///   <item>permissive <c>{}</c> / object schema → <c>unknown</c> + notice (Requirement 3.6)</item>
///   <item>any other <c>type</c>/<c>format</c> → <c>unknown</c> + notice (Requirement 3.7)</item>
/// </list>
/// Modifiers applied after the base map: a <c>string</c> enum becomes a string-literal union in document
/// order (Requirement 3.2); <c>nullable</c> adds <c>null</c> (Requirement 3.3); an <c>array</c> becomes
/// <c>T[]</c> over the mapped item type; a <c>$ref</c> becomes a reference to the target type by name.
/// </para>
/// <para>
/// Optionality (the <c>?</c> modifier, Requirement 3.4) is <em>not</em> a type concern, so it is not
/// expressed by <see cref="Map"/>. Callers that build an object member use <see cref="MapProperty"/>, which
/// carries the required-ness flag onto the resulting <see cref="TsProperty"/>.
/// </para>
/// <para>
/// Scope note: this mapper handles scalars, string enums, arrays, permissive objects, and named
/// references. A structured inline object (a <c>type: object</c> with its own properties) is outside the
/// scalar mapper's scope; it degrades to <c>unknown</c> with a permissive-object notice rather than being
/// expanded here. The model builder is responsible for expanding named DTO components into declarations.
/// </para>
/// </remarks>
public sealed class TypeMapper
{
    /// <summary>
    /// Maps a schema to its TypeScript type expression, recording a non-fatal notice when the member
    /// degrades to <c>unknown</c>. Optionality is handled separately by <see cref="MapProperty"/>.
    /// </summary>
    /// <param name="schema">The schema to map.</param>
    /// <param name="viewContext">The owning view name, used to identify a degraded member in a notice.</param>
    /// <param name="propertyContext">The property name, used to identify a degraded member in a notice.</param>
    /// <param name="notices">The collector that receives any non-fatal degradation notice.</param>
    /// <returns>The mapped <see cref="TsType"/>; never <c>null</c>.</returns>
    public TsType Map(OpenApiSchema schema, string viewContext, string propertyContext, NoticeCollector notices)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(notices);

        // 1. A $ref is a reference to a declared type by name. Per OpenAPI 3.0 semantics, siblings of a
        //    $ref (including `nullable`) are ignored.
        if (!string.IsNullOrEmpty(schema.Ref))
        {
            return TsType.Named(ExtractRefName(schema.Ref));
        }

        // 2. Array → T[] over the mapped element type.
        if (IsType(schema, "array"))
        {
            TsType element;
            if (schema.Items is null)
            {
                // A malformed array (no item schema) degrades its element to `unknown`, never fatal.
                notices.AddPermissiveObjectMember(viewContext, propertyContext);
                element = TsType.Unknown;
            }
            else
            {
                element = Map(schema.Items, viewContext, propertyContext, notices);
            }

            return ApplyNullable(TsType.ArrayOf(element), schema.Nullable);
        }

        // 3. Permissive / unconstrained object → `unknown` + notice (Requirement 3.6).
        if (IsPermissive(schema))
        {
            notices.AddPermissiveObjectMember(viewContext, propertyContext);
            return ApplyNullable(TsType.Unknown, schema.Nullable);
        }

        // 4. String enum → literal union in document order (Requirement 3.2).
        if (IsStringEnum(schema))
        {
            return ApplyNullable(TsType.LiteralUnion(schema.Enum!), schema.Nullable);
        }

        // 5. Recognized scalar → its TypeScript primitive (Requirement 3.5).
        if (TryMapScalar(schema.Type, schema.Format, out var scalar))
        {
            return ApplyNullable(scalar, schema.Nullable);
        }

        // 6. Anything else is an unrecognized scalar type/format → `unknown` + notice (Requirement 3.7).
        notices.AddUnrecognizedScalar(viewContext, propertyContext, schema.Type, schema.Format);
        return ApplyNullable(TsType.Unknown, schema.Nullable);
    }

    /// <summary>
    /// Maps a schema to an object-member declaration, applying the property-level optionality modifier: a
    /// property absent from its owner's <c>required</c> list is emitted optional (the <c>?</c> modifier,
    /// Requirement 3.4). Property names are used verbatim and case-sensitively (Requirement 3.1).
    /// </summary>
    /// <param name="propertyName">The verbatim, case-sensitive property name.</param>
    /// <param name="schema">The property's schema.</param>
    /// <param name="required"><c>true</c> when the property is required; <c>false</c> makes it optional.</param>
    /// <param name="viewContext">The owning view name, used to identify a degraded member in a notice.</param>
    /// <param name="notices">The collector that receives any non-fatal degradation notice.</param>
    /// <returns>The mapped <see cref="TsProperty"/>.</returns>
    public TsProperty MapProperty(
        string propertyName,
        OpenApiSchema schema,
        bool required,
        string viewContext,
        NoticeCollector notices)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        var type = Map(schema, viewContext, propertyName, notices);
        return new TsProperty(propertyName, type, Optional: !required);
    }

    // Resolves a local component $ref (e.g. "#/components/schemas/CustomerRow") to its bare name.
    // A ref without a '/' is returned unchanged.
    private static string ExtractRefName(string reference)
    {
        var lastSlash = reference.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash < reference.Length - 1
            ? reference[(lastSlash + 1)..]
            : reference;
    }

    // Adds `null` to a type when the schema is nullable (Requirement 3.3). NullableOf is idempotent and
    // skips types that already admit null, so the emitted union stays canonical.
    private static TsType ApplyNullable(TsType type, bool nullable) =>
        nullable ? TsType.NullableOf(type) : type;

    private static bool IsType(OpenApiSchema schema, string type) =>
        string.Equals(schema.Type, type, StringComparison.Ordinal);

    // A permissive/unconstrained member: an open `{}` schema, an object schema (structured expansion is out
    // of the scalar mapper's scope), or a schema carrying neither a type nor a $ref.
    private static bool IsPermissive(OpenApiSchema schema) =>
        schema.AdditionalPropertiesOpen
        || IsType(schema, "object")
        || (schema.Ref is null && schema.Type is null);

    private static bool IsStringEnum(OpenApiSchema schema) =>
        IsType(schema, "string") && schema.Enum is { Count: > 0 };

    // Maps a recognized scalar type/format to its TypeScript primitive. Returns false for any combination
    // the mapper does not recognize, so the caller can degrade to `unknown` with a notice (Requirement 3.7).
    private static bool TryMapScalar(string? type, string? format, out TsType mapped)
    {
        switch (type)
        {
            case "integer":
            case "number":
                mapped = TsType.Number;
                return true;

            case "boolean":
                mapped = TsType.Boolean;
                return true;

            case "string" when IsRecognizedStringFormat(format):
                mapped = TsType.String;
                return true;

            default:
                mapped = TsType.Unknown;
                return false;
        }
    }

    // The string formats the mapper recognizes (Requirement 3.5): none, uuid, date-time, byte.
    private static bool IsRecognizedStringFormat(string? format) =>
        string.IsNullOrEmpty(format)
        || format is "uuid" or "date-time" or "byte";
}
