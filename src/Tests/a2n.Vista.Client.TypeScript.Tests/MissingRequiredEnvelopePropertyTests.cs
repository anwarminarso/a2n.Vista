// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for the model builder's fixed-envelope binding (task 7.10; Requirement 2.7; design
/// Property 15). <see cref="EnvelopeCatalog.Bind"/> must abort with a fatal
/// <see cref="GenerationError.MissingSchema"/> — naming the offending envelope — the moment a required
/// envelope is absent from the resolved document, and must succeed when every required envelope is present.
/// </summary>
/// <remarks>
/// <para>
/// Three properties are asserted, pinning the full behaviour of the required/optional split:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Missing required envelope aborts, naming it (Requirement 2.7).</b> For a document that is missing
///     a non-empty subset of the required envelopes, <see cref="EnvelopeCatalog.Bind"/> returns
///     <see cref="Result{T, E}.Err"/> carrying <see cref="GenerationError.MissingSchema"/> whose
///     <see cref="GenerationError.MissingSchema.SchemaName"/> is the deterministic <em>first</em> missing
///     required envelope (in <see cref="EnvelopeCatalog.RequiredEnvelopeNames"/> order), and that name is
///     genuinely absent from the document's schema graph.
///   </item>
///   <item>
///     <b>All present binds (complement).</b> When every required envelope is present, binding returns
///     <see cref="Result{T, E}.Ok"/> regardless of the write-envelope flag.
///   </item>
///   <item>
///     <b>Read/write required-ness split.</b> Removing only a write-surface envelope while
///     <c>includeWriteEnvelopes</c> is <c>false</c> does <em>not</em> abort — the write envelopes are not
///     required when the write surface is off.
///   </item>
/// </list>
/// <para>
/// Documents are built directly through the model records, starting from a complete set of minimal object
/// schemas keyed by the required envelope names, then removing a generated subset.
/// </para>
/// </remarks>
public sealed class MissingRequiredEnvelopePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    // ---- Model construction helpers ----

    /// <summary>A minimal, structurally-empty object schema — enough for a by-name binding lookup.</summary>
    private static OpenApiSchema MinimalObject() =>
        new(null, "object", null, false, Array.Empty<string>(), null, null, null, null, false);

    private static ResolvedDocument ResolvedFrom(IReadOnlyDictionary<string, OpenApiSchema> schemas)
    {
        var document = new OpenApiDocument(
            "3.0.4",
            new OpenApiInfo("a2n.Vista API", "1.0.0"),
            new Dictionary<string, OpenApiPathItem>(),
            new OpenApiComponents(schemas, new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal)),
            Array.Empty<OpenApiSecurityRequirement>());

        return new ResolvedDocument(
            document,
            schemas,
            new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal));
    }

    /// <summary>Builds a schema graph holding a minimal object for each of <paramref name="names"/>.</summary>
    private static Dictionary<string, OpenApiSchema> SchemasFor(IEnumerable<string> names)
    {
        var map = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            map[name] = MinimalObject();
        }

        return map;
    }

    // ---- Generators ----

    /// <summary>
    /// A case that removes a non-empty subset of the required envelopes for the chosen write-surface flag.
    /// Carries the resolved document, the flag, and the deterministic first-missing name Bind must report.
    /// </summary>
    private static readonly Gen<(ResolvedDocument Document, bool IncludeWrite, string ExpectedMissing)> MissingRequiredCase =
        from includeWrite in Gen.Bool
        let required = EnvelopeCatalog.RequiredEnvelopeNames(includeWrite)
        from removeFlags in Gen.Bool.Array[required.Count].Where(flags => flags.Any(flag => flag))
        select BuildMissingCase(includeWrite, required, removeFlags);

    private static (ResolvedDocument, bool, string) BuildMissingCase(
        bool includeWrite,
        IReadOnlyList<string> required,
        bool[] removeFlags)
    {
        var removed = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < required.Count; i++)
        {
            if (removeFlags[i])
            {
                removed.Add(required[i]);
            }
        }

        // Present schemas = every required envelope except the removed subset. The document therefore has a
        // genuine gap for each removed required envelope.
        var present = required.Where(name => !removed.Contains(name));
        var schemas = SchemasFor(present);

        // Bind reports the FIRST missing in RequiredEnvelopeNames order, so the expected name is the first
        // required name that was removed — computed identically to the production walk.
        string expectedMissing = required.First(name => removed.Contains(name));

        return (ResolvedFrom(schemas), includeWrite, expectedMissing);
    }

    /// <summary>A case with every required envelope present, for either write-surface flag.</summary>
    private static readonly Gen<(ResolvedDocument Document, bool IncludeWrite)> AllPresentCase =
        from includeWrite in Gen.Bool
        // Always populate the full eight-envelope catalog so the read-only flag path is a strict superset.
        let allNames = EnvelopeCatalog.RequiredEnvelopeNames(includeWriteEnvelopes: true)
        select (ResolvedFrom(SchemasFor(allNames)), includeWrite);

    /// <summary>
    /// A case that removes a non-empty subset of the write-surface envelopes only, with the read surface
    /// fully present and the write-surface flag OFF. Binding must still succeed.
    /// </summary>
    private static readonly Gen<ResolvedDocument> WriteOnlyRemovedReadOnlyFlagCase =
        from removeRequest in Gen.Bool
        from removeResponse in Gen.Bool
        where removeRequest || removeResponse
        select BuildWriteOnlyRemovedCase(removeRequest, removeResponse);

    private static ResolvedDocument BuildWriteOnlyRemovedCase(bool removeRequest, bool removeResponse)
    {
        // Every read-surface envelope present.
        var names = new List<string>(EnvelopeCatalog.ReadSurfaceEnvelopeNames);

        // Keep the write envelopes only when not marked for removal.
        if (!removeRequest)
        {
            names.Add(EnvelopeCatalog.VistaWriteRequestBody);
        }

        if (!removeResponse)
        {
            names.Add(EnvelopeCatalog.VistaWriteResponse);
        }

        return ResolvedFrom(SchemasFor(names));
    }

    // Feature: typescript-client, Property 15: Missing required envelope aborts, naming it
    //
    // For a document missing at least one required envelope, Bind aborts with
    // GenerationError.MissingSchema naming the deterministic first-missing required envelope, and that
    // named schema is genuinely absent from the document.
    //
    // Validates: Requirements 2.7
    [Test]
    public void Missing_Required_Envelope_Aborts_With_MissingSchema_Naming_The_First_Missing()
    {
        var catalog = new EnvelopeCatalog();

        MissingRequiredCase.Sample(
            testCase =>
            {
                (ResolvedDocument document, bool includeWrite, string expectedMissing) = testCase;

                Result<EnvelopeBindings, GenerationError> result = catalog.Bind(document, includeWrite);

                if (result.IsOk)
                {
                    throw new Exception(
                        "A document missing a required envelope must abort, but binding succeeded " +
                        $"(includeWriteEnvelopes: {includeWrite}, expected missing: '{expectedMissing}').");
                }

                if (result.Error is not GenerationError.MissingSchema missing)
                {
                    throw new Exception(
                        $"Expected GenerationError.MissingSchema for '{expectedMissing}', but got " +
                        $"'{result.Error.GetType().Name}' ({result.Error.Message}).");
                }

                if (!string.Equals(missing.SchemaName, expectedMissing, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Bind named missing envelope '{missing.SchemaName}', expected the deterministic " +
                        $"first-missing '{expectedMissing}' (includeWriteEnvelopes: {includeWrite}).");
                }

                // The named schema must be genuinely absent — the report is not spurious.
                if (document.Schemas.ContainsKey(missing.SchemaName))
                {
                    throw new Exception(
                        $"Bind named '{missing.SchemaName}' as missing, yet it is present in the document " +
                        "schema graph.");
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 15: Missing required envelope aborts, naming it
    //
    // Complement: when every required envelope is present, Bind returns Ok for either write-surface flag.
    //
    // Validates: Requirements 2.7
    [Test]
    public void All_Required_Envelopes_Present_Binds_Ok()
    {
        var catalog = new EnvelopeCatalog();

        AllPresentCase.Sample(
            testCase =>
            {
                (ResolvedDocument document, bool includeWrite) = testCase;

                Result<EnvelopeBindings, GenerationError> result = catalog.Bind(document, includeWrite);

                if (result.IsError)
                {
                    throw new Exception(
                        "A document with every required envelope present must bind to Ok, but binding " +
                        $"failed (includeWriteEnvelopes: {includeWrite}): {result.Error.Message}.");
                }

                // Every required name for the chosen surface must be bound exactly by name.
                foreach (var name in EnvelopeCatalog.RequiredEnvelopeNames(includeWrite))
                {
                    if (!result.Value.Contains(name))
                    {
                        throw new Exception(
                            $"Binding succeeded but required envelope '{name}' was not bound " +
                            $"(includeWriteEnvelopes: {includeWrite}).");
                    }
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 15: Missing required envelope aborts, naming it
    //
    // Read/write required-ness split: removing only a write-surface envelope while includeWriteEnvelopes is
    // false does NOT abort — write envelopes are not required when the write surface is off.
    //
    // Validates: Requirements 2.7
    [Test]
    public void Missing_Write_Envelope_Does_Not_Abort_When_Write_Surface_Is_Off()
    {
        var catalog = new EnvelopeCatalog();

        WriteOnlyRemovedReadOnlyFlagCase.Sample(
            document =>
            {
                Result<EnvelopeBindings, GenerationError> result = catalog.Bind(document, includeWriteEnvelopes: false);

                if (result.IsError)
                {
                    throw new Exception(
                        "Removing a write-surface envelope while the write surface is off must not abort, " +
                        $"but binding failed: {result.Error.Message}.");
                }
            },
            iter: Iterations);
    }
}
