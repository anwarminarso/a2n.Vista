// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 4 (M9, D123/D124, source-generator-http-surface) ViewInvokerGenerator's
// VISTA0041 serialization-guidance type set (task 4.3).
//
// Feature: source-generator-http-surface, Property 8: VISTA0041 guidance names exactly the required
// serializable types.
//
// Validates: Requirements 5.4, 9.2
//
// The generator reports one VISTA0041 (Info) per covered typed Style B view, composing the exact
// [JsonSerializable] type set the developer registers via AddVistaJsonContext(...):
//
//     { TRow, ViewListResult<TRow>, PagedResult<TRow> }  (+ TCrud iff writable with a named TCrud)
//
// as a comma-separated list of global::-qualified names in the message's {1} placeholder. This property
// proves that for RANDOMLY-SHAPED covered views — varying namespace / view / row / crud type names and
// read-only vs writable — the VISTA0041 message names EXACTLY that set (same order, no more, no fewer),
// with correct global:: qualification and the writable TCrud present iff the view is writable with a
// named TCrud.
//
// Strategy: a CsCheck generator produces valid, distinct C# identifiers for the namespace and the view /
// row / crud types (distinct prefixes guarantee no collision even when the random cores coincide) plus a
// read-only/writable flag. Each case renders a small compilable view, drives the ViewInvokerGenerator via
// CSharpGeneratorDriver (ViewInvokerGeneratorTestHarness), then parses the single VISTA0041 message's type
// list back out (splitting on ", " — safe because each generic argument is a single type, so no comma
// occurs inside a name) and asserts the parsed sequence equals the exact expected set for the shape. It
// also asserts exactly one VISTA0041, no VISTA0040 (the view is covered, not uncovered), and a green build.
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample, pairs
// cleanly with TUnit [Test]). Only the run diagnostics are inspected; no generated source is executed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CsCheck;
using Microsoft.CodeAnalysis;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class SerializationGuidanceTypeSetPropertyTests
{
    /// <summary>One generated covered-view input: its writable flag and the four varied identifiers.</summary>
    private sealed record ViewInput(
        bool IsWritable,
        string Namespace,
        string ViewName,
        string RowName,
        string CrudName);

    // A valid C# identifier core: an uppercase leading letter followed by 2–6 lowercase letters, so it is
    // never a C# keyword (keywords are all lowercase) and always parses.
    private static readonly Gen<string> GenIdentifierCore =
        from first in Gen.Char['a', 'z']
        from rest in Gen.Char['a', 'z'].Array[2, 6]
        select char.ToUpperInvariant(first) + new string(rest);

    // Distinct prefixes ("Ns"/"View"/"Row"/"Crud") guarantee the four names never collide even when their
    // random cores happen to coincide, so the rendered source always compiles with distinct type/namespace
    // names while still varying every name across iterations.
    private static readonly Gen<ViewInput> GenViewInput =
        from isWritable in Gen.Bool
        from ns in GenIdentifierCore
        from view in GenIdentifierCore
        from row in GenIdentifierCore
        from crud in GenIdentifierCore
        select new ViewInput(isWritable, "Ns" + ns, "View" + view, "Row" + row, "Crud" + crud);

    // The fixed message marker after which the {1} type list begins (see DiagnosticDescriptors.VISTA0041).
    private const string TypeListMarker = "AddVistaJsonContext(...): ";

    [Test]
    public void VISTA0041_Guidance_Names_Exactly_The_Required_Serializable_Types()
    {
        // Feature: source-generator-http-surface, Property 8: VISTA0041 guidance names exactly the required
        // serializable types.
        GenViewInput.Sample(
            input =>
            {
                var source = RenderViewSource(input);
                var result = ViewInvokerGeneratorTestHarness.Run(source);

                // A covered view raises exactly one VISTA0041 and no VISTA0040 (it is covered, not
                // uncovered), and the build stays green (R9.2, R9.4).
                var vista0041 = result.Diagnostics.Where(static d => d.Id == "VISTA0041").ToArray();
                if (vista0041.Length != 1)
                {
                    return false;
                }

                if (result.Diagnostics.Any(static d => d.Id == "VISTA0040"))
                {
                    return false;
                }

                if (result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
                {
                    return false;
                }

                var message = vista0041[0].GetMessage(CultureInfo.InvariantCulture);

                // The message must name the view (the {0} placeholder).
                if (!message.Contains("'" + input.ViewName + "'", StringComparison.Ordinal))
                {
                    return false;
                }

                // Parse the {1} type list back out and compare it to the exact expected set for the shape —
                // same order, no more, no fewer, with correct global:: qualification (R5.4, R9.2).
                var actualTypes = ParseTypeList(message);
                var expectedTypes = ExpectedTypes(input);

                return actualTypes.SequenceEqual(expectedTypes, StringComparer.Ordinal);
            },
            iter: 100,
            // On failure, print the exact view source that broke the property for a reproducible example.
            print: RenderViewSource);
    }

    /// <summary>
    /// The exact, ordered global::-qualified [JsonSerializable] type set the generator must name for the
    /// given shape: { TRow, ViewListResult&lt;TRow&gt;, PagedResult&lt;TRow&gt; } plus TCrud iff writable.
    /// </summary>
    private static IReadOnlyList<string> ExpectedTypes(ViewInput input)
    {
        var rowFqn = $"global::{input.Namespace}.{input.RowName}";
        var types = new List<string>
        {
            rowFqn,
            $"global::a2n.Vista.Ports.ViewListResult<{rowFqn}>",
            $"global::a2n.Vista.Results.PagedResult<{rowFqn}>",
        };

        if (input.IsWritable)
        {
            types.Add($"global::{input.Namespace}.{input.CrudName}");
        }

        return types;
    }

    /// <summary>
    /// Extracts the comma-separated {1} type list from the VISTA0041 message. Splitting on ", " is safe:
    /// each generic argument is a single type, so no comma occurs inside a name.
    /// </summary>
    private static IReadOnlyList<string> ParseTypeList(string message)
    {
        var markerIndex = message.IndexOf(TypeListMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return Array.Empty<string>();
        }

        var list = message[(markerIndex + TypeListMarker.Length)..];
        return list.Split(new[] { ", " }, StringSplitOptions.None);
    }

    /// <summary>
    /// Renders a compilable source file declaring the row type (and, for a writable view, the crud type)
    /// and a covered partial view deriving the recognized Vista base — arity-1 View&lt;TRow&gt; for a
    /// read-only view or arity-2 View&lt;TRow, TCrud&gt; for a writable view.
    /// </summary>
    private static string RenderViewSource(ViewInput input)
    {
        if (input.IsWritable)
        {
            return $@"
namespace {input.Namespace}
{{
    public sealed class {input.RowName}
    {{
        public int Id {{ get; set; }}
    }}

    public sealed class {input.CrudName}
    {{
        public string Name {{ get; set; }} = string.Empty;
    }}

    public partial class {input.ViewName} : a2n.Vista.Authoring.View<{input.RowName}, {input.CrudName}>
    {{
    }}
}}
";
        }

        return $@"
namespace {input.Namespace}
{{
    public sealed class {input.RowName}
    {{
        public int Id {{ get; set; }}
    }}

    public partial class {input.ViewName} : a2n.Vista.Authoring.View<{input.RowName}>
    {{
    }}
}}
";
    }
}
