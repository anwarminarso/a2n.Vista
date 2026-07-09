// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 3 (M9, D121/D122) WriteMapperGenerator's MapWritable_Analyzer
// (task 3.2).
//
// Feature: source-generator-write-mapper, Property 6: For any MapWritable chain, the MapWritable_Analyzer
// extracts exactly the declared (CrudMember, EntityMember) name pairs, in declaration order, when every
// argument is a Simple_Member_Selector (regardless of how many compiler-inserted Convert/ConvertChecked
// nodes wrap the member access), and classifies the view as not statically analyzable — extracting no
// pairs — as soon as any argument's innermost body is not a single member access on the lambda parameter.
//
// Validates: Requirements 2.1, 2.3, 2.4, 2.5
//
// Strategy: the analyzer is internal to the generator, so its extraction is observed through the driver
// output (CSharpGeneratorDriver via WriteMapperGeneratorTestHarness). A CsCheck generator produces a
// random MapWritable chain (1..5 mappings), where each of the two selectors per mapping is independently
// either a Simple_Member_Selector (in one of several wrapping variants — bare, parenthesized, or wrapped
// in one/two identity casts, exercising the conversion-unwrapping of R2.3) or a NON-simple selector
// (binary/unary/nested-member/literal — none of which is a single member access on the lambda parameter).
// Every selector is authored to return `int`, so the two-argument MapWritable<int> overload always
// resolves and only the selector *shape* varies.
//
//   * When every selector is simple, the chain is analyzable: the generator emits the view's
//     <View>_VistaWriteMapper.g.cs, whose body carries exactly one `e.<EntityMember> = m.<CrudMember>;`
//     assignment per mapping, in declaration order. All targets are non-key, non-token scalar members, so
//     the emitted "safe subset" equals the full whitelist and the assignment sequence reflects the
//     analyzer's extracted (CrudMember, EntityMember) pairs verbatim (R2.1, R2.2/order, R2.3).
//   * As soon as any selector is non-simple, the analyzer clears the whole pair set and marks the view
//     unanalyzable (R2.4): no <View>_VistaWriteMapper source is emitted and a VISTA0033 fallback warning
//     is raised instead.
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample,
// pairs cleanly with TUnit [Test]). Only the generated source TEXT and diagnostics are inspected; no
// generated [ModuleInitializer] is executed.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CsCheck;
using Microsoft.CodeAnalysis;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class AnalyzerMemberPairExtractionPropertyTests
{
    // Simple_Member_Selector wrapping variants for parameter `p` selecting int member `m`. Each unwraps
    // (parentheses + author-written identity casts, mirroring the compiler-inserted Convert nodes the
    // analyzer must see through — R2.3) to the same `p.m` member access rooted at the parameter, and each
    // returns int so the shared MapWritable<int> overload resolves.
    private static readonly Func<string, string, string>[] SimpleForms =
    {
        static (p, m) => $"{p} => {p}.{m}",
        static (p, m) => $"{p} => ({p}.{m})",
        static (p, m) => $"{p} => (({p}.{m}))",
        static (p, m) => $"{p} => (int){p}.{m}",
        static (p, m) => $"{p} => (int)(int){p}.{m}",
    };

    // NON-simple selector variants: none is a single member access on the lambda parameter, yet each
    // returns int so the two-argument MapWritable<int> overload still binds (only the shape is invalid).
    // Binary/unary have a member access but not as the whole body; the nested access is rooted at a
    // sub-member (not the parameter); the literal has no member access at all.
    private static readonly Func<string, string, string>[] NonSimpleForms =
    {
        static (p, m) => $"{p} => {p}.{m} + 1",
        static (p, m) => $"{p} => -{p}.{m}",
        static (p, _) => $"{p} => {p}.Nested.Value",
        static (p, _) => $"{p} => 42",
    };

    /// <summary>One selector: whether it is a Simple_Member_Selector, and which variant renders it.</summary>
    private sealed record SelectorSpec(bool Simple, int Variant);

    /// <summary>One MapWritable mapping: the source (TCrud) selector and the target (TEntity) selector.</summary>
    private sealed record MappingSpec(SelectorSpec From, SelectorSpec To);

    private static readonly Gen<SelectorSpec> GenSelector =
        Gen.Select(Gen.Bool, Gen.Int[0, 4], (simple, variant) => new SelectorSpec(simple, variant));

    private static readonly Gen<MappingSpec> GenMapping =
        Gen.Select(GenSelector, GenSelector, (from, to) => new MappingSpec(from, to));

    // A chain of 1..5 mappings. At least one mapping guarantees the analyzable case never collapses into
    // the zero-mapping VISTA0030 branch, keeping the property focused on extraction fidelity (R2.1/R2.3)
    // and the non-simple fallback (R2.4).
    private static readonly Gen<MappingSpec[]> GenChain =
        from count in Gen.Int[1, 5]
        from mappings in GenMapping.Array[count]
        select mappings;

    // Matches each emitted direct assignment `e.<EntityMember> = m.<CrudMember>;` so the extracted pair
    // sequence can be recovered from the generated source in emission (= declaration) order.
    private static readonly Regex AssignmentPattern =
        new(@"e\.(?<entity>\w+)\s*=\s*m\.(?<crud>\w+);", RegexOptions.Compiled);

    [Test]
    public void Analyzer_Extracts_Declared_Pairs_In_Order_And_Falls_Back_On_Non_Simple_Selectors()
    {
        // Feature: source-generator-write-mapper, Property 6: For any MapWritable chain, the
        // MapWritable_Analyzer extracts exactly the declared (CrudMember, EntityMember) name pairs, in
        // declaration order, when every argument is a Simple_Member_Selector (regardless of how many
        // compiler-inserted Convert/ConvertChecked nodes wrap the member access), and classifies the view
        // as not statically analyzable — extracting no pairs — as soon as any argument's innermost body is
        // not a single member access on the lambda parameter.
        GenChain.Sample(
            chain =>
            {
                var source = RenderViewSource(chain);
                var result = WriteMapperGeneratorTestHarness.Run(source);

                // The chain is analyzable exactly when EVERY selector on EVERY mapping is simple.
                var analyzable = chain.All(m => m.From.Simple && m.To.Simple);

                if (analyzable)
                {
                    // R2.1/R2.3: an analyzable chain emits the write mapper. No fallback warning.
                    if (!result.HasGeneratedSourceContaining("GenView_VistaWriteMapper"))
                    {
                        return false;
                    }

                    if (result.Diagnostics.Any(static d => d.Id == "VISTA0033"))
                    {
                        return false;
                    }

                    // The emitted assignments are the analyzer's extracted pairs in declaration order:
                    // one `e.E{i} = m.C{i};` per mapping, in the same sequence as authored (R2.1, R2.2).
                    var generated = result.GeneratedSourceContaining("GenView_VistaWriteMapper");
                    var extracted = AssignmentPattern.Matches(generated)
                        .Select(match => (Entity: match.Groups["entity"].Value, Crud: match.Groups["crud"].Value))
                        .ToArray();

                    var expected = Enumerable.Range(0, chain.Length)
                        .Select(i => (Entity: $"E{i}", Crud: $"C{i}"))
                        .ToArray();

                    return extracted.SequenceEqual(expected);
                }

                // R2.4: a non-simple selector makes the view unanalyzable — NO write mapper source is
                // emitted (no pairs extracted) and a VISTA0033 fallback warning is raised instead.
                if (result.HasGeneratedSourceContaining("GenView_VistaWriteMapper"))
                {
                    return false;
                }

                return result.Diagnostics.Any(static d =>
                    d.Id == "VISTA0033" && d.Severity == DiagnosticSeverity.Warning);
            },
            iter: 100,
            // On failure, print the exact view source that broke the property for a reproducible example.
            print: RenderViewSource);
    }

    /// <summary>
    /// Renders a compilable typed Style B writable view whose CRUD facet declares the given MapWritable
    /// chain. The entity (<c>Source</c>) and CRUD contract (<c>WriteCrud</c>) each expose <c>int</c>
    /// members <c>E0..E{n-1}</c> / <c>C0..C{n-1}</c> (all non-key, non-token scalars, so every simple
    /// mapping is a "safe" assignment the emitter keeps) plus a <c>Nested</c> member for the nested
    /// non-simple selector variant.
    /// </summary>
    private static string RenderViewSource(MappingSpec[] chain)
    {
        var sb = new StringBuilder();
        sb.Append("namespace App\n{\n");

        sb.Append("    public sealed class Nested { public int Value { get; set; } }\n\n");

        // Entity (TEntity for CrudOn<Source>) — carries the E{i} targets and a Nested navigation.
        sb.Append("    public sealed class Source\n    {\n");
        for (var i = 0; i < chain.Length; i++)
        {
            sb.Append($"        public int E{i} {{ get; set; }}\n");
        }

        sb.Append("        public Nested Nested { get; set; } = new Nested();\n");
        sb.Append("    }\n\n");

        // TQuery row — minimal; the write mapper does not read it.
        sb.Append("    public sealed class Row { }\n\n");

        // TCrud write contract — carries the C{i} sources and a Nested navigation.
        sb.Append("    public sealed class WriteCrud\n    {\n");
        for (var i = 0; i < chain.Length; i++)
        {
            sb.Append($"        public int C{i} {{ get; set; }}\n");
        }

        sb.Append("        public Nested Nested { get; set; } = new Nested();\n");
        sb.Append("    }\n\n");

        // The writable view: CrudOn<Source>() then one MapWritable per mapping, in declaration order.
        sb.Append("    public partial class GenView : a2n.Vista.Authoring.View<Row, WriteCrud>\n    {\n");
        sb.Append("        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row, WriteCrud> builder)\n");
        sb.Append("            => builder\n");
        sb.Append("                .CrudOn<Source>()\n");

        for (var i = 0; i < chain.Length; i++)
        {
            var from = RenderSelector(chain[i].From, "c", $"C{i}");
            var to = RenderSelector(chain[i].To, "e", $"E{i}");
            sb.Append($"                .MapWritable({from}, {to})\n");
        }

        sb.Append("                ;\n");
        sb.Append("    }\n}\n");

        return sb.ToString();
    }

    /// <summary>Renders one selector lambda from its spec, over parameter <paramref name="param"/>
    /// selecting member <paramref name="member"/>.</summary>
    private static string RenderSelector(SelectorSpec spec, string param, string member)
    {
        var forms = spec.Simple ? SimpleForms : NonSimpleForms;
        return forms[spec.Variant % forms.Length](param, member);
    }
}
