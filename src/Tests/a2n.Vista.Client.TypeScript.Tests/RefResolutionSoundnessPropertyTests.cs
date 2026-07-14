// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for the resolve stage's <c>$ref</c> resolution soundness (task 5.2; Requirements
/// 1.7, 1.8; design Property 13). The resolve stage confirms every local <c>$ref</c> targets an existing
/// component before generation and reports the first dangling reference verbatim.
/// </summary>
/// <remarks>
/// <para>
/// Two properties are asserted, mirroring the two acceptance criteria:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Soundness (Requirement 1.7).</b> For a document whose every <c>$ref</c> targets a present
///     component — including cyclic edges such as a schema referencing itself —
///     <see cref="RefResolver.Resolve"/> returns <see cref="Result{T, E}.Ok"/>, and every <c>$ref</c> in
///     the resolved graph can be followed by name to a present component
///     (<see cref="ResolvedDocument.ResolveSchemaRef"/> / <c>Schemas.ContainsKey</c>). Because the whole
///     collection walk and the resolution itself complete, termination over cyclic graphs is demonstrated
///     (no infinite expansion, no stack overflow).
///   </item>
///   <item>
///     <b>Dangling (Requirement 1.8).</b> For a document containing exactly one <c>$ref</c> to a name that
///     is absent from <c>components.schemas</c>, <see cref="RefResolver.Resolve"/> returns
///     <see cref="Result{T, E}.Err"/> with <see cref="ResolveError.Dangling"/> whose
///     <see cref="ResolveError.Dangling.RefValue"/> is verbatim the injected dangling reference.
///   </item>
/// </list>
/// <para>
/// Documents are built directly through the model records. Every generated component schema places its
/// references across the three ref-bearing positions the resolver walks — object <c>properties</c>, array
/// <c>items</c>, and <c>oneOf</c> variants — so the property exercises each position.
/// </para>
/// </remarks>
public sealed class RefResolutionSoundnessPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The local <c>$ref</c> prefix for a component schema (mirrors the resolver's contract).</summary>
    private const string SchemaRefPrefix = "#/components/schemas/";

    // ---- Model construction helpers (build OpenApiDocument instances directly via the records) ----

    private static string RefValue(string name) => SchemaRefPrefix + name;

    /// <summary>A schema node that is purely a local <c>$ref</c> to <paramref name="name"/>.</summary>
    private static OpenApiSchema Ref(string name) =>
        new(RefValue(name), null, null, false, Array.Empty<string>(), null, null, null, null, false);

    /// <summary>A leaf scalar schema (used to pad objects and oneOf unions with a non-ref member).</summary>
    private static OpenApiSchema Scalar(string type) =>
        new(null, type, null, false, Array.Empty<string>(), null, null, null, null, false);

    /// <summary>An inline object schema over the supplied properties.</summary>
    private static OpenApiSchema Obj(Dictionary<string, OpenApiSchema> properties) =>
        new(null, "object", null, false, Array.Empty<string>(), properties, null, null, null, false);

    /// <summary>An inline array schema whose element type is <paramref name="items"/>.</summary>
    private static OpenApiSchema Arr(OpenApiSchema items) =>
        new(null, "array", null, false, Array.Empty<string>(), null, items, null, null, false);

    /// <summary>An inline <c>oneOf</c> union over the supplied variants.</summary>
    private static OpenApiSchema OneOf(IReadOnlyList<OpenApiSchema> variants) =>
        new(null, null, null, false, Array.Empty<string>(), null, null, variants, null, false);

    private static OpenApiDocument Document(IReadOnlyDictionary<string, OpenApiSchema> schemas) =>
        new(
            "3.0.4",
            new OpenApiInfo("a2n.Vista API", "1.0.0"),
            new Dictionary<string, OpenApiPathItem>(),
            new OpenApiComponents(schemas, new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal)),
            Array.Empty<OpenApiSecurityRequirement>());

    // ---- Generators ----

    /// <summary>A lower-case identifier of 1–6 letters, usable as a component name.</summary>
    private static readonly Gen<string> Name =
        Gen.Char['a', 'z'].Array[1, 6].Select(chars => new string(chars));

    /// <summary>A non-empty set of distinct component names (deduplicated; 1–6 members).</summary>
    private static readonly Gen<string[]> PresentNames =
        Name.Array[1, 6].Select(names => names.Distinct().ToArray());

    /// <summary>
    /// A component schema whose references (in <c>properties</c>, array <c>items</c>, and <c>oneOf</c>
    /// positions) all point to names in <paramref name="names"/>. Because a reference target may equal the
    /// component's own name, cyclic edges (including direct self-reference) are generated.
    /// </summary>
    private static Gen<OpenApiSchema> SoundComponentSchema(string[] names)
    {
        Gen<OpenApiSchema> refGen = Gen.OneOfConst(names).Select(Ref);

        return
            from includeProperty in Gen.Bool
            from propertyRef in refGen
            from includeItems in Gen.Bool
            from itemRef in refGen
            from includeOneOf in Gen.Bool
            from oneOfCount in Gen.Int[1, 3]
            from oneOfRefs in refGen.Array[oneOfCount]
            select BuildComponent(
                includeProperty ? propertyRef : null,
                includeItems ? itemRef : null,
                includeOneOf ? oneOfRefs : null);
    }

    private static OpenApiSchema BuildComponent(
        OpenApiSchema? propertyRef,
        OpenApiSchema? itemRef,
        OpenApiSchema[]? oneOfRefs)
    {
        var properties = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["leaf"] = Scalar("string"),
        };

        if (propertyRef is not null)
        {
            properties["child"] = propertyRef;              // ref in a property position
        }

        if (itemRef is not null)
        {
            properties["list"] = Arr(itemRef);              // ref in an array-items position
        }

        if (oneOfRefs is not null)
        {
            properties["choice"] = OneOf(oneOfRefs);        // ref in a oneOf-variant position
        }

        return Obj(properties);
    }

    /// <summary>A document whose every <c>$ref</c> targets a present component (soundness case).</summary>
    private static readonly Gen<OpenApiDocument> SoundDocument =
        from names in PresentNames
        from schemas in SoundComponentSchema(names).Array[names.Length]
        select Document(ZipToSchemas(names, schemas));

    /// <summary>
    /// A document identical to a sound one except that a single component references a name absent from
    /// <c>components.schemas</c>. The tuple carries the exact dangling <c>$ref</c> value injected.
    /// </summary>
    private static readonly Gen<(OpenApiDocument Document, string DanglingRef)> DanglingDocument =
        from names in PresentNames
        from schemas in SoundComponentSchema(names).Array[names.Length]
        from danglingName in Name.Where(candidate => !names.Contains(candidate))
        from position in Gen.Int[0, 2]
        select BuildDangling(names, schemas, danglingName, position);

    private static Dictionary<string, OpenApiSchema> ZipToSchemas(string[] names, OpenApiSchema[] schemas)
    {
        var map = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++)
        {
            map[names[i]] = schemas[i];
        }

        return map;
    }

    private static (OpenApiDocument, string) BuildDangling(
        string[] names,
        OpenApiSchema[] schemas,
        string danglingName,
        int position)
    {
        var map = ZipToSchemas(names, schemas);
        OpenApiSchema danglingRef = Ref(danglingName);

        // Overwrite the first present component so that it references the absent name in the chosen
        // ref-bearing position. It remains the only dangling reference anywhere in the document.
        map[names[0]] = position switch
        {
            0 => Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["bad"] = danglingRef,                                   // property position
            }),
            1 => Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["list"] = Arr(danglingRef),                             // items position
            }),
            _ => Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["choice"] = OneOf(new[] { Scalar("string"), danglingRef }), // oneOf position
            }),
        };

        return (Document(map), RefValue(danglingName));
    }

    /// <summary>
    /// Collects every <c>$ref</c> reachable from <paramref name="schema"/> by walking the inline structure
    /// (object properties, array items, oneOf variants) and stopping at each reference leaf. This mirrors
    /// the resolver's own walk and is inherently terminating because a schema tree is finite and a
    /// reference is never followed into its target.
    /// </summary>
    private static void CollectRefs(OpenApiSchema schema, List<string> sink)
    {
        if (schema.Ref is { } refValue)
        {
            sink.Add(refValue);
            return;
        }

        if (schema.Properties is { } properties)
        {
            foreach (var property in properties.Values)
            {
                CollectRefs(property, sink);
            }
        }

        if (schema.Items is { } items)
        {
            CollectRefs(items, sink);
        }

        if (schema.OneOf is { } oneOf)
        {
            foreach (var variant in oneOf)
            {
                CollectRefs(variant, sink);
            }
        }
    }

    // Feature: typescript-client, Property 13: $ref resolution soundness
    //
    // For a document whose every $ref targets a present component (including cyclic self-references),
    // Resolve returns Ok and every $ref in the resolved graph can be followed by name to a present
    // component. The walk and the resolution both completing demonstrates termination over cycles.
    //
    // Validates: Requirements 1.7
    [Test]
    public void All_Refs_To_Present_Components_Resolve_Ok_And_Are_Followable_By_Name()
    {
        SoundDocument.Sample(
            document =>
            {
                Result<ResolvedDocument, ResolveError> result = RefResolver.Resolve(document);

                if (result.IsError)
                {
                    throw new Exception(
                        "A document whose every $ref targets a present component must resolve to Ok, but " +
                        $"resolution failed: {result.Error.Message}");
                }

                ResolvedDocument resolved = result.Value;

                // Every $ref reachable in the resolved graph must be followable by name to a present
                // component. The walk terminates over cyclic edges because references are by-name edges.
                var refs = new List<string>();
                foreach (var schema in resolved.Schemas.Values)
                {
                    CollectRefs(schema, refs);
                }

                foreach (var refValue in refs)
                {
                    if (resolved.ResolveSchemaRef(refValue) is null)
                    {
                        throw new Exception(
                            $"Reference '{refValue}' could not be followed by name to a present component, " +
                            "yet resolution reported success.");
                    }

                    if (!ResolvedDocument.TryGetComponentName(refValue, SchemaRefPrefix, out var name)
                        || !resolved.Schemas.ContainsKey(name))
                    {
                        throw new Exception(
                            $"Reference '{refValue}' does not name a present component in the resolved graph.");
                    }
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 13: $ref resolution soundness
    //
    // For a document containing a $ref to a name absent from components.schemas, Resolve returns
    // Err(ResolveError.Dangling) whose RefValue is verbatim the injected dangling reference.
    //
    // Validates: Requirements 1.8
    [Test]
    public void A_Dangling_Ref_Aborts_With_Dangling_Carrying_The_Verbatim_Ref_Value()
    {
        DanglingDocument.Sample(
            testCase =>
            {
                (OpenApiDocument document, string danglingRef) = testCase;

                Result<ResolvedDocument, ResolveError> result = RefResolver.Resolve(document);

                if (result.IsOk)
                {
                    throw new Exception(
                        $"A document containing a dangling reference '{danglingRef}' must abort, but " +
                        "resolution succeeded.");
                }

                if (result.Error is not ResolveError.Dangling dangling)
                {
                    throw new Exception(
                        $"Expected ResolveError.Dangling for '{danglingRef}', but got " +
                        $"'{result.Error.GetType().Name}' ({result.Error.Message}).");
                }

                if (!string.Equals(dangling.RefValue, danglingRef, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Dangling reference named '{dangling.RefValue}', expected the verbatim injected " +
                        $"'{danglingRef}'.");
                }
            },
            iter: Iterations);
    }
}
