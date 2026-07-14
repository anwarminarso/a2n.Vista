// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Text.Json.Nodes;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Resolve;

namespace a2n.Vista.Client.TypeScript.Parity;

/// <summary>
/// Builds the <b>representative value set</b> that the parity harnesses (tasks 15.2 round-trip and 15.3
/// schema-parity) consume. Given a resolved OpenAPI document — the authoritative oracle — and a
/// <c>Generated_Type</c> (by name or by schema), it derives a deterministic set of sample JSON instances
/// (<see cref="JsonNode"/>) that satisfy the coverage criteria of Requirement 11.1.
/// </summary>
/// <remarks>
/// <para>
/// For each <c>Generated_Type</c> the returned set includes, at minimum, at least one value covering:
/// </para>
/// <list type="bullet">
///   <item>each declared property present (the canonical base value);</item>
///   <item>each nullable property in both its <em>present-and-null</em> and <em>absent</em> forms;</item>
///   <item>each string-enum member at least once;</item>
///   <item>each collection-typed property in both its empty and non-empty forms.</item>
/// </list>
/// <para>
/// The builder is a pure function of its inputs and produces deterministic output: object members are
/// visited in a fixed, ordinal, case-sensitive order, so repeated runs over the same document yield the
/// same values in the same order. The generated JSON is intended to be serialized and handed to the
/// TypeScript (fast-check) round-trip and schema-parity harnesses.
/// </para>
/// <para>
/// The only cyclic edge in a Vista document is the recursive <c>FilterNode</c> union (a bare <c>oneOf</c>
/// with no discriminator). Recursion through that union is bounded by <see cref="MaxUnionDepth"/> (default
/// <c>3</c>): once the budget is exhausted the builder emits a terminal (non-recursive) leaf variant. A
/// secondary hard guard protects against pathological, non-union self-references in a malformed document so
/// expansion is always finite.
/// </para>
/// </remarks>
public sealed class RepresentativeValueSet
{
    /// <summary>The default bound on recursion through the recursive <c>FilterNode</c> union.</summary>
    public const int DefaultMaxUnionDepth = 3;

    // A hard cap on total recursion depth. It never bites for a well-formed Vista document (whose only
    // cycle is FilterNode, already bounded by the union budget); it exists only so a malformed document
    // with a non-union self-reference cannot expand without end.
    private const int HardRecursionGuard = 64;

    private readonly int _maxUnionDepth;

