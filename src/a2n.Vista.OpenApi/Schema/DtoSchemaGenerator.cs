using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using a2n.Vista.OpenApi.Model;

namespace a2n.Vista.OpenApi.Schema;

/// <summary>
/// The single reflection branch of the OpenAPI emitter (Decision Log D127; Requirement 13.3). Given a CLR
/// type and the serialization seam's <see cref="JsonSerializerOptions"/>, it produces an
/// <see cref="OpenApiSchema"/> whose property names, nullability, enum representation, and BCL scalar
/// <c>type</c>/<c>format</c> match the JSON the seam actually emits (schema/wire parity, Requirement 4).
/// </summary>
/// <remarks>
/// <para>
/// This type is <b>not</b> trim/AOT-clean: describing a DTO's serialized shape requires reflecting over the
/// CLR type under the seam options, so all reflection is confined here and the public entry point is marked
/// <see cref="RequiresUnreferencedCodeAttribute"/>. The reflection-free document surface (envelopes,
/// <c>FilterNode</c>, path/operation structure) never reaches this branch (D96 AOT asymmetry).
/// </para>
/// <para>
/// Robustness over completeness (Requirement 4.6): a member whose shape cannot be described — a bespoke or
/// unsupported type, or any reflection failure — yields the permissive empty schema (<c>{}</c>) and a
/// non-fatal <see cref="Notices"/> entry. The generator never omits a property and never throws.
/// </para>
/// <para>
/// Nested POCOs are emitted as their own component schemas (collected in <see cref="Components"/>) and
/// referenced by <c>$ref</c>. Self-referential and mutually recursive types are handled by registering a
/// component's name before building its body, so a cycle resolves to a <c>$ref</c> rather than recursing
/// forever.
/// </para>
/// </remarks>
public sealed class DtoSchemaGenerator
{
    private readonly JsonSerializerOptions _seamOptions;
    private readonly SortedDictionary<string, OpenApiSchema> _components =
        OpenApiCollections.CreateMap<OpenApiSchema>();
    private readonly List<string> _notices = new();

    // Components are identified by CLR type (plus the visibility policy applied to it), NOT by simple type
    // name: two row types named `OrderRow` in different namespaces are different schemas and must not
    // collapse onto one component. The reservation also acts as the recursion/cycle guard — a nested
    // occurrence of a type already being built resolves to the reserved name instead of recursing.
    private readonly Dictionary<ComponentKey, string> _componentNames = new();
    private readonly HashSet<string> _takenNames = new(StringComparer.Ordinal);

    // Maps BCL scalar CLR types to their conventional OpenAPI type/format (Requirement 4.4). The instances
    // are immutable records, so sharing a single instance per type is safe.
    private static readonly IReadOnlyDictionary<Type, OpenApiSchema> ScalarSchemas = BuildScalarSchemas();

    /// <summary>The fixed description attached to a maskable field's schema (no value is disclosed).</summary>
    private const string MaskableDescription =
        "Maskable field: the server may substitute this value per request, so the response value is not "
        + "guaranteed to be the stored one.";

    /// <summary>
    /// The component identity: the CLR type plus the field-visibility policy applied to it. The policy is
    /// part of the identity because the same row type described under two different policies is two
    /// different schemas.
    /// </summary>
    private readonly record struct ComponentKey(Type Type, string? PolicyKey);

    /// <summary>
    /// Creates a generator bound to the serialization seam's options. The seam's
    /// <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> is the authority for JSON property names.
    /// </summary>
    /// <param name="seamOptions">The serialization seam's options (read, never modified).</param>
    public DtoSchemaGenerator(JsonSerializerOptions seamOptions)
    {
        ArgumentNullException.ThrowIfNull(seamOptions);
        _seamOptions = seamOptions;
    }

    /// <summary>
    /// The nested POCO component schemas discovered while generating, keyed by component name; ordinal
    /// ordered for determinism. Populated as a side effect of <see cref="GenerateSchema"/>.
    /// </summary>
    public IReadOnlyDictionary<string, OpenApiSchema> Components => _components;

