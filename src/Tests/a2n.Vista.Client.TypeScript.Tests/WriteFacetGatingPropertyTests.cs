// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for write-facet gating (task 7.9; Requirements 5.1, 5.2, 5.3; design Property 6). A
/// <c>View_Client</c> must expose create/update/delete operations <em>if and only if</em> write-facet
/// generation is enabled <b>and</b> the view is writable; in every other combination it exposes no write
/// operation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layer under test.</b> The gated write-emit path (task 10.7) is not yet landed:
/// <see cref="ViewClientEmitter"/> emits only the read facets by construction and takes no
/// <see cref="GenerationConfig"/>. Property 6 is therefore verified at the two layers that <em>do</em>
/// express the gating deterministically today:
/// </para>
/// <list type="number">
///   <item>
///     <b>Model gating (Requirements 5.2, 5.3).</b> <see cref="OperationGraphBuilder"/> derives a view's
///     <see cref="FacetModel"/> set from the operations present in the document. A writable view — one whose
///     <c>View_Operation_Set</c> contains the <c>create</c>/<c>update</c>/<c>delete</c> paths — yields exactly
///     those three write facets and a non-null <see cref="ViewModel.CrudType"/>; a read-only view yields no
///     write facet and a null <c>CrudType</c>. This is the deterministic writability classification every
///     downstream emitter (including the pending 10.7) keys off.
///   </item>
///   <item>
///     <b>Read-emitter gate-off (Requirement 5.1).</b> The read-client emitter
///     (<see cref="ViewClientEmitter.Emit(ViewModel)"/>) emits <em>no</em> create/update/delete operation for
///     <em>any</em> view, writable or not. This is exactly the <c>EmitWriteFacets = false</c> behavior
///     (the default, Requirement 5.1 and <see cref="GenerationConfig.EmitWriteFacets"/>) realized by
///     construction: the only write-emitting component is the opt-in task-10.7 path.
///   </item>
/// </list>
/// <para>
/// Together these establish the "only if" direction fully (no write op is ever emitted with the gate off,
/// and none is ever modeled for a read-only view) and the "if" direction at the model level (a writable view
/// is correctly classified so 10.7 can emit its writes). Once task 10.7 lands, this property should be
/// extended to assert, at the emitter level, that <c>EmitWriteFacets = true</c> + writable emits the three
/// write methods while every other combination does not.
/// </para>
/// </remarks>
public sealed class WriteFacetGatingPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy — ≥ 100).</summary>
    private const int Iterations = 100;

    /// <summary>The local <c>$ref</c> prefix for a component schema.</summary>
    private const string SchemaRefPrefix = "#/components/schemas/";

    /// <summary>The one shared list-request envelope every list facet references.</summary>
    private const string ListRequestBodyName = "VistaListRequestBody";

    /// <summary>The three write-facet suffixes a writable view must expose (and a read-only view must not).</summary>
    private static readonly string[] WriteSuffixes =
    [
        OperationGraphBuilder.CreateSuffix,
        OperationGraphBuilder.UpdateSuffix,
        OperationGraphBuilder.DeleteSuffix,
    ];

    // ---- Model construction helpers (build OpenApiDocument instances directly via the records) ----

    private static string RefValue(string name) => SchemaRefPrefix + name;

    /// <summary>A schema node that is purely a local <c>$ref</c> to <paramref name="name"/>.</summary>
    private static OpenApiSchema Ref(string name) =>
        new(RefValue(name), null, null, false, Array.Empty<string>(), null, null, null, null, false);

    /// <summary>A minimal inline object component schema (one scalar leaf) usable as a referenced target.</summary>
    private static OpenApiSchema Component() =>
        new(
            null,
            "object",
            null,
            false,
            Array.Empty<string>(),
            new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["leaf"] = new(null, "string", null, false, Array.Empty<string>(), null, null, null, null, false),
            },
            null,
            null,
            null,
            false);

    /// <summary>An operation with the given id, optional request-body ref, and 2xx (+ optional concurrency) responses.</summary>
    private static OpenApiOperation Operation(
        string operationId,
        string? requestBodyRef,
        bool tokenBearing)
    {
        OpenApiRequestBody? requestBody = requestBodyRef is null
            ? null
            : new OpenApiRequestBody(true, Ref(requestBodyRef));

        var responses = new Dictionary<string, OpenApiResponse>(StringComparer.Ordinal)
        {
            // An empty-bodied 200: the operation graph treats an unnamed success as the raw payload, so no
            // extra component ref is needed to keep the document resolvable.
            ["200"] = new OpenApiResponse(null),
        };

        if (tokenBearing)
        {
            responses["428"] = new OpenApiResponse(null);
            responses["409"] = new OpenApiResponse(null);
        }

        return new OpenApiOperation(
            operationId,
            requestBody,
            responses,
            Array.Empty<OpenApiSecurityRequirement>());
    }

    private static OpenApiDocument Document(IReadOnlyDictionary<string, OpenApiSchema> schemas, IReadOnlyDictionary<string, OpenApiPathItem> paths) =>
        new(
            "3.0.4",
            new OpenApiInfo("a2n.Vista API", "1.0.0"),
            paths,
            new OpenApiComponents(schemas, new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal)),
            Array.Empty<OpenApiSecurityRequirement>());

    // Builds a full document from a set of view specs: each view always exposes the list + metadata read
    // facets, and a writable view additionally exposes create/update/delete (update/delete token-bearing).
    private static OpenApiDocument BuildDocument(IReadOnlyList<ViewSpec> specs)
    {
        var schemas = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            [ListRequestBodyName] = Component(),
        };
        var paths = new Dictionary<string, OpenApiPathItem>(StringComparer.Ordinal);

        foreach (var spec in specs)
        {
            var root = "/api/views/" + spec.Name;

            void AddFacet(string suffix, string method, string? requestBodyRef, bool tokenBearing)
            {
                var operationId = spec.Name + "_" + suffix;
                var operations = new Dictionary<string, OpenApiOperation>(StringComparer.Ordinal)
                {
                    [method] = Operation(operationId, requestBodyRef, tokenBearing),
                };
                paths[root + "/" + suffix] = new OpenApiPathItem(operations);
            }

            // Read facets present for every mapped view.
            AddFacet(OperationGraphBuilder.ListSuffix, "post", ListRequestBodyName, tokenBearing: false);
            AddFacet(OperationGraphBuilder.MetadataSuffix, "get", requestBodyRef: null, tokenBearing: false);

            if (spec.Writable)
            {
                var crudName = "Crud_" + spec.Name;
                schemas[crudName] = Component();

                AddFacet(OperationGraphBuilder.CreateSuffix, "post", crudName, tokenBearing: false);
                AddFacet(OperationGraphBuilder.UpdateSuffix, "post", crudName, tokenBearing: true);
                AddFacet(OperationGraphBuilder.DeleteSuffix, "post", requestBodyRef: null, tokenBearing: true);
            }
        }

        return Document(schemas, paths);
    }

    // ---- Generators ----

    /// <summary>A lower-case identifier of 3–7 letters, usable as a view name and a URL segment.</summary>
    private static readonly Gen<string> ViewName =
        Gen.Char['a', 'z'].Array[3, 7].Select(chars => new string(chars));

    /// <summary>A single view spec: a name and whether it is writable.</summary>
    private static readonly Gen<ViewSpec> ViewSpecGen =
        from name in ViewName
        from writable in Gen.Bool
        select new ViewSpec(name, writable);

    /// <summary>
    /// A non-empty set of view specs with distinct names (so each maps to a distinct endpoint root), mixing
    /// writable and read-only views within one document.
    /// </summary>
    private static readonly Gen<IReadOnlyList<ViewSpec>> ViewSpecsGen =
        ViewSpecGen.Array[1, 5]
            .Select(specs => (IReadOnlyList<ViewSpec>)specs
                .GroupBy(spec => spec.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray());

    // Feature: typescript-client, Property 6: Write-facet gating
    //
    // For any valid document mixing writable and read-only views, the operation graph models the three write
    // facets (create/update/delete) and a non-null CrudType for exactly the writable views and for no other,
    // and the read-client emitter emits no write operation for ANY view (the EmitWriteFacets=off default,
    // Requirement 5.1). This is the deterministic gating the pending task-10.7 emitter keys off.
    //
    // Validates: Requirements 5.1, 5.2, 5.3
    [Test]
    public void Write_Facets_Are_Modeled_Iff_Writable_And_The_Read_Emitter_Never_Emits_Them()
    {
        ViewSpecsGen.Sample(
            specs =>
            {
                var specByName = specs.ToDictionary(spec => spec.Name, StringComparer.Ordinal);
                var document = BuildDocument(specs);

                var resolved = RefResolver.Resolve(document);
                if (resolved.IsError)
                {
                    throw new Exception(
                        $"A well-formed document must resolve to Ok, but resolution failed: {resolved.Error.Message}");
                }

                var notices = new NoticeCollector();
                var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(resolved.Value, notices);
                var views = new OperationGraphBuilder().Build(resolved.Value, reLift, notices);

                // Every generated view corresponds to exactly one spec, and every spec becomes a view.
                if (views.Count != specs.Count)
                {
                    throw new Exception(
                        $"Expected one modeled view per spec ({specs.Count}), but got {views.Count}.");
                }

                foreach (var view in views)
                {
                    if (!specByName.TryGetValue(view.ViewName, out var spec))
                    {
                        throw new Exception($"Modeled an unexpected view '{view.ViewName}'.");
                    }

                    var presentWriteSuffixes = view.Facets
                        .Select(facet => facet.Suffix)
                        .Where(suffix => WriteSuffixes.Contains(suffix))
                        .ToHashSet(StringComparer.Ordinal);

                    if (spec.Writable)
                    {
                        // Requirement 5.2: a writable view is modeled with all three write facets and a
                        // bound TCrud write model.
                        foreach (var writeSuffix in WriteSuffixes)
                        {
                            if (!presentWriteSuffixes.Contains(writeSuffix))
                            {
                                throw new Exception(
                                    $"Writable view '{view.ViewName}' is missing the '{writeSuffix}' facet " +
                                    $"(present write facets: {string.Join(", ", presentWriteSuffixes)}).");
                            }
                        }

                        if (view.CrudType is null)
                        {
                            throw new Exception(
                                $"Writable view '{view.ViewName}' must bind a non-null CrudType (Requirement 5.2).");
                        }
                    }
                    else
                    {
                        // Requirement 5.3: a read-only view is modeled with no write facet and no TCrud.
                        if (presentWriteSuffixes.Count != 0)
                        {
                            throw new Exception(
                                $"Read-only view '{view.ViewName}' must have no write facet, but modeled: " +
                                $"{string.Join(", ", presentWriteSuffixes)} (Requirement 5.3).");
                        }

                        if (view.CrudType is not null)
                        {
                            throw new Exception(
                                $"Read-only view '{view.ViewName}' must have a null CrudType, but bound " +
                                $"'{view.CrudType.Render()}' (Requirement 5.3).");
                        }
                    }

                    // Requirement 5.1: with write-facet generation off (the default — and the only behavior
                    // the read emitter realizes), NO create/update/delete operation is emitted on any view.
                    var content = ViewClientEmitter.Emit(view).Content;
                    foreach (var writeSuffix in WriteSuffixes)
                    {
                        var methodDeclaration = writeSuffix + "(";
                        if (content.Contains(methodDeclaration, StringComparison.Ordinal))
                        {
                            throw new Exception(
                                $"The read-client emitter emitted a '{writeSuffix}' operation on view " +
                                $"'{view.ViewName}' with write facets off (Requirement 5.1).");
                        }
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>A generated view spec: a distinct view name and whether the view is writable.</summary>
    private sealed record ViewSpec(string Name, bool Writable);
}
