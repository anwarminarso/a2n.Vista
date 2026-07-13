// Licensed to the a2n.Vista project. Published artifact — English only.
//
// OpenAPI emitter STRUCTURAL property test (spec openapi-emitter, task 9.4).
//
// Property 8: Security requirement tracks the anonymity posture.
//   For any set of registered views: when the app is NOT anonymous, components.securitySchemes contains the
//   configured (or default bearer) scheme and EVERY emitted operation carries that SAME security
//   requirement (naming exactly that scheme); when the app IS anonymous (AllowAnonymousAccess()), NO
//   operation carries a security requirement (and no scheme is emitted).
//
// Validates: Requirements 7.1, 7.3, 7.4.
//
// Oracle: the live route table (IViewRegistry) — the structural registry generator (RegistryGenerators)
// produces arbitrary view sets, the real VistaOpenApiDocumentBuilder builds the document, and this test
// asserts the security posture over every emitted operation (Get + Post across all paths).
//
// CsCheck-via-TUnit idiom: Gen<GeneratedRegistry>.Sample(action, iter: 100) at ≥100 iterations, matching
// the sibling structural suites. The builder's Build() is [RequiresUnreferencedCode] (per-view DTO schema
// generation reflects over CLR row/CRUD types, D96 asymmetry); security posture is purely structural, but
// building the whole document drives that reflection branch, so the trim warning is suppressed at the class
// level, matching the sibling emitter/oracle property suites.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.OpenApi;
using a2n.Vista.OpenApi.Model;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property 8 (task 9.4): the emitted document's security posture tracks the anonymity flag. Structural
/// property over arbitrary registries (the registry is the oracle) covering Requirements 7.1, 7.3, 7.4:
/// the default bearer scheme when not anonymous, a configured scheme override, and the anonymous case with
/// no security at all.
/// </summary>
/// <remarks>
/// <see cref="VistaOpenApiDocumentBuilder.Build"/> is <c>[RequiresUnreferencedCode]</c> because it collects
/// per-view DTO schemas by reflecting over the row/write CLR types. The security posture is purely
/// structural (schemes + per-operation requirements), but building the whole document drives that reflection
/// branch, so the trim warning is suppressed at the class level, matching the sibling emitter/oracle
/// property suites.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "The property builds the full document, which drives the RUC per-view DTO schema reflection by design; trimming is not used for tests.")]
public sealed class SecurityPosturePropertyTests
{
    /// <summary>Minimum iterations per the design "Testing Strategy" (CsCheck via TUnit, ≥100).</summary>
    private const int Iterations = 100;

    /// <summary>
    /// Enumerates every emitted operation of a document as <c>(path, method, operation)</c> tuples, across
    /// both the <c>GET</c> (metadata) and <c>POST</c> (list/detail/export/write) slots of every path item.
    /// </summary>
    private static IEnumerable<(string Path, string Method, OpenApiOperation Operation)> EnumerateOperations(
        OpenApiDocument document)
    {
        if (document.Paths is null)
        {
            yield break;
        }

        foreach (var (path, item) in document.Paths)
        {
            if (item.Get is not null)
            {
                yield return (path, "GET", item.Get);
            }

            if (item.Post is not null)
            {
                yield return (path, "POST", item.Post);
            }
        }
    }

    /// <summary>
    /// Returns the single scheme name referenced by an operation's one-door <c>security</c> requirement, or
    /// <c>null</c> when the operation has no requirement or the requirement is not a single-scheme, single-
    /// alternative shape (which itself would be a violation the caller reports).
    /// </summary>
    private static string? OnlySchemeName(OpenApiOperation operation)
    {
        var requirement = operation.Security;
        if (requirement is null || requirement.Count != 1)
        {
            return null;
        }

        var alternative = requirement[0];
        return alternative.Count == 1 ? alternative.Keys.Single() : null;
    }

    /// <summary>
    /// Property 8 (not anonymous, default scheme): over arbitrary registries built with the default
    /// <see cref="VistaOpenApiOptions"/> and a non-anonymous <see cref="VistaEndpointOptions"/>,
    /// <c>components.securitySchemes</c> contains the default HTTP <c>bearer</c> scheme and every emitted
    /// operation carries the same one-door <c>security</c> requirement naming exactly that scheme
    /// (Requirements 7.1, 7.4).
    /// </summary>
    [Test]
    public void NotAnonymous_Default_Emits_Bearer_And_Every_Operation_References_It()
    {
        // Feature: openapi-emitter, Property 8: Security requirement tracks the anonymity posture
        RegistryGenerators.Registry().Sample(
            generated =>
            {
                var document = new VistaOpenApiDocumentBuilder(
                    generated.Registry,
                    EmitterFixtures.SeamOptions(),
                    new VistaEndpointOptions(),          // AllowAnonymous defaults to false
                    new VistaOpenApiOptions(),           // Security null -> default HTTP bearer
                    generated.WriteFacets).Build();

                AssertSchemeEmittedAndReferenced(document, "bearer", generated.Views.Count);
            },
            iter: Iterations);
    }