    /// <summary>
    /// The non-fatal notices recorded for members whose shape could not be described (Requirement 4.6).
    /// An empty list means every member was described precisely.
    /// </summary>
    public IReadOnlyList<string> Notices => _notices;

    /// <summary>
    /// Produces the schema for <paramref name="type"/>. A POCO yields a <c>$ref</c> to a component schema
    /// registered in <see cref="Components"/>; a scalar/enum/collection yields an inline schema. Any type
    /// that cannot be described yields the permissive empty schema plus a <see cref="Notices"/> entry.
    /// </summary>
    /// <param name="type">The CLR type whose serialized JSON shape to describe.</param>
    /// <param name="policy">
    /// An optional per-view field-visibility policy applied to <paramref name="type"/>'s own members
    /// (never to nested types): hidden fields are omitted from the schema and maskable fields are annotated.
    /// Pass <see langword="null"/> to describe the type exactly as it serializes.
    /// </param>
    [RequiresUnreferencedCode("Per-view DTO schema generation reflects over CLR row/write types under the seam options.")]
    public OpenApiSchema GenerateSchema(Type type, DtoSchemaPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        return BuildTypeSchema(type, policy);
    }

    /// <summary>
    /// The <c>$ref</c> string for a component schema name.
    /// </summary>
    public static string ComponentRef(string componentName) => "#/components/schemas/" + componentName;

