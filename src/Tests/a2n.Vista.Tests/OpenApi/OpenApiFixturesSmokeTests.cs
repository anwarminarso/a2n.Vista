// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Smoke tests proving the OpenAPI emitter test INFRASTRUCTURE (spec openapi-emitter, task 8.1) compiles and
// works, so the numbered property tests (tasks 8.2–8.5, 9.x) have a working foundation. These are NOT the
// numbered properties: they only assert that
//   (1) the compile-once representative registry (EmitterFixtures) builds a non-empty, buildable OpenAPI
//       document via the real VistaOpenApiDocumentBuilder (paths + components present), and
//   (2) the RegistryGenerators produce registries whose view names (and hence routes) are globally unique,
//       and whose output the builder can consume end-to-end.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.Metadata;
using a2n.Vista.OpenApi;
using a2n.Vista.OpenApi.Model;
using CsCheck;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Infrastructure smoke tests for the emitter-consumer fixtures and registry generators (task 8.1). See the
/// file header: these prove the shared test infrastructure works, not the numbered emitter properties.
/// </summary>
public sealed class OpenApiFixturesSmokeTests
{
    /// <summary>A handful of samples is enough for a smoke check (the numbered properties run ≥100).</summary>
    private const int SmokeIterations = 50;

    [RequiresUnreferencedCode("Exercises the RUC document builder over representative CLR row/CRUD types.")]
    private static OpenApiDocument BuildRepresentative()
    {
        var builder = new VistaOpenApiDocumentBuilder(
            EmitterFixtures.Registry(),
            EmitterFixtures.SeamOptions(),
            new VistaEndpointOptions(),
            new VistaOpenApiOptions(),
            EmitterFixtures.WriteFacets());
        return builder.Build();
    }

    // ---- Fixture smoke: the representative registry builds a non-empty document -----------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over representative CLR row/CRUD types.")]
    public async Task Representative_Registry_Builds_A_NonEmpty_Document()
    {
        var document = BuildRepresentative();

        // A buildable document with paths and components proves the fixtures compile and emit real schemas.
        await Assert.That(document.Paths).IsNotNull();
        await Assert.That(document.Paths!.Count).IsGreaterThan(0);
        await Assert.That(document.Components).IsNotNull();
        await Assert.That(document.Components!.Schemas).IsNotNull();
        await Assert.That(document.Components!.Schemas!.Count).IsGreaterThan(0);

        // The three representative views contribute their routes: read-only (4 facets) + composite (4) +
        // writable (7) = 15 operation paths.
        await Assert.That(document.Paths!.ContainsKey(EmitterFixtures.CatalogItemRoute + "/list")).IsTrue();
        await Assert.That(document.Paths!.ContainsKey(EmitterFixtures.GeoZoneRoute + "/metadata")).IsTrue();
        await Assert.That(document.Paths!.ContainsKey(EmitterFixtures.SubscriptionRoute + "/create")).IsTrue();

        // The writable view's TCrud (SubscriptionCrud) reflects into a real component schema.
        await Assert.That(document.Components!.Schemas!.ContainsKey(nameof(EmitterFixtures.SubscriptionCrud))).IsTrue();
    }

    // ---- Generator smoke: names/routes are globally unique in every generated registry ----------------

    [Test]
    public void Generated_Registries_Have_Globally_Unique_View_Names_And_Routes()
    {
        RegistryGenerators.Registry().Sample(
            generated =>
            {
                var names = generated.Views.Select(v => v.Name).ToArray();
                var routes = generated.Views.Select(v => v.Route).ToArray();

                if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                {
                    throw new Exception(
                        "Generated registry has duplicate view names: " + string.Join(", ", names));
                }

                if (routes.Distinct(StringComparer.Ordinal).Count() != routes.Length)
                {
                    throw new Exception(
                        "Generated registry has duplicate routes: " + string.Join(", ", routes));
                }

                // Every writable view is represented on the write-facet registry (token or not).
                foreach (var view in generated.Views.Where(v => !v.IsReadOnly))
                {
                    if (!generated.WriteFacets.TryGet(view.Name, out _))
                    {
                        throw new Exception(
                            $"Writable view '{view.Name}' is missing from the generated write-facet registry.");
                    }
                }
            },
            iter: SmokeIterations);
    }

    // ---- Generator smoke: the builder consumes generated registries end-to-end ------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over generated registries.")]
    public void Generated_Registries_Build_A_Document_With_Resolvable_Refs()
    {
        RegistryGenerators.Registry().Sample(
            generated =>
            {
                var builder = new VistaOpenApiDocumentBuilder(
                    generated.Registry,
                    EmitterFixtures.SeamOptions(),
                    new VistaEndpointOptions(),
                    new VistaOpenApiOptions(),
                    generated.WriteFacets);

                var document = builder.Build();

                if (document.Paths is null || document.Paths.Count == 0)
                {
                    throw new Exception("Generated registry produced a document with no paths.");
                }

                // Referential-integrity sanity (a precursor to Property 5): every operation path ends in a
                // known core facet suffix, never an adapter suffix (a precursor to Property 10).
                var coreSuffixes = new HashSet<string>(StringComparer.Ordinal)
                {
                    "list", "detail", "metadata", "export", "create", "update", "delete",
                };
                foreach (var path in document.Paths.Keys)
                {
                    var suffix = path[(path.LastIndexOf('/') + 1)..];
                    if (!coreSuffixes.Contains(suffix))
                    {
                        throw new Exception($"Unexpected non-core operation path '{path}'.");
                    }
                }
            },
            iter: SmokeIterations);
    }
}
