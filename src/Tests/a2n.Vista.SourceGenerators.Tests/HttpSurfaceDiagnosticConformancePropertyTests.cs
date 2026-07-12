// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 4 (M9, D123/D124, source-generator-http-surface) HTTP-surface
// diagnostic descriptors' contract (task 4.4).
//
// Feature: source-generator-http-surface, Property 9: Diagnostic id, category, and non-blocking severity
// conformance.
//
// Validates: Requirements 9.3, 9.4
//
// Strategy: R9.3 requires every HTTP-surface diagnostic to use the "a2n.Vista.SourceGenerators" category,
// the "VISTA####" id format, and a help link under docs/diagnostics/; R9.4 requires every HTTP-surface
// diagnostic to be NON-BLOCKING (Info or Warning, never Error) so an uncovered view is a valid, working
// view on the reflection fallback and the build is always green. These invariants must hold for EACH of
// the HTTP-surface descriptors (VISTA0040 HttpSurfaceCandidateUncovered and VISTA0041
// HttpSurfaceSerializationGuidance), so a CsCheck generator quantifies over the descriptor set — picking
// one descriptor per iteration — and asserts every invariant on the pick:
//
//   * Id matches `^VISTA[0-9]{4}$`                       (R9.3, Spec 03 D81 id format);
//   * Category == "a2n.Vista.SourceGenerators"           (R9.3);
//   * HelpLinkUri is non-null/non-empty and points under docs/diagnostics/  (R9.3);
//   * DefaultSeverity is Info or Warning — never Error   (R9.4, non-blocking).
//
// The descriptor set is read back from the (internal) DiagnosticDescriptors holder in the generator
// assembly by reflection — matching the project convention of no InternalsVisibleTo to the tests (see
// ViewInvokerGeneratorTestHarness). The HTTP-surface subset is selected by id ({ VISTA0040, VISTA0041 })
// so the property is scoped to exactly the diagnostics this feature owns, independent of the other Vista
// families (VISTA0001 etc. are legitimately Error and are not HTTP-surface diagnostics).
//
// Minimum 100 generated cases (CsCheck default iter = 100). With two HTTP-surface descriptors the random
// index selection exercises each descriptor many times over the run. PBT library: CsCheck (imperative
// Sample, pairs cleanly with TUnit [Test]). No generator is driven and no source is compiled: the property
// is a pure invariant over the shipped descriptor set.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CsCheck;
using Microsoft.CodeAnalysis;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class HttpSurfaceDiagnosticConformancePropertyTests
{
    // R9.3 / Spec 03 D81: the VISTA prefix immediately followed by exactly four decimal digits.
    private static readonly Regex DiagnosticIdPattern = new("^VISTA[0-9]{4}$", RegexOptions.Compiled);

    // R9.3: the shared diagnostic category all Vista generator diagnostics carry.
    private const string ExpectedCategory = "a2n.Vista.SourceGenerators";

    // R9.3: the help link must point at the per-diagnostic docs under docs/diagnostics/.
    private const string HelpLinkSegment = "docs/diagnostics/";

    // The HTTP-surface diagnostic family this feature (D123/D124) owns. Selecting by id keeps the property
    // scoped to exactly VISTA0040/VISTA0041, independent of the other Vista diagnostic families.
    private static readonly HashSet<string> HttpSurfaceIds = new(StringComparer.Ordinal)
    {
        "VISTA0040",
        "VISTA0041",
    };

    // The HTTP-surface descriptors, read back from the (internal) DiagnosticDescriptors holder in the
    // generator assembly by reflection (project convention: no InternalsVisibleTo to the tests).
    private static readonly DiagnosticDescriptor[] HttpSurfaceDescriptors =
        LoadHttpSurfaceDescriptors();

    // A CsCheck generator that quantifies over the HTTP-surface descriptor set — one descriptor per case.
    private static readonly Gen<DiagnosticDescriptor> GenHttpSurfaceDescriptor =
        Gen.Int[0, HttpSurfaceDescriptors.Length - 1].Select(i => HttpSurfaceDescriptors[i]);

    [Test]
    public void Every_HttpSurface_Descriptor_Conforms_To_Id_Category_HelpLink_And_NonBlocking_Severity()
    {
        // Feature: source-generator-http-surface, Property 9: Diagnostic id, category, and non-blocking
        // severity conformance.

        // Guard: the reflection lookup must actually find the HTTP-surface descriptors, otherwise a
        // vacuously-true property could hide a rename/removal of VISTA0040/VISTA0041.
        if (HttpSurfaceDescriptors.Length != HttpSurfaceIds.Count)
        {
            throw new InvalidOperationException(
                $"Expected {HttpSurfaceIds.Count} HTTP-surface descriptors ({string.Join(", ", HttpSurfaceIds)}), " +
                $"found {HttpSurfaceDescriptors.Length}: [{string.Join(", ", HttpSurfaceDescriptors.Select(d => d.Id))}].");
        }

        GenHttpSurfaceDescriptor.Sample(
            descriptor =>
            {
                // R9.3: VISTA#### id format.
                if (!DiagnosticIdPattern.IsMatch(descriptor.Id))
                {
                    return false;
                }

                // R9.3: shared category.
                if (!string.Equals(descriptor.Category, ExpectedCategory, StringComparison.Ordinal))
                {
                    return false;
                }

                // R9.3: a help link under docs/diagnostics/.
                if (string.IsNullOrEmpty(descriptor.HelpLinkUri) ||
                    descriptor.HelpLinkUri.IndexOf(HelpLinkSegment, StringComparison.Ordinal) < 0)
                {
                    return false;
                }

                // R9.4: non-blocking severity — Info or Warning, never Error.
                if (descriptor.DefaultSeverity != DiagnosticSeverity.Info &&
                    descriptor.DefaultSeverity != DiagnosticSeverity.Warning)
                {
                    return false;
                }

                return true;
            },
            iter: 100,
            // On failure, print the offending descriptor's id/category/severity/help-link for a
            // reproducible example.
            print: d =>
                $"{d.Id} (category='{d.Category}', severity={d.DefaultSeverity}, helpLink='{d.HelpLinkUri}')");
    }

    /// <summary>
    /// Reads the HTTP-surface <see cref="DiagnosticDescriptor"/>s ({ VISTA0040, VISTA0041 }) back from the
    /// generator assembly's internal <c>DiagnosticDescriptors</c> holder by reflection, so the (internal)
    /// holder need not be visible to the test assembly (matching the no-InternalsVisibleTo convention).
    /// </summary>
    private static DiagnosticDescriptor[] LoadHttpSurfaceDescriptors()
    {
        // ViewInvokerGenerator lives in the generator assembly alongside DiagnosticDescriptors.
        var generatorAssembly = typeof(ViewInvokerGenerator).Assembly;
        var holder = generatorAssembly.GetType("a2n.Vista.SourceGenerators.DiagnosticDescriptors", throwOnError: true)!;

        return holder
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .Where(d => HttpSurfaceIds.Contains(d.Id))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