    [RequiresUnreferencedCode("Reflects over the CLR type to describe its serialized JSON shape.")]
    private OpenApiSchema BuildTypeSchema(Type type, DtoSchemaPolicy? policy = null)
    {
        try
        {
            // Nullable<T> (a nullable value type) -> describe T and mark the schema nullable.
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                var inner = BuildTypeSchema(underlying, policy);
                // A $ref cannot carry a sibling `nullable` in OpenAPI 3.0; only decorate inline schemas.
                return inner.Ref is null ? inner with { Nullable = true } : inner;
            }

            // byte[] serializes as a base64 string (Requirement 4.4); must precede the collection check.
            if (type == typeof(byte[]))
            {
                return new OpenApiSchema { Type = "string", Format = "byte" };
            }

            // Enums serialize as their member-name string via JsonStringEnumConverter (Requirement 4.2).
            if (type.IsEnum)
            {
                return new OpenApiSchema { Type = "string", Enum = Enum.GetNames(type) };
            }

            // BCL scalars -> conventional type/format (Requirement 4.4).
            if (ScalarSchemas.TryGetValue(type, out var scalar))
            {
                return scalar;
            }

            // Dictionaries serialize as a JSON object keyed by the dictionary key; describe the value shape
            // via additionalProperties and keep the object permissive on the key side.
            if (TryGetDictionaryValueType(type, out var valueType))
            {
                _ = valueType; // value-schema detail is deferred; the object shape is what tooling needs.
                return new OpenApiSchema { Type = "object", AdditionalProperties = true };
            }

            // Collections (IEnumerable<T>, arrays) -> array + items, excluding string/byte[] handled above.
            if (type != typeof(string) && TryGetEnumerableElementType(type, out var elementType))
            {
                return new OpenApiSchema { Type = "array", Items = BuildTypeSchema(elementType) };
            }

            // Anything else that looks like a POCO -> its own component schema + $ref (Requirement 4.5).
            if (IsPocoLike(type))
            {
                var name = RegisterComponent(type, policy);
                return new OpenApiSchema { Ref = ComponentRef(name) };
            }

            // Unresolvable shape -> permissive {} + non-fatal notice; never omit, never throw.
            return Permissive(type);
        }
        catch (Exception ex)
        {
            return Permissive(type, ex);
        }
    }

    [RequiresUnreferencedCode("Reflects over the POCO's properties to describe its serialized JSON shape.")]
    private string RegisterComponent(Type type, DtoSchemaPolicy? policy)
    {
        var key = new ComponentKey(type, policy?.Key);

        // Already emitted, or currently being built (cycle guard): resolve to the reserved name only.
        if (_componentNames.TryGetValue(key, out var reserved))
        {
            return reserved;
        }

        var name = ReserveComponentName(type, policy);
        _componentNames[key] = name;
        _components[name] = BuildObjectComponent(type, policy);
        return name;
    }

    /// <summary>
    /// Reserves a unique, deterministic component name for <paramref name="type"/>. The simple type name is
    /// preferred; on collision with an already-reserved name the namespace's last segment, then the full
    /// namespace, then an ordinal suffix disambiguate it, so two same-named types in different namespaces
    /// (or the same type under two different visibility policies) never share one component.
    /// </summary>
    private string ReserveComponentName(Type type, DtoSchemaPolicy? policy)
    {
        var baseName = ComponentName(type);
        if (_takenNames.Add(baseName))
        {
            return baseName;
        }

        foreach (var qualifier in NameQualifiers(type, policy))
        {
            var candidate = qualifier + "_" + baseName;
            if (_takenNames.Add(candidate))
            {
                return candidate;
            }
        }

        for (var ordinal = 2; ; ordinal++)
        {
            var candidate = baseName + "_" + ordinal.ToString(CultureInfo.InvariantCulture);
            if (_takenNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// The ordered disambiguation qualifiers for a colliding component name: the namespace's last segment,
    /// the full namespace (dots replaced so the name stays <c>$ref</c>-safe), then the policy key (the view
    /// name) when the collision comes from one type described under two different field-visibility policies.
    /// </summary>
    private static IEnumerable<string> NameQualifiers(Type type, DtoSchemaPolicy? policy)
    {
        var ns = type.Namespace;
        if (!string.IsNullOrEmpty(ns))
        {
            var lastDot = ns.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < ns.Length - 1)
            {
                yield return Sanitize(ns[(lastDot + 1)..]);
            }

            yield return Sanitize(ns);
        }

        if (policy is not null)
        {
            yield return Sanitize(policy.Key);
        }
    }

    /// <summary>Replaces every character that is not <c>$ref</c>-safe with an underscore.</summary>
    private static string Sanitize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    [RequiresUnreferencedCode("Reflects over the POCO's properties to describe its serialized JSON shape.")]
    private OpenApiSchema BuildObjectComponent(Type type, DtoSchemaPolicy? policy)
    {
        var properties = OpenApiCollections.CreateMap<OpenApiSchema>();

        foreach (var property in GetSerializableProperties(type))
        {
            // A hidden field is deliberately withheld from the view's own metadata facet, so it is not
            // described here either — publishing its name and type would disclose exactly what the author
            // chose to withhold (D95 field flags).
            if (policy is not null && policy.IsHidden(property.Name))
            {
                continue;
            }

            var jsonName = ResolvePropertyName(property);
            var memberSchema = BuildTypeSchema(property.PropertyType);

            // A maskable field's value may be substituted per request, so the schema says so. Only an inline
            // schema is annotated: in OpenAPI 3.0 a $ref cannot carry sibling keywords.
            if (policy is not null && policy.IsMaskable(property.Name) && memberSchema.Ref is null)
            {
                memberSchema = memberSchema with { Description = MaskableDescription };
            }

            // Reference-type nullability is a member-level concern (NullabilityInfoContext); value-type
            // nullability was already applied by BuildTypeSchema for Nullable<T>.
            if (memberSchema.Ref is null
                && memberSchema.Nullable is not true
                && IsReferenceMemberNullable(property))
            {
                memberSchema = memberSchema with { Nullable = true };
            }

            properties[jsonName] = memberSchema;
        }

        return new OpenApiSchema
        {
            Type = "object",
            Properties = properties.Count == 0 ? null : properties,
        };
    }

    [RequiresUnreferencedCode("Enumerates the CLR type's public instance properties.")]
    private static IEnumerable<PropertyInfo> GetSerializableProperties(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.GetMethod is not { IsPublic: true })
            {
                continue;
            }

            // A [JsonIgnore] with the default Always condition is never on the wire, so it is not described.
            var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>();
            if (ignore is not null && ignore.Condition == JsonIgnoreCondition.Always)
            {
                continue;
            }

            yield return property;
        }
    }

    private string ResolvePropertyName(PropertyInfo property)
    {
        // An explicit [JsonPropertyName] wins on the wire; otherwise the seam's naming policy governs.
        var explicitName = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (explicitName is not null)
        {
            return explicitName.Name;
        }

        var policy = _seamOptions.PropertyNamingPolicy;
        return policy is null ? property.Name : policy.ConvertName(property.Name);
    }

    private static bool IsReferenceMemberNullable(PropertyInfo property)
    {
        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        var info = new NullabilityInfoContext().Create(property);
        return info.ReadState == NullabilityState.Nullable;
    }

    private OpenApiSchema Permissive(Type type, Exception? error = null)
    {
        _notices.Add(error is null
            ? $"Member type '{type}' could not be described; emitting a permissive schema."
            : $"Member type '{type}' could not be described ({error.GetType().Name}: {error.Message}); emitting a permissive schema.");

        // An all-null OpenApiSchema serializes to the permissive empty schema {}.
        return new OpenApiSchema();
    }

    private static bool IsPocoLike(Type type)
    {
        if (type == typeof(object) || type.IsPrimitive || type.IsPointer)
        {
            return false;
        }

        if (type.IsInterface || type.IsAbstract)
        {
            return false;
        }

        return type.IsClass || (type.IsValueType && !type.IsEnum);
    }

    [RequiresUnreferencedCode("Inspects the type's implemented interfaces.")]
    private static bool TryGetDictionaryValueType(Type type, out Type valueType)
    {
        foreach (var candidate in TypeAndInterfaces(type))
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            var definition = candidate.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                valueType = candidate.GetGenericArguments()[1];
                return true;
            }
        }

        valueType = typeof(object);
        return false;
    }

    [RequiresUnreferencedCode("Inspects the type's implemented interfaces.")]
    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        foreach (var candidate in TypeAndInterfaces(type))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = candidate.GetGenericArguments()[0];
                return true;
            }
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            // A non-generic IEnumerable: the element shape is unknown, so items stay permissive.
            elementType = typeof(object);
            return true;
        }

        elementType = typeof(object);
        return false;
    }

    [RequiresUnreferencedCode("Inspects the type's implemented interfaces.")]
    private static IEnumerable<Type> TypeAndInterfaces(Type type)
    {
        yield return type;
        foreach (var @interface in type.GetInterfaces())
        {
            yield return @interface;
        }
    }

    private static string ComponentName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        // Strip the arity marker (`1) and append the type-argument names, keeping the name $ref-safe.
        var baseName = type.Name;
        var tick = baseName.IndexOf('`');
        if (tick >= 0)
        {
            baseName = baseName[..tick];
        }

        var args = type.GetGenericArguments().Select(ComponentName);
        return baseName + "_" + string.Join("_", args);
    }

    private static IReadOnlyDictionary<Type, OpenApiSchema> BuildScalarSchemas()
    {
        return new Dictionary<Type, OpenApiSchema>
        {
            [typeof(sbyte)] = new() { Type = "integer" },
            [typeof(byte)] = new() { Type = "integer" },
            [typeof(short)] = new() { Type = "integer" },
            [typeof(ushort)] = new() { Type = "integer" },
            [typeof(int)] = new() { Type = "integer", Format = "int32" },
            [typeof(uint)] = new() { Type = "integer", Format = "int32" },
            [typeof(long)] = new() { Type = "integer", Format = "int64" },
            [typeof(ulong)] = new() { Type = "integer", Format = "int64" },
            [typeof(float)] = new() { Type = "number", Format = "float" },
            [typeof(double)] = new() { Type = "number", Format = "double" },
            [typeof(decimal)] = new() { Type = "number" },
            [typeof(bool)] = new() { Type = "boolean" },
            [typeof(char)] = new() { Type = "string" },
            [typeof(string)] = new() { Type = "string" },
            [typeof(Guid)] = new() { Type = "string", Format = "uuid" },
            [typeof(DateTime)] = new() { Type = "string", Format = "date-time" },
            [typeof(DateTimeOffset)] = new() { Type = "string", Format = "date-time" },
            [typeof(DateOnly)] = new() { Type = "string", Format = "date" },
            [typeof(TimeOnly)] = new() { Type = "string", Format = "time" },
            [typeof(TimeSpan)] = new() { Type = "string" },
            [typeof(Uri)] = new() { Type = "string", Format = "uri" },
        };
    }
}