    /// <summary>Initializes the builder with the given <c>FilterNode</c> recursion bound.</summary>
    /// <param name="maxUnionDepth">
    /// The maximum number of times a recursive union (<c>FilterNode</c>) may be crossed while expanding a
    /// value. Must be non-negative; defaults to <see cref="DefaultMaxUnionDepth"/>.
    /// </param>
    public RepresentativeValueSet(int maxUnionDepth = DefaultMaxUnionDepth)
    {
        if (maxUnionDepth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxUnionDepth), maxUnionDepth, "The union recursion depth must be non-negative.");
        }

        _maxUnionDepth = maxUnionDepth;
    }

    /// <summary>The configured bound on recursion through the recursive <c>FilterNode</c> union.</summary>
    public int MaxUnionDepth => _maxUnionDepth;

    /// <summary>
    /// Builds the representative value set for a named <c>Generated_Type</c> (a
    /// <c>#/components/schemas/{typeName}</c> component).
    /// </summary>
    /// <param name="typeName">The component name, for example <c>"CustomerRow"</c>.</param>
    /// <param name="document">The resolved OpenAPI document (the oracle).</param>
    /// <returns>A deterministic, non-empty set of sample JSON values covering the Requirement 11.1 criteria.</returns>
    /// <exception cref="ArgumentException">No schema named <paramref name="typeName"/> exists in the document.</exception>
    public IReadOnlyList<JsonNode> Build(string typeName, ResolvedDocument document)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(document);

        if (!document.Schemas.TryGetValue(typeName, out var schema))
        {
            throw new ArgumentException(
                $"No schema named '{typeName}' exists in the resolved document.", nameof(typeName));
        }

        return Build(schema, document);
    }

    /// <summary>Builds the representative value set for the given schema.</summary>
    /// <param name="schema">The <c>Generated_Type</c> schema (may be a local <c>$ref</c>).</param>
    /// <param name="document">The resolved OpenAPI document (the oracle).</param>
    /// <returns>A deterministic, non-empty set of sample JSON values covering the Requirement 11.1 criteria.</returns>
    public IReadOnlyList<JsonNode> Build(OpenApiSchema schema, ResolvedDocument document)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(document);

        var results = new List<JsonNode>();
        BuildCoverage(schema, document, _maxUnionDepth, results);

        // A schema that constrains nothing (permissive/unknown scalar) still contributes one canonical
        // value so the set is never empty.
        if (results.Count == 0)
        {
            results.Add(Canonical(schema, document, _maxUnionDepth, HardRecursionGuard));
        }

        return results;
    }

    // ---- Coverage (the set of representative values for a type) ----

    private void BuildCoverage(OpenApiSchema schema, ResolvedDocument document, int depth, List<JsonNode> results)
    {
        var s = Deref(schema, document);

        if (s.OneOf is { Count: > 0 } variants)
        {
            // A union (FilterNode): cover each variant once. Each variant is an object, so covering it does
            // not re-enter the union — the recursive FilterNode edges inside a variant are expanded with
            // bounded canonical values, not with a further coverage pass.
            foreach (var variant in variants)
            {
                BuildCoverage(variant, document, depth, results);
            }

            return;
        }

        if (s.Enum is { Count: > 0 } enumValues)
        {
            // A top-level enum type: one value per member.
            foreach (var member in enumValues)
            {
                results.Add(JsonValue.Create(member));
            }

            return;
        }

        if (IsArray(s))
        {
            // A top-level collection type: the empty form and a non-empty form.
            results.Add(new JsonArray());
            var nonEmpty = new JsonArray();
            if (s.Items is { } itemSchema)
            {
                nonEmpty.Add(Canonical(itemSchema, document, depth, HardRecursionGuard));
            }

            results.Add(nonEmpty);
            return;
        }

        if (IsObject(s))
        {
            BuildObjectCoverage(s, document, depth, results);
            return;
        }

        // A scalar or permissive/unknown top-level type: a single canonical value.
        results.Add(Canonical(s, document, depth, HardRecursionGuard));
    }

    private void BuildObjectCoverage(
        OpenApiSchema schema, ResolvedDocument document, int depth, List<JsonNode> results)
    {
        // The canonical base: every declared property present, non-null; every collection non-empty; every
        // enum at its first member.
        var baseValue = (JsonObject)Canonical(schema, document, depth, HardRecursionGuard);
        results.Add(baseValue);

        if (schema.Properties is not { } properties)
        {
            return;
        }

        foreach (var name in OrderedNames(properties))
        {
            var propertySchema = properties[name];
            var propertyDeref = Deref(propertySchema, document);

            // Nullability is declared on the property's own schema node (a $ref ignores its siblings, so a
            // referenced schema is never itself nullable here).
            if (propertySchema.Nullable)
            {
                // Present-and-null form.
                var presentNull = (JsonObject)baseValue.DeepClone();
                presentNull[name] = null;
                results.Add(presentNull);

                // Absent form.
                var absent = (JsonObject)baseValue.DeepClone();
                absent.Remove(name);
                results.Add(absent);
            }

            // Each enum member at least once.
            if (propertyDeref.Enum is { Count: > 0 } enumValues)
            {
                foreach (var member in enumValues)
                {
                    var variant = (JsonObject)baseValue.DeepClone();
                    variant[name] = JsonValue.Create(member);
                    results.Add(variant);
                }
            }

            // Each collection in its empty form (the non-empty form is already carried by the base).
            if (IsArray(propertyDeref))
            {
                var empty = (JsonObject)baseValue.DeepClone();
                empty[name] = new JsonArray();
                results.Add(empty);
            }
        }
    }

    // ---- Canonical (a single, fully-populated representative value) ----

    private JsonNode Canonical(OpenApiSchema schema, ResolvedDocument document, int depth, int guard)
    {
        var s = Deref(schema, document);

        // Pathological non-union self-reference safety net (never bites a well-formed Vista document).
        if (guard <= 0)
        {
            return JsonValue.Create("sample");
        }

        if (s.OneOf is { Count: > 0 } variants)
        {
            // Cross the recursive union. When the budget is exhausted, pick a terminal (non-recursive)
            // variant so expansion always ends; otherwise pick the first variant in document order.
            var chosen = depth <= 0
                ? TerminalVariant(variants, document) ?? variants[0]
                : variants[0];

            return Canonical(chosen, document, depth - 1, guard - 1);
        }

        if (s.Enum is { Count: > 0 } enumValues)
        {
            return JsonValue.Create(enumValues[0]);
        }

        if (IsArray(s))
        {
            var array = new JsonArray();
            if (s.Items is { } itemSchema)
            {
                array.Add(Canonical(itemSchema, document, depth, guard - 1));
            }

            return array;
        }

        if (IsObject(s))
        {
            var obj = new JsonObject();
            if (s.Properties is { } properties)
            {
                foreach (var name in OrderedNames(properties))
                {
                    obj[name] = Canonical(properties[name], document, depth, guard - 1);
                }
            }

            return obj;
        }

        // A permissive ({}) or otherwise unconstrained schema, or a member with no declared type: a
        // representative scalar placeholder.
        if (s.AdditionalPropertiesOpen || s.Type is null)
        {
            return JsonValue.Create("sample");
        }

        return Scalar(s);
    }

    // ---- Schema helpers ----

    // Follows a local $ref to its target; returns the schema unchanged when it is not a reference or the
    // reference cannot be resolved (the resolve stage has already guaranteed every ref resolves).
    private static OpenApiSchema Deref(OpenApiSchema schema, ResolvedDocument document) =>
        schema.Ref is { } refValue && document.ResolveSchemaRef(refValue) is { } target
            ? target
            : schema;

    private static bool IsArray(OpenApiSchema schema) =>
        string.Equals(schema.Type, "array", StringComparison.Ordinal) || schema.Items is not null;

    private static bool IsObject(OpenApiSchema schema) =>
        schema.Properties is not null || string.Equals(schema.Type, "object", StringComparison.Ordinal);

    // A terminal variant is one that does not lead back into another union in a single step (for the Vista
    // FilterNode family this is FilterLeaf). Used only to close off recursion once the union budget is
    // spent.
    private static OpenApiSchema? TerminalVariant(IReadOnlyList<OpenApiSchema> variants, ResolvedDocument document)
    {
        foreach (var variant in variants)
        {
            var s = Deref(variant, document);
            if (s.Properties is not { } properties)
            {
                // A non-object variant (scalar/enum) is terminal.
                return variant;
            }

            var recursive = false;
            foreach (var property in properties.Values)
            {
                var propertyDeref = Deref(property, document);
                var itemDeref = propertyDeref.Items is { } items ? Deref(items, document) : null;
                if (propertyDeref.OneOf is not null || itemDeref?.OneOf is not null)
                {
                    recursive = true;
                    break;
                }
            }

            if (!recursive)
            {
                return variant;
            }
        }

        return null;
    }

    // Maps an OpenAPI scalar type/format to a representative JSON value (the scalar table of design §3.5).
    private static JsonNode Scalar(OpenApiSchema schema) => schema.Type switch
    {
        "integer" => JsonValue.Create(1),
        "number" => JsonValue.Create(1.5d),
        "boolean" => JsonValue.Create(true),
        "string" => JsonValue.Create(SampleString(schema.Format)),
        _ => JsonValue.Create("sample"),
    };

    private static string SampleString(string? format) => format switch
    {
        "uuid" => "00000000-0000-0000-0000-000000000000",
        "date-time" => "2024-01-01T00:00:00Z",
        "byte" => "c2FtcGxl", // base64("sample")
        _ => "sample",
    };

    // A fixed, ordinal, case-sensitive ordering by property name for deterministic, reproducible output.
    private static IEnumerable<string> OrderedNames(IReadOnlyDictionary<string, OpenApiSchema> properties)
    {
        var names = new List<string>(properties.Keys);
        names.Sort(StringComparer.Ordinal);
        return names;
    }
}
