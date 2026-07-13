// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Style A coverage generator's (StyleAShapeGenerator, M9, D129/D130,
// style-a-coverage) VISTA0060–VISTA0063 diagnostics (task 3.3).
//
// Feature: style-a-coverage, Property 8: Diagnostic conformance and coverage set
//
// Validates: Requirements 8.1, 8.5
//
// Property 8 (design.md "Correctness Properties") bundles two conjuncts about the Style A generator's
// diagnostics:
//
//   (i)  COVERAGE SET (R8.1): for any covered Style A view, the VISTA0060 diagnostic names EXACTLY the
//        artifacts generated for that view — "export accessors" iff TRow is named; "read-DTO JsonTypeInfo"
//        iff named + emittable read DTOs; "TCrud JsonTypeInfo" iff writable + emittable TCrud.
//   (ii) CONFORMANCE (R8.5): for any view, EVERY diagnostic the generator emits has an id matching the
//        `VISTA` + four-digit format, category `a2n.Vista.SourceGenerators`, and a severity that is never
//        Error (non-blocking — an uncovered Style A view is a valid, working view served by the reflection
//        fallback; only the AOT-clean auto-generation is missed).
//
// The property quantifies over the FINITE Style A recognition/coverage matrix — read TRow {named, anonymous,
// object} × write facet {read-only, writable} × view name {constant, non-constant} — with randomly varied
// namespace/template/view/row/crud identifiers, driving the generator via CSharpGeneratorDriver
// (StyleAShapeGeneratorTestHarness) and asserting on the emitted diagnostics. Each conjunct is a separate
// [Test] carrying the same Property-8 tag; each is a CsCheck imperative Sample with iter: 100 (the project's
// PBT convention — CsCheck pairs cleanly with TUnit [Test]). Conjunct (ii) is checked BOTH at runtime (every
// emitted diagnostic conforms, over the whole matrix) AND statically (the shipped VISTA0060–VISTA0063
// descriptor set read back from the internal DiagnosticDescriptors holder by reflection — matching the
// no-InternalsVisibleTo convention of the sibling Vista0050/HttpSurface conformance tests).
//
// ROBUSTNESS TO THE UNLANDED SHAPE ANALYSIS (task 2.4): the Emittable_Shape analysis has not landed, so
// ReadDtosEmittable/CrudDtoEmittable are the placeholder `false` and the read-DTO / TCrud JsonTypeInfo
// artifacts are gated off — right now a covered named-TRow view emits VISTA0060 with the artifact set
// "export accessors" ONLY. This test therefore asserts the coverage-set INVARIANTS that hold regardless of
// task 2.4 rather than a hardcoded full set:
//   * "export accessors" is in the set IFF TRow is named (accessors need only member access, never
//     emittability) — an exact biconditional that holds now AND after 2.4;
//   * "read-DTO JsonTypeInfo" present ⟹ TRow is named; "TCrud JsonTypeInfo" present ⟹ the view is writable
//     — one-directional implications that are vacuously satisfied now and meaningfully after 2.4, so their
//     PRESENCE is never asserted (which would falsely fail today) nor their ABSENCE (which would falsely
//     fail after 2.4);
//   * a named-TRow constant-named view is always covered (VISTA0060 present); a constant-named anonymous
//     read-only view is never covered (empty artifact set → no VISTA0060) — both hold across 2.4.
// All generated DTO members are emittable shapes (int / string / int? / enum), so VISTA0063 never fires in
// either state, keeping the matrix stable across 2.4.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CsCheck;
using Microsoft.CodeAnalysis;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class Vista0060CoverageAndDiagnosticConformancePropertyTests
{
    // ---- shared diagnostic-contract constants (R8.5) --------------------------------------------------

    // R8.5 / Spec 03 D81: the VISTA prefix immediately followed by exactly four decimal digits.
    private static readonly Regex DiagnosticIdPattern = new("^VISTA[0-9]{4}$", RegexOptions.Compiled);

    // R8.5: the shared diagnostic category all Vista generator diagnostics carry.
    private const string ExpectedCategory = "a2n.Vista.SourceGenerators";

    // R8.5: the help link must point at the per-diagnostic docs under docs/diagnostics/.
    private const string HelpLinkSegment = "docs/diagnostics/";

    // The three artifact names VISTA0060 can list (design "Coverage classification"). Any other token in the
    // set indicates a message-composition bug.
    private static readonly HashSet<string> KnownArtifacts = new(StringComparer.Ordinal)
    {
        "export accessors",
        "read-DTO JsonTypeInfo",
        "TCrud JsonTypeInfo",
    };

    // The fixed message marker after which VISTA0060's {1} artifact list begins (see
    // DiagnosticDescriptors.StyleAViewCovered): "...is covered by generated artifacts: {1}".
    private const string Vista0060ArtifactMarker = "is covered by generated artifacts: ";

    // ---- the finite Style A recognition/coverage matrix -----------------------------------------------

    /// <summary>The read row shape of a Style A view.</summary>
    private enum RowKind
    {
        /// <summary>A named DTO/record row — nameable in generated source (read-side coverable).</summary>
        Named,

        /// <summary>An anonymous projection row — unnameable, RUC by design (VISTA0061).</summary>
        Anonymous,

        /// <summary><c>object</c> — treated like anonymous (unnameable, VISTA0061).</summary>
        Object,
    }

    /// <summary>The write facet of a Style A view.</summary>
    private enum CrudKind
    {
        /// <summary>Read-only — no <c>WithCrud</c>.</summary>
        None,

        /// <summary>Writable via <c>.WithCrud&lt;TCrud, TEntity&gt;()</c> with a named <c>TCrud</c> (D38).</summary>
        Named,
    }

    /// <summary>How the <c>AddView</c> name argument is expressed.</summary>
    private enum NameKind
    {
        /// <summary>A compile-time constant string literal — keyable, so artifacts can be emitted.</summary>
        Constant,

        /// <summary>A non-constant expression (a method call) — not keyable (VISTA0062, hard gate).</summary>
        NonConstant,
    }

    /// <summary>One generated Style A call site: its matrix coordinates and the varied identifiers.</summary>
    private sealed record StyleAViewInput(
        RowKind Row,
        CrudKind Crud,
        NameKind Name,
        string Namespace,
        string TemplateName,
        string ViewName,
        string RowName,
        string CrudName);

    // A valid C# identifier core: an uppercase leading letter followed by 2–6 lowercase letters, so it is
    // never a C# keyword (keywords are all lowercase) and always parses.
    private static readonly Gen<string> GenIdentifierCore =
        from first in Gen.Char['a', 'z']
        from rest in Gen.Char['a', 'z'].Array[2, 6]
        select char.ToUpperInvariant(first) + new string(rest);

    // Distinct prefixes ("Ns"/"Tmpl"/"Row"/"Crud"/"view_") guarantee the names never collide even when their
    // random cores coincide, so every rendered source compiles with distinct names while still varying each.
    private static readonly Gen<StyleAViewInput> GenStyleAViewInput =
        from row in Gen.Int[0, 2]
        from crud in Gen.Int[0, 1]
        from name in Gen.Int[0, 1]
        from ns in GenIdentifierCore
        from tmpl in GenIdentifierCore
        from view in GenIdentifierCore
        from rowName in GenIdentifierCore
        from crudName in GenIdentifierCore
        select new StyleAViewInput(
            (RowKind)row,
            (CrudKind)crud,
            (NameKind)name,
            "Ns" + ns,
            "Tmpl" + tmpl,
            "view_" + view.ToLowerInvariant(),
            "Row" + rowName,
            "Crud" + crudName);

    // ---- conjunct (i): VISTA0060 names exactly the generated artifacts (R8.1) -------------------------

    [Test]
    public void VISTA0060_Coverage_Set_Matches_The_Generated_Artifacts()
    {
        // Feature: style-a-coverage, Property 8: Diagnostic conformance and coverage set
        GenStyleAViewInput.Sample(
            input =>
            {
                var named = input.Row == RowKind.Named;
                var writable = input.Crud == CrudKind.Named;
                var constant = input.Name == NameKind.Constant;

                var result = StyleAShapeGeneratorTestHarness.Run(RenderStyleAViewSource(input));

                // R8.5 (runtime): every emitted diagnostic conforms and none is an Error.
                if (!AllEmittedDiagnosticsConform(result))
                {
                    return false;
                }

                var covered = result.Diagnostics.Where(static d => d.Id == "VISTA0060").ToArray();

                // A non-constant name is a HARD GATE (VISTA0062, checked in the boundary conjunct): no
                // artifact can be keyed statically, so VISTA0060 is never emitted for it.
                if (!constant)
                {
                    return covered.Length == 0;
                }

                // A named TRow is always covered (accessors need only member access), so exactly one
                // VISTA0060 is emitted; a constant-named anonymous/object read-only view has an empty
                // artifact set, so none is. (Both hold regardless of the task-2.4 emittability analysis.)
                if (named && covered.Length != 1)
                {
                    return false;
                }

                if (!named && !writable && covered.Length != 0)
                {
                    return false;
                }

                // At most one VISTA0060 per call site.
                if (covered.Length > 1)
                {
                    return false;
                }

                if (covered.Length == 1)
                {
                    var diagnostic = covered[0];

                    // R8.1/R8.5: VISTA0060 is Info and names the covered view (the {0} placeholder).
                    if (diagnostic.Severity != DiagnosticSeverity.Info)
                    {
                        return false;
                    }

                    var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
                    if (!message.Contains("'" + input.ViewName + "'", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var artifacts = ParseArtifactSet(message);

                    // The set is non-empty and lists only recognized artifact names.
                    if (artifacts.Count == 0 || artifacts.Any(a => !KnownArtifacts.Contains(a)))
                    {
                        return false;
                    }

                    // Exact biconditional (task-2.4-independent): "export accessors" is in the set IFF the
                    // read TRow is named — accessors are compile-time member access, never gated on DTO
                    // emittability.
                    if (artifacts.Contains("export accessors") != named)
                    {
                        return false;
                    }

                    // One-directional coverage implications (robust to task 2.4): a read-DTO context requires
                    // a named row; a TCrud context requires a writable view. Presence is never asserted (it
                    // would falsely fail before 2.4); only that a listed artifact is consistent with the
                    // view's shape.
                    if (artifacts.Contains("read-DTO JsonTypeInfo") && !named)
                    {
                        return false;
                    }

                    if (artifacts.Contains("TCrud JsonTypeInfo") && !writable)
                    {
                        return false;
                    }
                }

                return true;
            },
            iter: 100,
            print: RenderStyleAViewSource);
    }

    // ---- conjunct (i, boundary): VISTA0061 / VISTA0062 fire exactly when expected (R8.1) --------------

    [Test]
    public void Boundary_Diagnostics_VISTA0061_And_VISTA0062_Fire_Exactly_When_Expected()
    {
        // Feature: style-a-coverage, Property 8: Diagnostic conformance and coverage set
        GenStyleAViewInput.Sample(
            input =>
            {
                var named = input.Row == RowKind.Named;
                var constant = input.Name == NameKind.Constant;

                var result = StyleAShapeGeneratorTestHarness.Run(RenderStyleAViewSource(input));

                // R8.5 (runtime): every emitted diagnostic conforms and none is an Error.
                if (!AllEmittedDiagnosticsConform(result))
                {
                    return false;
                }

                // VISTA0063 (non-emittable member) never fires: every generated DTO member is an emittable
                // shape, in either the pre- or post-task-2.4 state.
                if (result.Diagnostics.Any(static d => d.Id == "VISTA0063"))
                {
                    return false;
                }

                var has0061 = result.Diagnostics.Any(static d => d.Id == "VISTA0061");
                var has0062 = result.Diagnostics.Any(static d => d.Id == "VISTA0062");

                if (!constant)
                {
                    // Hard gate: a non-constant name emits EXACTLY ONE diagnostic — VISTA0062 (Info) — and
                    // nothing else (no VISTA0060/0061/0063), because the generator returns after reporting it.
                    if (result.Diagnostics.Length != 1)
                    {
                        return false;
                    }

                    var only = result.Diagnostics[0];
                    return only.Id == "VISTA0062" && only.Severity == DiagnosticSeverity.Info;
                }

                // A constant name is keyable, so VISTA0062 is never reported for it.
                if (has0062)
                {
                    return false;
                }

                // VISTA0061 fires exactly for a constant-named anonymous/object read row (its read side stays
                // RUC by design, D96) and never for a named row.
                if (has0061 != !named)
                {
                    return false;
                }

                if (has0061)
                {
                    var diagnostic = result.Diagnostics.First(static d => d.Id == "VISTA0061");

                    // R8.2/R8.5: VISTA0061 is Info and names the affected view.
                    if (diagnostic.Severity != DiagnosticSeverity.Info)
                    {
                        return false;
                    }

                    if (!diagnostic.GetMessage(CultureInfo.InvariantCulture)
                            .Contains("'" + input.ViewName + "'", StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            },
            iter: 100,
            print: RenderStyleAViewSource);
    }

    // ---- conjunct (ii, static): every Style A descriptor conforms (R8.5) ------------------------------

    // The Style A coverage diagnostic family this feature (D129/D130) owns. Selecting by id keeps the
    // property scoped to exactly VISTA0060–VISTA0063, independent of the other Vista families (VISTA0001
    // etc. are legitimately Error and are not Style A diagnostics).
    private static readonly HashSet<string> StyleAIds = new(StringComparer.Ordinal)
    {
        "VISTA0060",
        "VISTA0061",
        "VISTA0062",
        "VISTA0063",
    };

    private static readonly DiagnosticDescriptor[] StyleADescriptors = LoadStyleADescriptors();

    private static readonly Gen<DiagnosticDescriptor> GenStyleADescriptor =
        Gen.Int[0, StyleADescriptors.Length - 1].Select(i => StyleADescriptors[i]);

    [Test]
    public void Every_StyleA_Descriptor_Conforms_To_Id_Category_HelpLink_And_NonBlocking_Severity()
    {
        // Feature: style-a-coverage, Property 8: Diagnostic conformance and coverage set

        // Guard: the reflection lookup must actually find all four descriptors, otherwise a
        // vacuously-true property could hide a rename/removal of a VISTA0060–VISTA0063 descriptor.
        if (StyleADescriptors.Length != StyleAIds.Count)
        {
            throw new InvalidOperationException(
                $"Expected {StyleAIds.Count} Style A descriptors ({string.Join(", ", StyleAIds)}), " +
                $"found {StyleADescriptors.Length}: [{string.Join(", ", StyleADescriptors.Select(d => d.Id))}].");
        }

        GenStyleADescriptor.Sample(
            descriptor =>
            {
                // R8.5: VISTA#### id format.
                if (!DiagnosticIdPattern.IsMatch(descriptor.Id))
                {
                    return false;
                }

                // R8.5: shared category.
                if (!string.Equals(descriptor.Category, ExpectedCategory, StringComparison.Ordinal))
                {
                    return false;
                }

                // R8.5: a help link under docs/diagnostics/.
                if (string.IsNullOrEmpty(descriptor.HelpLinkUri) ||
                    descriptor.HelpLinkUri.IndexOf(HelpLinkSegment, StringComparison.Ordinal) < 0)
                {
                    return false;
                }

                // R8.5: non-blocking severity — Info or Warning, never Error.
                return descriptor.DefaultSeverity is DiagnosticSeverity.Info or DiagnosticSeverity.Warning;
            },
            iter: 100,
            print: d =>
                $"{d.Id} (category='{d.Category}', severity={d.DefaultSeverity}, helpLink='{d.HelpLinkUri}')");
    }

    // ---- shared helpers -------------------------------------------------------------------------------

    /// <summary>
    /// R8.5 (runtime conformance): every diagnostic the generator emitted has a <c>VISTA####</c> id, the
    /// shared category, and a non-Error severity. Also requires at least one diagnostic — every shape in the
    /// matrix is recognized and reports at least one Style A diagnostic, so an empty set would signal a
    /// recognition regression rather than a vacuously-true conformance check.
    /// </summary>
    private static bool AllEmittedDiagnosticsConform(GeneratorDriverRunResult result)
    {
        if (result.Diagnostics.Length == 0)
        {
            return false;
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            if (!DiagnosticIdPattern.IsMatch(diagnostic.Id))
            {
                return false;
            }

            if (!string.Equals(diagnostic.Descriptor.Category, ExpectedCategory, StringComparison.Ordinal))
            {
                return false;
            }

            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the comma-separated artifact list that follows <see cref="Vista0060ArtifactMarker"/> in a
    /// VISTA0060 message. Splitting on ", " is safe: no artifact name contains a comma.
    /// </summary>
    private static IReadOnlyList<string> ParseArtifactSet(string message)
    {
        var markerIndex = message.IndexOf(Vista0060ArtifactMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return Array.Empty<string>();
        }

        var list = message[(markerIndex + Vista0060ArtifactMarker.Length)..];
        return list.Split(new[] { ", " }, StringSplitOptions.None);
    }

    /// <summary>
    /// Reads the Style A <see cref="DiagnosticDescriptor"/>s ({ VISTA0060–VISTA0063 }) back from the
    /// generator assembly's internal <c>DiagnosticDescriptors</c> holder by reflection, so the (internal)
    /// holder need not be visible to the test assembly (matching the no-InternalsVisibleTo convention).
    /// </summary>
    private static DiagnosticDescriptor[] LoadStyleADescriptors()
    {
        var generatorAssembly = typeof(StyleAShapeGenerator).Assembly;
        var holder = generatorAssembly.GetType(
            "a2n.Vista.SourceGenerators.DiagnosticDescriptors", throwOnError: true)!;

        return holder
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .Where(d => StyleAIds.Contains(d.Id))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();
    }

    // ---- Style A source rendering ---------------------------------------------------------------------

    /// <summary>
    /// Renders a compilable Style A template with a single <c>AddView</c> call site of the requested matrix
    /// shape. The read row is a named DTO (only emittable members), an anonymous projection, or <c>object</c>;
    /// the view is read-only or writable via <c>.WithCrud&lt;TCrud, TEntity&gt;()</c> with a named <c>TCrud</c>
    /// (only emittable members); the name is a constant string literal or a non-constant method call. All DTO
    /// members are emittable shapes (int / string / int? / enum) so VISTA0063 never applies.
    /// </summary>
    private static string RenderStyleAViewSource(StyleAViewInput input)
    {
        var rowDecl = input.Row == RowKind.Named
            ? $@"
    public enum {input.RowName}Kind {{ Alpha, Beta }}

    public sealed class {input.RowName}
    {{
        public int Id {{ get; set; }}
        public string Label {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}
        public {input.RowName}Kind Kind {{ get; set; }}
    }}
"
            : string.Empty;

        var crudDecl = input.Crud == CrudKind.Named
            ? $@"
    public sealed class {input.CrudName}
    {{
        public string Label {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}
    }}

    public sealed class {input.CrudName}Entity
    {{
        public int Id {{ get; set; }}
        public string Label {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}
    }}
"
            : string.Empty;

        // The projection producing the requested row kind (System.Linq.AsQueryable gives the IQueryable<TRow>).
        var projection = input.Row switch
        {
            RowKind.Named => $"new {input.RowName}[0].AsQueryable()",
            RowKind.Anonymous => "new[] { new { Id = 1, Label = \"x\" } }.AsQueryable()",
            _ => "new object[0].AsQueryable()",
        };

        // Explicit type argument for a named/object row; anonymous must be inferred from the projection.
        var typeArg = input.Row switch
        {
            RowKind.Named => $"<{input.RowName}>",
            RowKind.Object => "<object>",
            _ => string.Empty,
        };

        var withCrud = input.Crud == CrudKind.Named
            ? $".WithCrud<{input.CrudName}, {input.CrudName}Entity>()"
            : string.Empty;

        // A constant string literal (keyable → VISTA0060/0061) or a non-constant method call (→ VISTA0062).
        var nameArg = input.Name == NameKind.Constant ? $"\"{input.ViewName}\"" : "ViewName()";
        var nameHelper = input.Name == NameKind.NonConstant
            ? "        private static string ViewName() => \"runtime\";\n\n"
            : string.Empty;

        return $@"
using System.Linq;

namespace {input.Namespace}
{{{rowDecl}{crudDecl}
    public class {input.TemplateName}
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {{
{nameHelper}        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {{
            views.AddView{typeArg}({nameArg}, (db, sp) => {projection}){withCrud};
        }}
    }}
}}
";
    }
}
