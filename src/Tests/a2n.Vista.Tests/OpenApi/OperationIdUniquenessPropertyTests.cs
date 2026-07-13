// Licensed to the a2n.Vista project. Published artifact — English only.
//
// OpenAPI emitter STRUCTURAL property test (spec openapi-emitter, task 8.3).
//
// Property 2: Operation ids are unique and no operation has a path parameter.
//   For any set of registered views, every emitted operation has a distinct operationId, and no emitted
//   operation declares a path parameter (the row key / query ride in the request body, not the path).
//
// Validates: Requirements 1.5, 2.3.
//
// Oracle: the live route table (IViewRegistry) — the structural registry generator (RegistryGenerators)
// produces arbitrary view sets, the real VistaOpenApiDocumentBuilder builds the document, and this test
// asserts the two structural invariants over every emitted operation (Get and Post across all paths).
//
// CsCheck-via-TUnit idiom: Gen<GeneratedRegistry>.Sample(action, iter: 100) at ≥100 iterations, matching
// the sibling structural suites. The builder's Build() is [RequiresUnreferencedCode] (per-view DTO schema
// generation reflects over CLR row/CRUD types, D96 asymmetry), so the driving members are RUC-annotated.

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
/// Property 2 (task 8.3): every emitted operation has a distinct <c>operationId</c> and no operation
/// declares a path parameter. Structural property over arbitrary registries (the registry is the oracle).
/// </summary>
public sealed class OperationIdUniquenessPropertyTests
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
    /// Property 2: over arbitrary registries, all emitted <c>operationId</c> values are non-null and
    /// globally distinct (R1.5), and no emitted operation declares a path parameter (<c>in: path</c>) — the
    /// key and query ride in the request body, consistent with the action-style surface (R2.3). Header
    /// parameters (for example <c>If-None-Match</c>) and query parameters are permitted; only <c>path</c>
    /// parameters are forbidden.
    /// </summary>
    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder over generated registries.")]
    public void OperationIds_Are_Unique_And_No_Operation_Has_A_Path_Parameter()
    {
        // Feature: openapi-emitter, Property 2: Operation ids are unique and no operation has a path parameter
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
                var operations = EnumerateOperations(document).ToArray();

                // --- R1.5: operationIds are non-null and DISTINCT across the whole document -------------
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var duplicates = new List<string>();
                foreach (var (path, method, operation) in operations)
                {
                    if (operation.OperationId is null)
                    {
                        throw new Exception(
                            $"Operation {method} {path} has a null operationId (Requirement 1.5).");
                    }

                    if (!seen.Add(operation.OperationId))
                    {
                        duplicates.Add($"{operation.OperationId} ({method} {path})");
                    }
                }

                if (duplicates.Count > 0)
                {
                    throw new Exception(
                        "Duplicate operationId(s) emitted (Requirement 1.5): "
                        + string.Join(", ", duplicates));
                }

                // --- R2.3: no operation declares a path parameter ---------------------------------------
                var offenders = new List<string>();
                foreach (var (path, method, operation) in operations)
                {
                    if (operation.Parameters is null)
                    {
                        continue;
                    }

                    foreach (var parameter in operation.Parameters)
                    {
                        if (string.Equals(parameter.In, "path", StringComparison.Ordinal))
                        {
                            offenders.Add(
                                $"{operation.OperationId ?? "(null)"} ({method} {path}) -> path param '{parameter.Name}'");
                        }
                    }
                }

                if (offenders.Count > 0)
                {
                    throw new Exception(
                        "Operation(s) declared a forbidden path parameter (Requirement 2.3): "
                        + string.Join(", ", offenders));
                }
            },
            iter: Iterations);
    }
}
