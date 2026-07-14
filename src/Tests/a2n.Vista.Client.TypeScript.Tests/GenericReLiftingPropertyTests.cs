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
/// Property-based test for the envelope generic re-lifting step (task 7.8; Requirements 2.6, 2.5; design
/// Property 5). M18 monomorphizes the row-parameterized list envelope as one named component per distinct
/// row — <c>ViewListResult_{Row}</c> — whose <c>page</c> object inlines the fixed <c>PagedResult</c> shape
/// with the row <c>$ref</c> bound. Requirement 2.6 nonetheless requires a <b>single generic</b>
/// <c>ViewListResult&lt;TRow&gt;</c> / <c>PagedResult&lt;TRow&gt;</c> TypeScript type: the re-lifter must
/// recognize every monomorphized component, bind each to its correct row type, and signal that the single
/// generic pair is needed — without treating any per-row monomorphization as a distinct generic.
/// </summary>
/// <remarks>
/// <para>
/// Two properties are asserted, both driving <see cref="EnvelopeReLifter.ReLift"/> against a
/// <see cref="ResolvedDocument"/> built directly through the model records (mirroring
/// <see cref="RefResolutionSoundnessPropertyTests"/>):
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Re-lifting to a single generic (Requirement 2.6/2.5).</b> For N (1..5) distinct row components,
///     a document containing N well-formed <c>ViewListResult_{Row_i}</c> components (each matching the
///     <see cref="EnvelopeCatalog"/> templates exactly) re-lifts so that <c>ReLifted.Count == N</c>, each
///     component binds to its correct row type (<c>RowTypeByComponent[ViewListResult_{Row_i}] == Row_i</c>),
///     <c>GenericEnvelopesNeeded</c> is <c>true</c>, and none fall back / raise a notice. The "single
///     generic" invariant is asserted structurally: exactly one conceptual generic pair covers all N
///     bindings — there is no per-row generic duplication (the result records only per-component row
///     bindings plus the single <c>GenericEnvelopesNeeded</c> flag, never N distinct generic declarations).
///   </item>
///   <item>
///     <b>Malformed shape degrades, never fatal (Requirement 2.6 robustness).</b> A companion
///     <c>ViewListResult_Broken</c> whose shape does NOT match the template appears in
///     <c>FallbackComponents</c> and records exactly one non-fatal <see cref="GenerationNotice"/>; it is
///     never re-lifted and never bound.
///   </item>
/// </list>
/// </remarks>
public sealed class GenericReLiftingPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    // ---- Model construction helpers (build OpenApiSchema/OpenApiDocument instances directly) ----

    private static OpenApiSchema Ref(string name) =>
        new(ResolvedDocument.SchemaRefPrefix + name, null, null, false, Array.Empty<string>(), null, null, null, null, false);

    /// <summary>A scalar schema pinned to an OpenAPI <c>type</c> and (optional) <c>format</c>.</summary>
    private static OpenApiSchema Scalar(string type, string? format = null) =>
        new(null, type, format, false, Array.Empty<string>(), null, null, null, null, false);

    /// <summary>An inline array schema whose element is <paramref name="items"/>.</summary>
    private static OpenApiSchema Arr(OpenApiSchema items) =>
        new(null, "array", null, false, Array.Empty<string>(), null, items, null, null, false);

    /// <summary>An inline object schema over the supplied properties, marking every member required.</summary>
    private static OpenApiSchema Obj(Dictionary<string, OpenApiSchema> properties) =>
        new(null, "object", null, false, properties.Keys.ToArray(), properties, null, null, null, false);

    /// <summary>A minimal row schema: an object with a couple of scalar properties.</summary>
    private static OpenApiSchema RowSchema() =>
        Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["id"] = Scalar("integer", "int32"),
            ["name"] = Scalar("string"),
        });

    /// <summary>
    /// The inlined <c>page</c> object matching the fixed <c>PagedResult</c> template exactly: an
    /// <c>items</c> array of the row <c>$ref</c>, plus the four fixed integer scalars with their pinned
    /// formats (design/EnvelopeCatalog.PagedResultTemplate).
    /// </summary>
    private static OpenApiSchema PageMatching(string rowName) =>
        Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["items"] = Arr(Ref(rowName)),
            ["totalRows"] = Scalar("integer", "int64"),
            ["pageIndex"] = Scalar("integer", "int32"),
            ["pageSize"] = Scalar("integer", "int32"),
            ["totalPages"] = Scalar("integer", "int64"),
        });

    /// <summary>
    /// A well-formed monomorphized <c>ViewListResult_{Row}</c> component matching the fixed
    /// <c>ViewListResult</c> template exactly: a <c>page</c> nested object (the <see cref="PageMatching"/>
    /// shape) plus the fixed <c>totalRowsUnfiltered</c> int64 scalar
    /// (design/EnvelopeCatalog.ViewListResultTemplate).
    /// </summary>
    private static OpenApiSchema ViewListResultMatching(string rowName) =>
        Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["page"] = PageMatching(rowName),
            ["totalRowsUnfiltered"] = Scalar("integer", "int64"),
        });

    private static string EnvelopeName(string rowName) => EnvelopeReLifter.MonomorphizedPrefix + rowName;

    private static ResolvedDocument Resolve(IReadOnlyDictionary<string, OpenApiSchema> schemas) =>
        new(
            new OpenApiDocument(
                "3.0.4",
                new OpenApiInfo("a2n.Vista API", "1.0.0"),
                new Dictionary<string, OpenApiPathItem>(),
                new OpenApiComponents(schemas, new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal)),
                Array.Empty<OpenApiSecurityRequirement>()),
            schemas,
            new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal));

    // ---- Generators ----

    /// <summary>A PascalCase-ish identifier of 1–6 letters used to build a "{Name}Row" row component name.</summary>
    private static readonly Gen<string> RowStem =
        Gen.Char['a', 'z'].Array[1, 6].Select(chars => new string(chars));

    /// <summary>N (1..5) distinct row component names of the form <c>{Stem}Row</c>.</summary>
    private static readonly Gen<string[]> DistinctRowNames =
        RowStem.Array[1, 5]
            .Select(stems => stems.Distinct().Select(s => s + "Row").ToArray())
            .Where(rows => rows.Length >= 1);

    // Feature: typescript-client, Property 5: Row-parameterized envelopes are re-lifted to a single generic type
    //
    // For N distinct row components, a document containing N well-formed monomorphized
    // ViewListResult_{Row_i} components (each matching the ViewListResult/PagedResult templates) is
    // re-lifted to a SINGLE generic ViewListResult<TRow>/PagedResult<TRow>: ReLift recognizes all N
    // (ReLifted.Count == N), each ViewListResult_{Row_i} binds to Row_i, GenericEnvelopesNeeded is true,
    // and NO per-row monomorphization becomes a distinct generic (exactly one conceptual generic pair
    // covers all N — asserted via GenericEnvelopesNeeded plus the per-component bindings, no duplication).
    //
    // Validates: Requirements 2.6, 2.5
    [Test]
    public void N_Monomorphized_Envelopes_ReLift_To_A_Single_Generic_Bound_Per_Row()
    {
        DistinctRowNames.Sample(
            rowNames =>
            {
                // Build a document: each row contributes its row schema AND a matching monomorphized envelope.
                var schemas = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
                foreach (var rowName in rowNames)
                {
                    schemas[rowName] = RowSchema();
                    schemas[EnvelopeName(rowName)] = ViewListResultMatching(rowName);
                }

                var notices = new NoticeCollector();
                var reLifter = new EnvelopeReLifter(new EnvelopeCatalog());

                EnvelopeReLiftResult result = reLifter.ReLift(Resolve(schemas), notices);

                // All N monomorphized components are recognized — one binding each, no more.
                if (result.ReLifted.Count != rowNames.Length)
                {
                    throw new Exception(
                        $"Expected all {rowNames.Length} monomorphized components to be re-lifted, but " +
                        $"ReLifted.Count == {result.ReLifted.Count}.");
                }

                // The single generic pair is signalled as needed exactly once (not per row).
                if (!result.GenericEnvelopesNeeded)
                {
                    throw new Exception(
                        "GenericEnvelopesNeeded must be true when at least one monomorphized component is " +
                        "recognized; the single ViewListResult<TRow>/PagedResult<TRow> pair is required.");
                }

                // Each component binds to its correct row type — and there is no per-row generic
                // duplication: the result records exactly N component→row bindings, never N generic
                // declarations. One conceptual generic pair (GenericEnvelopesNeeded) covers all bindings.
                if (result.RowTypeByComponent.Count != rowNames.Length)
                {
                    throw new Exception(
                        $"Expected exactly {rowNames.Length} component→row bindings, but " +
                        $"RowTypeByComponent.Count == {result.RowTypeByComponent.Count}.");
                }

                foreach (var rowName in rowNames)
                {
                    var componentName = EnvelopeName(rowName);

                    if (!result.TryGetRowType(componentName, out var boundRow))
                    {
                        throw new Exception(
                            $"Component '{componentName}' was not recognized as a re-lifted envelope.");
                    }

                    if (!string.Equals(boundRow, rowName, StringComparison.Ordinal))
                    {
                        throw new Exception(
                            $"Component '{componentName}' bound to row '{boundRow}', expected '{rowName}'.");
                    }
                }

                // Every re-lifted entry references a distinct component and its bound row — confirming the
                // N monomorphizations collapse into per-view bindings on a single generic, not N generics.
                var distinctComponents = result.ReLifted.Select(e => e.ComponentName).Distinct(StringComparer.Ordinal).Count();
                if (distinctComponents != rowNames.Length)
                {
                    throw new Exception(
                        $"Expected {rowNames.Length} distinct re-lifted component names, found {distinctComponents}.");
                }

                // Well-formed components must not fall back and must not raise any notice.
                if (result.FallbackComponents.Count != 0)
                {
                    throw new Exception(
                        $"Well-formed monomorphized components must not fall back, but " +
                        $"FallbackComponents.Count == {result.FallbackComponents.Count}.");
                }

                if (notices.Count != 0)
                {
                    throw new Exception(
                        $"Well-formed monomorphized components must not record notices, but " +
                        $"{notices.Count} notice(s) were recorded.");
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 5: Row-parameterized envelopes are re-lifted to a single generic type
    //
    // Companion (robustness): alongside N well-formed monomorphized envelopes, a ViewListResult_Broken
    // whose shape does NOT match the template appears in FallbackComponents with a non-fatal notice, is
    // never re-lifted, and never bound — while the N well-formed components still re-lift correctly.
    //
    // Validates: Requirements 2.6, 2.5
    [Test]
    public void A_Malformed_Monomorphized_Envelope_Falls_Back_With_A_Notice_And_Is_Never_ReLifted()
    {
        // The broken shapes: each is a plausible-but-non-matching ViewListResult_* component. Selecting a
        // shape by index keeps the counterexample stable.
        //   0: missing totalRowsUnfiltered (outer member-set mismatch)
        //   1: page missing entirely       (outer member-set mismatch)
        //   2: page missing totalPages     (nested member-set mismatch)
        //   3: items element is inline, not a $ref (no nameable row → mismatch)
        //   4: a scalar format drift on page.totalRows (int32 where int64 expected)
        var broken =
            from rowNames in DistinctRowNames
            from brokenKind in Gen.Int[0, 4]
            select (rowNames, brokenKind);

        broken.Sample(
            testCase =>
            {
                var (rowNames, brokenKind) = testCase;

                var schemas = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
                foreach (var rowName in rowNames)
                {
                    schemas[rowName] = RowSchema();
                    schemas[EnvelopeName(rowName)] = ViewListResultMatching(rowName);
                }

                // A distinct row for the broken envelope so it never collides with a well-formed one.
                const string brokenRow = "BrokenRow";
                const string brokenName = EnvelopeReLifter.MonomorphizedPrefix + "Broken";
                schemas[brokenRow] = RowSchema();
                schemas[brokenName] = BuildBroken(brokenKind, brokenRow);

                var notices = new NoticeCollector();
                var reLifter = new EnvelopeReLifter(new EnvelopeCatalog());

                EnvelopeReLiftResult result = reLifter.ReLift(Resolve(schemas), notices);

                // The broken component must fall back, never be re-lifted, never be bound.
                if (!result.FallbackComponents.Contains(brokenName))
                {
                    throw new Exception(
                        $"Malformed component '{brokenName}' (kind {brokenKind}) must appear in " +
                        "FallbackComponents.");
                }

                if (result.TryGetRowType(brokenName, out _))
                {
                    throw new Exception(
                        $"Malformed component '{brokenName}' (kind {brokenKind}) must not be bound to a row type.");
                }

                if (result.ReLifted.Any(e => string.Equals(e.ComponentName, brokenName, StringComparison.Ordinal)))
                {
                    throw new Exception(
                        $"Malformed component '{brokenName}' (kind {brokenKind}) must not be re-lifted.");
                }

                // The fallback is non-fatal: recorded as a notice (never an exception / abort).
                if (notices.Count != 1)
                {
                    throw new Exception(
                        $"Expected exactly one non-fatal fallback notice for the malformed component, but " +
                        $"{notices.Count} notice(s) were recorded (kind {brokenKind}).");
                }

                var notice = notices.ToSortedList()[0];
                if (notice.Kind != GenerationNoticeKind.EnvelopeShapeFallback)
                {
                    throw new Exception(
                        $"Expected an EnvelopeShapeFallback notice, got '{notice.Kind}' (kind {brokenKind}).");
                }

                // The N well-formed components still re-lift correctly alongside the broken one.
                if (result.ReLifted.Count != rowNames.Length)
                {
                    throw new Exception(
                        $"Expected the {rowNames.Length} well-formed components to still re-lift, but " +
                        $"ReLifted.Count == {result.ReLifted.Count} (kind {brokenKind}).");
                }

                foreach (var rowName in rowNames)
                {
                    var componentName = EnvelopeName(rowName);
                    if (!result.TryGetRowType(componentName, out var boundRow)
                        || !string.Equals(boundRow, rowName, StringComparison.Ordinal))
                    {
                        throw new Exception(
                            $"Well-formed component '{componentName}' failed to bind to '{rowName}' " +
                            $"(kind {brokenKind}).");
                    }
                }
            },
            iter: Iterations);
    }

    // Builds a ViewListResult_Broken component whose shape does not match the template, per the selected kind.
    private static OpenApiSchema BuildBroken(int kind, string rowName) => kind switch
    {
        // Outer member-set mismatch: missing the fixed totalRowsUnfiltered scalar.
        0 => Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["page"] = PageMatching(rowName),
        }),

        // Outer member-set mismatch: page absent, replaced by an unrelated member of the same count.
        1 => Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["notPage"] = Scalar("string"),
            ["totalRowsUnfiltered"] = Scalar("integer", "int64"),
        }),

        // Nested member-set mismatch: page is missing totalPages.
        2 => Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["page"] = Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["items"] = Arr(Ref(rowName)),
                ["totalRows"] = Scalar("integer", "int64"),
                ["pageIndex"] = Scalar("integer", "int32"),
                ["pageSize"] = Scalar("integer", "int32"),
            }),
            ["totalRowsUnfiltered"] = Scalar("integer", "int64"),
        }),

        // items element is inline (no nameable row $ref) → no row type to re-lift.
        3 => Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["page"] = Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["items"] = Arr(Scalar("string")),
                ["totalRows"] = Scalar("integer", "int64"),
                ["pageIndex"] = Scalar("integer", "int32"),
                ["pageSize"] = Scalar("integer", "int32"),
                ["totalPages"] = Scalar("integer", "int64"),
            }),
            ["totalRowsUnfiltered"] = Scalar("integer", "int64"),
        }),

        // Scalar format drift on page.totalRows (int32 where the template pins int64).
        _ => Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            ["page"] = Obj(new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["items"] = Arr(Ref(rowName)),
                ["totalRows"] = Scalar("integer", "int32"),
                ["pageIndex"] = Scalar("integer", "int32"),
                ["pageSize"] = Scalar("integer", "int32"),
                ["totalPages"] = Scalar("integer", "int64"),
            }),
            ["totalRowsUnfiltered"] = Scalar("integer", "int64"),
        }),
    };
}
