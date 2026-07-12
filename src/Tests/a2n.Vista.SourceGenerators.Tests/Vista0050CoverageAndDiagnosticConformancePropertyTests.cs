// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 5 (M9, D125/D126, source-generator-json-typeinfo)
// ViewJsonContextGenerator's VISTA0050/VISTA0051 diagnostics (task 3.3).
//
// Feature: source-generator-json-typeinfo, Property 7: VISTA0050 coverage set and diagnostic conformance.
//
// Validates: Requirements 9.1, 9.2, 9.3, 9.4
//
// Property 7 (design.md) bundles three conjuncts about the per-view JsonTypeInfo generator's diagnostics:
//
//   (A) For any COVERED view, the VISTA0050 diagnostic names EXACTLY the Serializable_DTO_Set
//       { TRow, ViewListResult<TRow>, PagedResult<TRow> } plus TCrud IF AND ONLY IF the view is writable
//       with a named TCrud (R9.1).
//   (B) For any view with a NON-EMITTABLE DTO member, exactly ONE VISTA0051 is reported naming the
//       offending type/member and NO per-view context is emitted for it (R9.2).
//   (C) EVERY diagnostic the generator emits has an id matching `^VISTA[0-9]{4}$`, the category
//       `a2n.Vista.SourceGenerators`, a help link under docs/diagnostics/, and a NON-BLOCKING default
//       severity (Info or Warning, never Error) (R9.3, R9.4).
//
// Each conjunct is a separate [Test] carrying the same Property-7 tag, each a CsCheck imperative Sample
// with iter: 100 (the project's PBT convention; CsCheck pairs cleanly with TUnit [Test]). Conjuncts (A)/(B)
// drive the generator via CSharpGeneratorDriver over RANDOMLY-SHAPED views (varying namespace / view / row
// / crud identifiers and read-only vs writable / named-vs-object TCrud); conjunct (C) reads the shipped
// VISTA0050/VISTA0051 descriptor set back from the (internal) DiagnosticDescriptors holder by reflection
// (matching the no-InternalsVisibleTo convention) and asserts the id/category/help-link/severity contract.
//
// Only the run diagnostics (and, for (B), the generated-source set) are inspected; no emitted
// [ModuleInitializer] is executed. The generated per-view context source is task 5.1, so conjunct (B)'s
// "no context emitted" check is expressed as "no <View>_VistaJsonContext.g.cs is produced for the view",
// which is forward-compatible with the emitter.

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

public sealed class Vista0050CoverageAndDiagnosticConformancePropertyTests
{
    // ---- shared CsCheck identifier generators ---------------------------------------------------------

    // A valid C# identifier core: an uppercase leading letter followed by 2–6 lowercase letters, so it is
    // never a C# keyword (keywords are all lowercase) and always parses.
    private static readonly Gen<string> GenIdentifierCore =
        from first in Gen.Char['a', 'z']
        from rest in Gen.Char['a', 'z'].Array[2, 6]
        select char.ToUpperInvariant(first) + new string(rest);

    // ---- conjunct (A): VISTA0050 names exactly the Serializable_DTO_Set -------------------------------

    /// <summary>The write facet of a covered view: read-only, writable with a named TCrud, or writable
    /// with an <c>object</c> TCrud (read-DTO coverage only — no TCrud in the set, R1.2).</summary>
    private enum WriteFacet
    {
        ReadOnly,
        WritableNamedCrud,
        WritableObjectCrud,
    }

    /// <summary>One generated covered-view input: its write facet and the varied identifiers.</summary>
    private sealed record CoveredViewInput(
        WriteFacet Facet,
        string Namespace,
        string ViewName,
        string RowName,
        string CrudName);

    // Distinct prefixes ("Ns"/"View"/"Row"/"Crud") guarantee the names never collide even when their random
    // cores coincide, so every rendered source compiles with distinct names while still varying each name.
    private static readonly Gen<CoveredViewInput> GenCoveredViewInput =
        from facet in Gen.Int[0, 2]
        from ns in GenIdentifierCore
        from view in GenIdentifierCore
        from row in GenIdentifierCore
        from crud in GenIdentifierCore
        select new CoveredViewInput(
            (WriteFacet)facet, "Ns" + ns, "View" + view, "Row" + row, "Crud" + crud);

    // The fixed message marker after which the {1} type list begins (see DiagnosticDescriptors.VISTA0050).
    private const string Vista0050TypeListMarker = "optional for these types: ";

