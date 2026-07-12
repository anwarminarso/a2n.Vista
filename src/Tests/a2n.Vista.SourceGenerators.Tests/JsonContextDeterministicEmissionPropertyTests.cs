// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 5 (M9, D125/D126, source-generator-json-typeinfo)
// ViewJsonContextGenerator's deterministic emission (task 5.3).
//
// Feature: source-generator-json-typeinfo, Property 5: Deterministic emission
//
// Validates: Requirements 7.4
//
// R7.4 requires the emitted per-view JsonTypeInfo code to be deterministic — byte-for-byte stable for the
// same input. This property proves that running the ViewJsonContextGenerator TWICE over the SAME input
// compilation produces byte-for-byte identical `<View>_VistaJsonContext.g.cs` source and STABLE hint
// names. The two runs are fully independent driver runs (each builds its own CSharpCompilation from the
// same source and drives a fresh CSharpGeneratorDriver, via ViewJsonContextGeneratorTestHarness.Run), so
// any nondeterminism in the emitter (hash-set ordering, culture-sensitive formatting, wall-clock/GUID
// content, unstable line endings) would surface as a mismatch.
//
// Strategy: a CsCheck generator produces a compilation of ONE-TO-FOUR covered typed Style B views, each in
// its OWN namespace (an index suffix guarantees the namespaces never collide) with randomly-varied view /
// row / crud identifiers and a random read-only vs writable flag per view. Distinct "View"/"Row"/"Crud"
// prefixes keep the three names inside one namespace from colliding. Every row and crud DTO carries only
// Emittable_Shape members (a scalar, a string, a nullable value type, an enum, a collection, and byte[])
// so each covered view (named TQuery, and — for a writable view — a named TCrud) is classified covered and
// emits exactly one context with a public parameterless ctor. The per-compilation emitted-context set is
// therefore non-empty and its size equals the view count. The test runs the harness twice over the
// identical rendered source and asserts:
//   * both runs emit the SAME set of context hint names (hint-name stability), and
//   * for every hint name the emitted source string is byte-for-byte identical (ordinal comparison), and
//   * the emitted-context count equals the number of generated views (sanity: every view emitted).
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample, pairs
// cleanly with TUnit [Test]). Only the emitted source text is inspected; no generated source is executed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CsCheck;
using Microsoft.CodeAnalysis;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class JsonContextDeterministicEmissionPropertyTests
{
    /// <summary>One generated covered view: its writable flag and the three varied identifiers.</summary>
    private sealed record ViewInput(
        bool IsWritable,
        string ViewName,
        string RowName,
        string CrudName);

    // The hint-name suffix the emitter uses for the generated per-view JsonTypeInfo context
    // (ViewJsonContextGenerator; <View>_VistaJsonContext.g.cs).
    private const string ContextHintSuffix = "_VistaJsonContext.g.cs";

    // A valid C# identifier core: an uppercase leading letter followed by 2–6 lowercase letters, so it is
    // never a C# keyword (keywords are all lowercase) and always parses.
    private static readonly Gen<string> GenIdentifierCore =
        from first in Gen.Char['a', 'z']
        from rest in Gen.Char['a', 'z'].Array[2, 6]
        select char.ToUpperInvariant(first) + new string(rest);

    // One covered view: random writable flag and three names. Distinct "View"/"Row"/"Crud" prefixes keep
    // the three names from colliding inside a single namespace even when their random cores coincide.
    private static readonly Gen<ViewInput> GenViewInput =
        from isWritable in Gen.Bool
        from view in GenIdentifierCore
        from row in GenIdentifierCore
        from crud in GenIdentifierCore
        select new ViewInput(isWritable, "View" + view, "Row" + row, "Crud" + crud);

    // A compilation of one-to-four covered views. Each view is placed in its OWN namespace (Ns0, Ns1, …)
    // so multiple views never collide regardless of their random identifier cores — this exercises the
    // "multiple views per compilation" case the task calls for while keeping the source compilable.
    private static readonly Gen<ViewInput[]> GenViewInputs = GenViewInput.Array[1, 4];

    [Test]
    public void Emission_Is_Deterministic_Byte_For_Byte_Across_Two_Runs()
    {
        // Feature: source-generator-json-typeinfo, Property 5: Deterministic emission
        GenViewInputs.Sample(
            views =>
            {
                var source = RenderSource(views);

                // Two fully-independent driver runs over the SAME source (each builds its own compilation
                // and a fresh CSharpGeneratorDriver).
                var first = ExtractContextSources(ViewJsonContextGeneratorTestHarness.Run(source));
                var second = ExtractContextSources(ViewJsonContextGeneratorTestHarness.Run(source));

                // Every covered view with a public parameterless ctor emits exactly one context, so the
                // emitted-context count must equal the number of generated views (sanity guard).
                if (first.Count != views.Length)
                {
                    return false;
                }

                // Hint-name stability: both runs must emit the same set of hint names.
                if (!first.Keys.OrderBy(static k => k, StringComparer.Ordinal)
                        .SequenceEqual(second.Keys.OrderBy(static k => k, StringComparer.Ordinal),
                            StringComparer.Ordinal))
                {
                    return false;
                }

                // Byte-for-byte source stability: for every hint name the emitted source is identical.
                foreach (var hintName in first.Keys)
                {
                    if (!string.Equals(first[hintName], second[hintName], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            },
            iter: 100,
            // On failure, print the exact rendered source that broke the property for a reproducible case.
            print: RenderSource);
    }

    /// <summary>
    /// Projects a driver run's emitted per-view JsonTypeInfo files into a hint-name → source-text map,
    /// keeping only the <c>*_VistaJsonContext.g.cs</c> outputs (the artifacts this property governs).
    /// </summary>
    private static IReadOnlyDictionary<string, string> ExtractContextSources(GeneratorDriverRunResult result)
        => result.Results
            .Single()
            .GeneratedSources
            .Where(static s => s.HintName.EndsWith(ContextHintSuffix, StringComparison.Ordinal))
            .ToDictionary(
                static s => s.HintName,
                static s => s.SourceText.ToString(),
                StringComparer.Ordinal);

    /// <summary>
    /// Renders a compilable source file placing each covered view in its own namespace (<c>Ns0</c>,
    /// <c>Ns1</c>, …) so multiple views never collide. Each view declares its row type (and, for a writable
    /// view, its crud type) with only Emittable_Shape members — a scalar, a string, a nullable value type,
    /// an enum, a collection, and <c>byte[]</c> — and derives the recognized Vista base: arity-1
    /// <c>View&lt;TRow&gt;</c> for a read-only view or arity-2 <c>View&lt;TRow, TCrud&gt;</c> for a writable
    /// view.
    /// </summary>
    private static string RenderSource(ViewInput[] views)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < views.Length; i++)
        {
            var view = views[i];
            var ns = "Ns" + i.ToString(CultureInfo.InvariantCulture);

            builder.Append($@"
namespace {ns}
{{
    public enum {view.RowName}Kind {{ Alpha, Beta }}

    public sealed class {view.RowName}
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}
        public {view.RowName}Kind Kind {{ get; set; }}
        public System.Collections.Generic.List<int> Tags {{ get; set; }}
            = new System.Collections.Generic.List<int>();
        public byte[] Blob {{ get; set; }} = System.Array.Empty<byte>();
    }}
");

            if (view.IsWritable)
            {
                builder.Append($@"
    public sealed class {view.CrudName}
    {{
        public string Name {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}
    }}

    public partial class {view.ViewName} : a2n.Vista.Authoring.View<{view.RowName}, {view.CrudName}>
    {{
    }}
}}
");
            }
            else
            {
                builder.Append($@"
    public partial class {view.ViewName} : a2n.Vista.Authoring.View<{view.RowName}>
    {{
    }}
}}
");
            }
        }

        return builder.ToString();
    }
}