    /// <summary>
    /// Property 8 (not anonymous, configured scheme): over arbitrary registries built with a host-configured
    /// <see cref="VistaSecurityScheme"/>, the CONFIGURED scheme name (not the default <c>bearer</c>) appears
    /// under <c>components.securitySchemes</c> and every emitted operation references THAT name
    /// (Requirement 7.4 with the 7.2 override in effect).
    /// </summary>
    [Test]
    public void NotAnonymous_Configured_Scheme_Is_Emitted_And_Referenced_Everywhere()
    {
        // Feature: openapi-emitter, Property 8: Security requirement tracks the anonymity posture
        const string schemeName = "apiKeyScheme";
        var configured = new VistaSecurityScheme(schemeName, "apiKey", string.Empty, null);

        RegistryGenerators.Registry().Sample(
            generated =>
            {
                var document = new VistaOpenApiDocumentBuilder(
                    generated.Registry,
                    EmitterFixtures.SeamOptions(),
                    new VistaEndpointOptions(),          // AllowAnonymous defaults to false
                    new VistaOpenApiOptions { Security = configured },
                    generated.WriteFacets).Build();

                var schemes = document.Components?.SecuritySchemes;
                if (schemes is null || !schemes.ContainsKey(schemeName))
                {
                    throw new Exception(
                        $"The configured security scheme '{schemeName}' is missing from " +
                        "components.securitySchemes (Requirement 7.2/7.4). Emitted: " +
                        (schemes is null ? "(none)" : string.Join(", ", schemes.Keys)));
                }

                if (schemes.ContainsKey("bearer"))
                {
                    throw new Exception(
                        "The default 'bearer' scheme was emitted even though a custom scheme " +
                        $"'{schemeName}' was configured (Requirement 7.2).");
                }

                AssertSchemeEmittedAndReferenced(document, schemeName, generated.Views.Count);
            },
            iter: Iterations);
    }

    /// <summary>
    /// Property 8 (anonymous): over arbitrary registries built with
    /// <c>AllowAnonymous = true</c>, NO operation carries a <c>security</c> requirement and no scheme is
    /// emitted under <c>components.securitySchemes</c> (Requirement 7.3).
    /// </summary>
    [Test]
    public void Anonymous_Emits_No_Scheme_And_No_Operation_Security()
    {
        // Feature: openapi-emitter, Property 8: Security requirement tracks the anonymity posture
        RegistryGenerators.Registry().Sample(
            generated =>
            {
                var document = new VistaOpenApiDocumentBuilder(
                    generated.Registry,
                    EmitterFixtures.SeamOptions(),
                    new VistaEndpointOptions { AllowAnonymous = true },
                    new VistaOpenApiOptions(),
                    generated.WriteFacets).Build();

                var schemes = document.Components?.SecuritySchemes;
                if (schemes is not null && schemes.Count > 0)
                {
                    throw new Exception(
                        "An anonymous app must emit no security scheme, but components.securitySchemes " +
                        "contained: " + string.Join(", ", schemes.Keys) + " (Requirement 7.3).");
                }

                if (document.Security is not null)
                {
                    throw new Exception(
                        "An anonymous app must emit no document-level security requirement (Requirement 7.3).");
                }

                foreach (var (path, method, operation) in EnumerateOperations(document))
                {
                    if (operation.Security is not null)
                    {
                        throw new Exception(
                            $"Operation {method} {path} carries a security requirement under an anonymous " +
                            "app; none is allowed (Requirement 7.3).");
                    }
                }
            },
            iter: Iterations);
    }

    /// <summary>
    /// Asserts the shared not-anonymous invariant: the expected scheme is emitted exactly once under
    /// <c>components.securitySchemes</c>, at least one operation exists, and every emitted operation carries
    /// the same single-scheme, single-alternative <c>security</c> requirement naming exactly
    /// <paramref name="expectedScheme"/> (Requirements 7.1, 7.4).
    /// </summary>
    private static void AssertSchemeEmittedAndReferenced(
        OpenApiDocument document,
        string expectedScheme,
        int viewCount)
    {
        var schemes = document.Components?.SecuritySchemes;
        if (schemes is null || !schemes.ContainsKey(expectedScheme))
        {
            throw new Exception(
                $"Expected security scheme '{expectedScheme}' under components.securitySchemes but found: " +
                (schemes is null ? "(none)" : string.Join(", ", schemes.Keys)) +
                $" (Requirement 7.1; {viewCount} view(s)).");
        }

        var operations = EnumerateOperations(document).ToArray();
        if (operations.Length == 0)
        {
            throw new Exception(
                $"No operations were emitted for {viewCount} generated view(s); the security posture " +
                "cannot be exercised.");
        }

        var offenders = new List<string>();
        foreach (var (path, method, operation) in operations)
        {
            var name = OnlySchemeName(operation);
            if (!string.Equals(name, expectedScheme, StringComparison.Ordinal))
            {
                offenders.Add(
                    $"{method} {path} -> {(operation.Security is null ? "(no requirement)" : name ?? "(non-single-scheme requirement)")}");
            }
        }

        if (offenders.Count > 0)
        {
            throw new Exception(
                $"Every operation must carry the same one-door security requirement naming '{expectedScheme}' " +
                $"(Requirements 7.1, 7.4). Offending operation(s): {string.Join(", ", offenders)}");
        }
    }
}
