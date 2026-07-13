// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Style A coverage generator's (StyleAShapeGenerator, the fifth phase — M9,
// D129/D130, style-a-coverage) deterministic emission (task 5.4).
//
// Feature: style-a-coverage, Property 6: Deterministic emission
//
// Validates: Requirements 7.4
//
// Property 6 (design.md "Correctness Properties"): for any AddView call site, two runs of the generator
// over the same input produce byte-identical accessor and context source. R7.4 requires the emitted code to
// be deterministic — byte-for-byte stable for the same input. This property proves that running the
// StyleAShapeGenerator TWICE over the SAME input compilation produces byte-for-byte identical generated
// source (both the `<Template>_<View>_VistaAccessors.g.cs` accessor maps AND the
// `<Template>_<View>_VistaJsonContext.g.cs` per-view contexts) and STABLE hint names. The two runs are
// fully independent driver runs (each builds its own CSharpCompilation from the same source and drives a
// fresh CSharpGeneratorDriver, via StyleAShapeGeneratorTestHarness.Run — which constructs a fresh
// StyleAShapeGenerator per call), so any nondeterminism in the emitter (hash-set ordering, culture-sensitive
// formatting, wall-clock/GUID content, unstable line endings) — and any hidden per-instance generator state —
// would surface as a mismatch. Two Run calls therefore also cover the "two SEPARATE generator/driver
// instances" stability check.
//
// Strategy: a CsCheck generator produces a compilation of ONE-TO-FOUR COVERED Style A views spanning the
// coverage matrix (design.md "Coverage classification") — a named-TRow read-only view (emits an accessor
// map + a read-DTO context), a named-TRow writable view (accessor map + read-DTO + TCrud context), and an
// anonymous-TRow + named-TCrud view (the D96 asymmetry: TCrud-only context, NO accessor map). Each view is a
// ViewTemplate<TDbContext>.AddView(...) call site placed in its OWN namespace (an index suffix guarantees the
// namespaces never collide, so the emitted hint names — which fold in the namespace + template + constant
// view name — are unique across views) with randomly-varied template / view / row / crud identifiers and a
// randomly-varied (all-emittable) row member shape (base int/string/int? always, plus an optional enum,
// collection, and byte[]). Every generated view is COVERED and emits at least one artifact, so the emission
// is non-vacuous. The test runs the harness twice over the identical rendered source and asserts:
//   * a non-vacuous SANITY guard: exactly one context per view (every covered view emits one context) and
//     exactly one accessor per NAMED-TRow view (an anonymous-row view emits none) — so the property is never
//     trivially satisfied by an empty output set;
//   * HINT-NAME stability: both runs emit the same set of hint names; and
//   * BYTE-FOR-BYTE source stability: for every hint name the emitted source string is identical (ordinal).
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample, pairs
// cleanly with TUnit [Test]). Only the emitted source text is inspected; no generated source is executed and
// no downstream compilation of the emitted source is required (a generator emits its source regardless of
// whether the referenced Core/STJ envelope types resolve in the stub compilation).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CsCheck;
using Microsoft.CodeAnalysis;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class StyleADeterministicEmissionPropertyTests
{
    /// <summary>The covered coverage-matrix row a generated Style A view exercises.</summary>
    private enum Coverage
    {
        /// <summary>Named <c>TRow</c>, read-only — emits an accessor map + a read-DTO context.</summary>
        NamedReadOnly,

        /// <summary>Named <c>TRow</c> + named <c>TCrud</c> — accessor map + read-DTO + <c>TCrud</c> context.</summary>
        NamedWritable,

        /// <summary>
        /// Anonymous <c>TRow</c> + named <c>TCrud</c> (the D96 asymmetry) — emits a <c>TCrud</c>-only context
        /// and NO accessor map (an anonymous row is unnameable in generated source).
        /// </summary>
        AnonymousWritable,
    }

    /// <summary>One generated covered Style A view: its matrix row and the varied identifiers/shape.</summary>
    private sealed record ViewInput(
        Coverage Coverage,
        string TemplateName,
        string ViewName,
        string RowName,
        string CrudName,
        bool IncludeEnum,
        bool IncludeCollection,
        bool IncludeBlob);

    // The hint-name suffixes the StyleAShapeGenerator emitters use (BuildAccessorHintName /
    // BuildContextHintName). These are the ONLY two artifact kinds the generator produces.
    private const string AccessorHintSuffix = "_VistaAccessors.g.cs";
    private const string ContextHintSuffix = "_VistaJsonContext.g.cs";

    // A valid C# identifier core: an uppercase leading letter followed by 2–6 lowercase letters, so it is
    // never a C# keyword (keywords are all lowercase) and always parses.
    private static readonly Gen<string> GenIdentifierCore =
        from first in Gen.Char['a', 'z']
        from rest in Gen.Char['a', 'z'].Array[2, 6]
        select char.ToUpperInvariant(first) + new string(rest);

    // One covered view: a random matrix row, four distinct-prefixed identifiers ("Tmpl"/"view_"/"Row"/"Crud"
    // never collide even when their random cores coincide), and a random all-emittable row member shape.
    private static readonly Gen<ViewInput> GenViewInput =
        from coverage in Gen.Int[0, 2]
        from tmpl in GenIdentifierCore
        from view in GenIdentifierCore
        from row in GenIdentifierCore
        from crud in GenIdentifierCore
        from includeEnum in Gen.Bool
        from includeCollection in Gen.Bool
        from includeBlob in Gen.Bool
        select new ViewInput(
            (Coverage)coverage,
            "Tmpl" + tmpl,
            "view_" + view.ToLowerInvariant(),
            "Row" + row,
            "Crud" + crud,
            includeEnum,
            includeCollection,
            includeBlob);

    // A compilation of one-to-four covered views. Each view is placed in its OWN namespace (Ns0, Ns1, …) so
    // multiple views never collide regardless of their random identifier cores — this exercises the
    // "multiple call sites per compilation" case while keeping the source compilable and the hint names
    // unique.
    private static readonly Gen<ViewInput[]> GenViewInputs = GenViewInput.Array[1, 4];

    [Test]
    public void Emission_Is_Deterministic_Byte_For_Byte_Across_Two_Runs()
    {
        // Feature: style-a-coverage, Property 6: Deterministic emission
        GenViewInputs.Sample(
            views =>
            {
                var source = RenderSource(views);

                // Two fully-independent driver runs over the SAME source (each builds its own compilation and
                // a fresh StyleAShapeGenerator + CSharpGeneratorDriver).
                var first = ExtractGeneratedSources(StyleAShapeGeneratorTestHarness.Run(source));
                var second = ExtractGeneratedSources(StyleAShapeGeneratorTestHarness.Run(source));

                // Non-vacuous sanity guard: every covered view emits exactly one context, and every
                // named-TRow view emits exactly one accessor map (an anonymous-row view emits none). If these
                // counts are wrong, the emission assumption is broken and a byte-comparison over an empty set
                // would falsely pass — so the property is anchored to real, expected output.
                var expectedContexts = views.Length;
                var expectedAccessors = views.Count(static v => v.Coverage != Coverage.AnonymousWritable);
                if (CountBySuffix(first, ContextHintSuffix) != expectedContexts
                    || CountBySuffix(first, AccessorHintSuffix) != expectedAccessors)
                {
                    return false;
                }

                // Hint-name stability: both runs must emit the same set of hint names.
                if (!first.Keys.OrderBy(static k => k, StringComparer.Ordinal)
                        .SequenceEqual(
                            second.Keys.OrderBy(static k => k, StringComparer.Ordinal),
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
    /// Projects a driver run's emitted files into a hint-name → source-text map. The StyleAShapeGenerator
    /// emits only <c>*_VistaAccessors.g.cs</c> and <c>*_VistaJsonContext.g.cs</c>, so every generated source
    /// is one of the two artifacts this property governs; all are included.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ExtractGeneratedSources(GeneratorDriverRunResult result)
        => result.Results
            .Single()
            .GeneratedSources
            .ToDictionary(
                static s => s.HintName,
                static s => s.SourceText.ToString(),
                StringComparer.Ordinal);

    /// <summary>Counts the emitted hint names ending with <paramref name="suffix"/> (one artifact kind).</summary>
    private static int CountBySuffix(IReadOnlyDictionary<string, string> sources, string suffix)
        => sources.Keys.Count(k => k.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>
    /// Renders a compilable source file placing each covered Style A view in its own namespace (<c>Ns0</c>,
    /// <c>Ns1</c>, …) so multiple views never collide. Each view is a
    /// <c>ViewTemplate&lt;TestDbContext&gt;.AddView(...)</c> call site of the requested coverage-matrix row:
    /// a named read-only view, a named writable view (<c>.WithCrud&lt;TCrud, TEntity&gt;()</c>), or an
    /// anonymous-row writable view. Named rows and crud models declare only Emittable_Shape members (int /
    /// string / int? always, plus an optional enum / collection / <c>byte[]</c>), so every generated view is
    /// covered and emits at least one artifact.
    /// </summary>
    private static string RenderSource(ViewInput[] views)
    {
        var builder = new StringBuilder();
        builder.Append("using System.Linq;\n");

        for (var i = 0; i < views.Length; i++)
        {
            var view = views[i];
            var ns = "Ns" + i.ToString(CultureInfo.InvariantCulture);
            var named = view.Coverage != Coverage.AnonymousWritable;
            var writable = view.Coverage != Coverage.NamedReadOnly;

            builder.Append($@"
namespace {ns}
{{");

            // Named read row DTO (only emittable members) — omitted for the anonymous-row case.
            if (named)
            {
                if (view.IncludeEnum)
                {
                    builder.Append($@"
    public enum {view.RowName}Kind {{ Alpha, Beta }}");
                }

                builder.Append($@"
    public sealed class {view.RowName}
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}");

                if (view.IncludeEnum)
                {
                    builder.Append($@"
        public {view.RowName}Kind Kind {{ get; set; }}");
                }

                if (view.IncludeCollection)
                {
                    builder.Append($@"
        public System.Collections.Generic.List<int> Tags {{ get; set; }}
            = new System.Collections.Generic.List<int>();");
                }

                if (view.IncludeBlob)
                {
                    builder.Append($@"
        public byte[] Blob {{ get; set; }} = System.Array.Empty<byte>();");
                }

                builder.Append($@"
    }}");
            }

            // Named write model + entity — present for both writable rows (TCrud is always a named type, D38).
            if (writable)
            {
                builder.Append($@"
    public sealed class {view.CrudName}
    {{
        public string Name {{ get; set; }} = string.Empty;
        public int? Score {{ get; set; }}
    }}

    public sealed class {view.CrudName}Entity
    {{
        public int Id {{ get; set; }}
    }}");
            }

            // The AddView call site (with an optional chained WithCrud). A named row supplies an explicit
            // type argument; the anonymous row is inferred from the projection's anonymous element type.
            var addView = view.Coverage switch
            {
                Coverage.NamedReadOnly =>
                    $@"views.AddView<{view.RowName}>(""{view.ViewName}"", (db, sp) => new {view.RowName}[0].AsQueryable())",
                Coverage.NamedWritable =>
                    $@"views.AddView<{view.RowName}>(""{view.ViewName}"", (db, sp) => new {view.RowName}[0].AsQueryable())
                 .WithCrud<{view.CrudName}, {view.CrudName}Entity>()",
                _ =>
                    $@"views.AddView(""{view.ViewName}"", (db, sp) => new[] {{ new {{ Id = 1, Label = ""x"" }} }}.AsQueryable())
                 .WithCrud<{view.CrudName}, {view.CrudName}Entity>()",
            };

            builder.Append($@"
    public class {view.TemplateName}
        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>
    {{
        protected internal override void Configure(
            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)
        {{
            {addView};
        }}
    }}
}}");
        }

        return builder.ToString();
    }
}