    [Test]
    public void Covered_View_VISTA0050_Names_Exactly_The_Serializable_DTO_Set()
    {
        // Feature: source-generator-json-typeinfo, Property 7: VISTA0050 coverage set and diagnostic
        // conformance.
        GenCoveredViewInput.Sample(
            input =>
            {
                var source = RenderCoveredViewSource(input);
                var result = ViewJsonContextGeneratorTestHarness.Run(source);

                // A covered view raises exactly one VISTA0050 (Info) and no VISTA0051, with a green build
                // (R9.1, R9.4).
                var vista0050 = result.Diagnostics.Where(static d => d.Id == "VISTA0050").ToArray();
                if (vista0050.Length != 1)
                {
                    return false;
                }

                if (vista0050[0].Severity != DiagnosticSeverity.Info)
                {
                    return false;
                }

                if (result.Diagnostics.Any(static d => d.Id == "VISTA0051"))
                {
                    return false;
                }

                if (result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
                {
                    return false;
                }

                var message = vista0050[0].GetMessage(CultureInfo.InvariantCulture);

                // The message must name the view (the {0} placeholder).
                if (!message.Contains("'" + input.ViewName + "'", StringComparison.Ordinal))
                {
                    return false;
                }

                // Parse the {1} type list back out and compare to the exact expected set — same order, no
                // more, no fewer, correct global:: qualification, TCrud present iff writable with a named
                // TCrud (R9.1).
                var actualTypes = ParseTypeList(message, Vista0050TypeListMarker);
                var expectedTypes = ExpectedSerializableDtoSet(input);

                return actualTypes.SequenceEqual(expectedTypes, StringComparer.Ordinal);
            },
            iter: 100,
            print: RenderCoveredViewSource);
    }

    /// <summary>
    /// The exact, ordered <c>global::</c>-qualified Serializable_DTO_Set the generator must name for the
    /// given shape: <c>{ TRow, ViewListResult&lt;TRow&gt;, PagedResult&lt;TRow&gt; }</c> plus <c>TCrud</c>
    /// iff writable with a named <c>TCrud</c>.
    /// </summary>
    private static IReadOnlyList<string> ExpectedSerializableDtoSet(CoveredViewInput input)
    {
        var rowFqn = $"global::{input.Namespace}.{input.RowName}";
        var types = new List<string>
        {
            rowFqn,
            $"global::a2n.Vista.Ports.ViewListResult<{rowFqn}>",
            $"global::a2n.Vista.Results.PagedResult<{rowFqn}>",
        };

        if (input.Facet == WriteFacet.WritableNamedCrud)
        {
            types.Add($"global::{input.Namespace}.{input.CrudName}");
        }

        return types;
    }

    /// <summary>
    /// Renders a compilable covered view: a named row type with only emittable members (a scalar, a
    /// string, a nullable value type, and an enum), and — for a writable facet — a named or <c>object</c>
    /// write model. All member shapes are in the Emittable_Shape set so the view is classified covered.
    /// </summary>
    private static string RenderCoveredViewSource(CoveredViewInput input)
    {
        var rowAndEnum = $@"
    public enum {input.RowName}Kind {{ Alpha, Beta }}

    public sealed class {input.RowName}
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}
        public {input.RowName}Kind Kind {{ get; set; }}
    }}
";

        var crudDecl = input.Facet == WriteFacet.WritableNamedCrud
            ? $@"
    public sealed class {input.CrudName}
    {{
        public string Name {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}
    }}
"
            : string.Empty;

        var baseClause = input.Facet switch
        {
            WriteFacet.ReadOnly => $"a2n.Vista.Authoring.View<{input.RowName}>",
            WriteFacet.WritableNamedCrud => $"a2n.Vista.Authoring.View<{input.RowName}, {input.CrudName}>",
            WriteFacet.WritableObjectCrud => $"a2n.Vista.Authoring.View<{input.RowName}, object>",
            _ => $"a2n.Vista.Authoring.View<{input.RowName}>",
        };

        return $@"
