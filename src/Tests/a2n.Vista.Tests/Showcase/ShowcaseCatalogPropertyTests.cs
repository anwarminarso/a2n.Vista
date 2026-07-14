// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Northwind sample showcase property test (spec northwind-sample-showcase, task 1.3).
//
// Property 2: The catalog is exactly the registered views (secure-by-default).
//   For any set of registered views in IViewRegistry, ShowcaseCatalog.Project(registry) yields exactly
//   one catalog entry per registered view — each carrying that view's Name and Route — and yields NO
//   entry that does not correspond to a registered view (a bijection onto IViewRegistry.All, including
//   the empty case). No arbitrary/unregistered source can ever appear in the catalog.
//
// Validates: Requirements 2.6, 4.1, 4.2.
//
// Oracle: the in-process IViewRegistry — the structural registry generator (RegistryGenerators, reused
// from the OpenAPI emitter suite) produces arbitrary view sets with unique names/routes, and this test
// asserts the catalog is a (Name, Route) bijection onto registry.All. The generator range starts at
// zero views so the empty-registry case (yields []) is exercised too (Requirement R4.5 projection side).
//
// CsCheck-via-TUnit idiom: Gen<GeneratedRegistry>.Sample(action, iter: 100) at >=100 iterations, matching
// the sibling structural property suites.
//
// The Northwind example host is net8.0-only (it overrides the repo-wide multi-target), so this test — and
// the project reference it needs — are scoped to net8.0; on net9.0/net10.0 the file compiles to nothing.

#if NET8_0

using System;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Examples.AgGridNorthwind.Showcase;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property 2 (task 1.3): <see cref="ShowcaseCatalog.Project"/> is a secure-by-default bijection onto
/// <see cref="a2n.Vista.Ports.IViewRegistry.All"/> — exactly one catalog entry per registered view (carrying
/// its <see cref="ViewMetadata.Name"/> and <see cref="ViewMetadata.Route"/>), no extra entry, and the empty
/// registry yields an empty catalog (Requirements 2.6, 4.1, 4.2).
/// </summary>
public sealed class ShowcaseCatalogPropertyTests
{
    /// <summary>Minimum iterations per the design "Testing Strategy" (CsCheck via TUnit, >=100).</summary>
    private const int Iterations = 100;

    /// <summary>
    /// Property 2: for any registry of 0..N registered views, the projected catalog is exactly the
    /// registered views by <c>(Name, Route)</c> — one entry per view, no extra entry, and <c>[]</c> for the
    /// empty registry.
    /// </summary>
    [Test]
    public void Catalog_Is_Exactly_The_Registered_Views()
    {
        // Feature: northwind-sample-showcase, Property 2: The catalog is exactly the registered views (secure-by-default)
        RegistryGenerators.Registry(minViews: 0).Sample(
            generated =>
            {
                var registry = generated.Registry;
                var catalog = ShowcaseCatalog.Project(registry);

                // Empty-registry case: the projection yields an empty list (R4.5 projection side).
                if (registry.All.Count == 0)
                {
                    if (catalog.Count != 0)
                    {
                        throw new Exception(
                            $"An empty registry must project to an empty catalog, but {catalog.Count} " +
                            "entry/entries were produced (Requirement 4.5).");
                    }

                    return;
                }

                // Cardinality: exactly one entry per registered view — no drop, no duplication.
                if (catalog.Count != registry.All.Count)
                {
                    throw new Exception(
                        $"The catalog must carry exactly one entry per registered view: registry has " +
                        $"{registry.All.Count} view(s) but the catalog has {catalog.Count} entry/entries " +
                        "(Requirement 4.1).");
                }

                // Bijection on (Name, Route): the set of catalog (Name, Route) pairs equals the set of
                // registered (Name, Route) pairs — every registered view appears, and no entry that does
                // not correspond to a registered view can appear (secure-by-default; Requirements 2.6, 4.2).
                var registered = registry.All
                    .Select(v => (v.Name, v.Route))
                    .ToHashSet();
                var projected = catalog
                    .Select(e => (e.Name, e.Route))
                    .ToHashSet();

                // No extra entry: every projected pair is a registered view.
                var extras = projected.Except(registered).ToArray();
                if (extras.Length > 0)
                {
                    throw new Exception(
                        "The catalog contains entries that do not correspond to any registered view " +
                        "(secure-by-default violation, Requirements 2.6, 4.2): " +
                        string.Join(", ", extras.Select(p => $"({p.Name} @ {p.Route})")));
                }

                // No missing entry: every registered view appears in the catalog.
                var missing = registered.Except(projected).ToArray();
                if (missing.Length > 0)
                {
                    throw new Exception(
                        "The catalog is missing entries for registered views (Requirement 4.1): " +
                        string.Join(", ", missing.Select(p => $"({p.Name} @ {p.Route})")));
                }

                // Because registered names are unique within a registry, equal extra-free/missing-free sets
                // of equal cardinality confirm the (Name, Route) bijection; also assert each entry's Route
                // is the exact route carried by its named view (read verbatim, never re-composed).
                var routeByName = registry.All.ToDictionary(v => v.Name, v => v.Route, StringComparer.Ordinal);
                foreach (var entry in catalog)
                {
                    if (!routeByName.TryGetValue(entry.Name, out var expectedRoute))
                    {
                        throw new Exception(
                            $"Catalog entry '{entry.Name}' names no registered view (Requirements 2.6, 4.2).");
                    }

                    if (!string.Equals(entry.Route, expectedRoute, StringComparison.Ordinal))
                    {
                        throw new Exception(
                            $"Catalog entry '{entry.Name}' carries route '{entry.Route}' but the registered " +
                            $"view's route is '{expectedRoute}' (Requirement 4.1).");
                    }
                }
            },
            iter: Iterations);
    }
}

#endif