namespace {input.Namespace}
{{{rowAndEnum}{crudDecl}
    public partial class {input.ViewName} : {baseClause}
    {{
    }}
}}
";
    }

    // ---- conjunct (B): a non-emittable DTO member → exactly one VISTA0051, no context -----------------

    /// <summary>One generated non-emittable-view input: the varied identifiers.</summary>
    private sealed record NonEmittableViewInput(string Namespace, string ViewName, string RowName);

    private static readonly Gen<NonEmittableViewInput> GenNonEmittableViewInput =
        from ns in GenIdentifierCore
        from view in GenIdentifierCore
        from row in GenIdentifierCore
        select new NonEmittableViewInput("Ns" + ns, "View" + view, "Row" + row);

    [Test]
    public void NonEmittable_Member_Reports_Single_VISTA0051_And_Emits_No_Context()
    {
        // Feature: source-generator-json-typeinfo, Property 7: VISTA0050 coverage set and diagnostic
        // conformance.
        GenNonEmittableViewInput.Sample(
            input =>
            {
                var source = RenderNonEmittableViewSource(input);
                var result = ViewJsonContextGeneratorTestHarness.Run(source);

                // R9.2: a candidate with a non-emittable DTO member reports exactly one VISTA0051
                // (Warning), naming the offending type/member, and is NOT covered — no VISTA0050.
                var vista0051 = result.Diagnostics.Where(static d => d.Id == "VISTA0051").ToArray();
                if (vista0051.Length != 1)
                {
                    return false;
                }

                if (vista0051[0].Severity != DiagnosticSeverity.Warning)
                {
                    return false;
                }

                if (result.Diagnostics.Any(static d => d.Id == "VISTA0050"))
                {
                    return false;
                }

                var message = vista0051[0].GetMessage(CultureInfo.InvariantCulture);

                // The message must name the view and the offending member ('Payload').
                if (!message.Contains("'" + input.ViewName + "'", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!message.Contains("Payload", StringComparison.Ordinal))
                {
                    return false;
                }

                // R9.2: no per-view context is emitted for a not-covered view; the build stays green.
                if (result.HasGeneratedContextFor(input.ViewName))
                {
                    return false;
                }

                return !result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
            },
            iter: 100,
            print: RenderNonEmittableViewSource);
    }

    /// <summary>
    /// Renders a compilable candidate view whose row DTO carries a NON-EMITTABLE member: a member named
    /// <c>Payload</c> typed as <c>object</c> — an unsupported polymorphic shape the generator cannot emit
    /// reflection-free via <c>JsonMetadataServices</c> (design.md Emittable_Shape table). The remaining
    /// members are emittable, so the view is a genuine candidate that is nevertheless not covered (R1.5).
    /// </summary>
    private static string RenderNonEmittableViewSource(NonEmittableViewInput input) => $@"
namespace {input.Namespace}
{{
    public sealed class {input.RowName}
    {{
        public int Id {{ get; set; }}
        public object Payload {{ get; set; }} = new object();
    }}

    public partial class {input.ViewName} : a2n.Vista.Authoring.View<{input.RowName}>
    {{
    }}
}}
";

    // ---- conjunct (C): descriptor id / category / help-link / non-blocking-severity conformance -------

    // R9.3 / Spec 03 D81: the VISTA prefix immediately followed by exactly four decimal digits.
    private static readonly Regex DiagnosticIdPattern = new("^VISTA[0-9]{4}$", RegexOptions.Compiled);

    private const string ExpectedCategory = "a2n.Vista.SourceGenerators";

    private const string HelpLinkSegment = "docs/diagnostics/";

    // The per-view JsonTypeInfo diagnostic family this feature (D125/D126) owns.
    private static readonly HashSet<string> JsonContextIds = new(StringComparer.Ordinal)
    {
        "VISTA0050",
        "VISTA0051",
    };

    private static readonly DiagnosticDescriptor[] JsonContextDescriptors = LoadJsonContextDescriptors();

    private static readonly Gen<DiagnosticDescriptor> GenJsonContextDescriptor =
        Gen.Int[0, JsonContextDescriptors.Length - 1].Select(i => JsonContextDescriptors[i]);

    [Test]
    public void Every_JsonContext_Descriptor_Conforms_To_Id_Category_HelpLink_And_NonBlocking_Severity()
    {
        // Feature: source-generator-json-typeinfo, Property 7: VISTA0050 coverage set and diagnostic
        // conformance.

        // Guard: the reflection lookup must actually find both descriptors, otherwise a vacuously-true
        // property could hide a rename/removal of VISTA0050/VISTA0051.
        if (JsonContextDescriptors.Length != JsonContextIds.Count)
        {
            throw new InvalidOperationException(
                $"Expected {JsonContextIds.Count} JsonTypeInfo descriptors ({string.Join(", ", JsonContextIds)}), " +
                $"found {JsonContextDescriptors.Length}: [{string.Join(", ", JsonContextDescriptors.Select(d => d.Id))}].");
        }

        GenJsonContextDescriptor.Sample(
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
                return descriptor.DefaultSeverity is DiagnosticSeverity.Info or DiagnosticSeverity.Warning;
            },
            iter: 100,
            print: d =>
                $"{d.Id} (category='{d.Category}', severity={d.DefaultSeverity}, helpLink='{d.HelpLinkUri}')");
    }

    /// <summary>
    /// Reads the JsonTypeInfo <see cref="DiagnosticDescriptor"/>s ({ VISTA0050, VISTA0051 }) back from the
    /// generator assembly's internal <c>DiagnosticDescriptors</c> holder by reflection, so the (internal)
    /// holder need not be visible to the test assembly (matching the no-InternalsVisibleTo convention).
    /// </summary>
    private static DiagnosticDescriptor[] LoadJsonContextDescriptors()
    {
        var generatorAssembly = typeof(ViewJsonContextGenerator).Assembly;
        var holder = generatorAssembly.GetType(
            "a2n.Vista.SourceGenerators.DiagnosticDescriptors", throwOnError: true)!;

        return holder
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .Where(d => JsonContextIds.Contains(d.Id))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();
    }

    // ---- shared helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Extracts the comma-separated type list that follows <paramref name="marker"/> in a diagnostic
    /// message. Splitting on ", " is safe: each generic argument is a single type, so no comma occurs
    /// inside a name.
    /// </summary>
    private static IReadOnlyList<string> ParseTypeList(string message, string marker)
    {
        var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return Array.Empty<string>();
        }

        var list = message[(markerIndex + marker.Length)..];
        return list.Split(new[] { ", " }, StringSplitOptions.None);
    }
}
