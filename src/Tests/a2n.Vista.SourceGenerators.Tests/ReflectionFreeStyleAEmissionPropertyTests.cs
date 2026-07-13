// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Style A coverage generator's (StyleAShapeGenerator, the fifth phase — M9,
// D129/D130, style-a-coverage) emitted shape-driven artifacts (tasks 5.1/5.2).
//
// Feature: style-a-coverage, Property 5: Generated source is reflection-free, attribute-free, and uses
// JsonMetadataServices.
//
// Validates: Requirements 2.1, 3.2, 7.3, 7.5
//
// Property 5 (design.md "Correctness Properties") is a single invariant over BOTH emitted artifacts of a
// covered Style A view:
//
//   * The per-view CONTEXT source (<...>_VistaJsonContext.g.cs) contains NO [JsonSerializable] attribute,
//     NO System.Reflection access, NO Activator.CreateInstance, NO System.Linq.Expressions Compile, and NO
//     MakeGenericMethod; it builds every JsonTypeInfo via JsonMetadataServices; and it references only the
//     template assembly's reachable types (Core + BCL/shared-framework System.Text.Json), with NO ASP.NET
//     Core type (R3.2, R7.3, R7.5).
//   * The export ACCESSOR source (<...>_VistaAccessors.g.cs) uses only cast + member access (never
//     reflection) and is registered into the existing ViewAccessorRegistry (R2.1, R7.3, R7.5).
//
// The reused shared emitter (JsonContextEmitter, extracted verbatim from the D125 ViewJsonContextGenerator
// so the two phases emit byte-for-byte identical contexts) means the Style A context is the SAME
// reflection-free `file sealed IJsonTypeInfoResolver` the D125 phase emits — so this mirrors the sibling
// D125 property test (ReflectionFreeJsonContextEmissionPropertyTests) in approach and assertions, but drives
// the Style A generator over AddView CALL SITES (not View<...> class declarations) and additionally asserts
// the export accessor artifact the Style A phase reuses from Phase 1 (D117).
//
// A NECESSARY DIFFERENCE FROM THE LITERAL "no PropertyInfo" WORDING (matching the sibling D125 test):
//   Property 5's text says "no System.Reflection.PropertyInfo access". The emitted context legitimately
//   NAMES System.Text.Json's JsonPropertyInfo / JsonMetadataServices.CreatePropertyInfo<TMember> factories —
//   those are AOT-clean metadata factories, NOT reflection — so both contain the substring "PropertyInfo".
//   Checking the bare "PropertyInfo" substring on the CONTEXT would therefore FALSELY FAIL. The reflection
//   check on the context is keyed on the "System.Reflection" namespace (which is genuinely absent and which
//   is what "System.Reflection.PropertyInfo access" actually means). The ACCESSOR source names no
//   System.Text.Json surface at all, so there the stricter bare "PropertyInfo" check IS applied and holds.
//   The emitted enum leaf legitimately uses JsonStringEnumConverter<TEnum> (that is NOT reflection) — the
//   forbidden-substring set deliberately excludes it.
//
// STRATEGY: the property quantifies over the finite Style A COVERAGE matrix — { named read-only, named
// writable, anonymous-row + named TCrud } — each ALWAYS covered (a constant AddView name; a named/emittable
// TRow and/or a named/emittable TCrud), so there is always emitted source to assert on. Identifiers
// (namespace / template / view / row / crud) are randomized with distinct prefixes (never colliding, never
// a C# keyword), and — for the two named-row shapes — the read row carries 1..4 members drawn from the full
// Emittable_Shape palette (int / string / int? / enum / byte[] / int[] / IReadOnlyList<string>) so the
// emitter exercises the object, scalar, nullable, enum, byte[], and collection JsonMetadataServices arms.
// Each case renders a compilable Style A template, drives StyleAShapeGenerator via CSharpGeneratorDriver
// (the REUSED StyleAShapeGeneratorTestHarness, unmodified), reads the emitted sources off
// result.Results[0].GeneratedSources by hint name, and asserts the invariants above on the context (always
// emitted) and — for a named row — the accessor (emitted iff the row is nameable; absent for an anonymous
// row, which stays RUC by design, D96/D130). Only the emitted source TEXT is inspected; no generated
// [ModuleInitializer] is executed.
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample, pairs
// cleanly with TUnit [Test]).

using System;
using System.Linq;
using System.Text;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ReflectionFreeStyleAEmissionPropertyTests
{
    // ---- the finite Style A coverage matrix -----------------------------------------------------------

    /// <summary>A covered Style A view shape (every entry is always covered — see the class remarks).</summary>
    private enum Shape
    {
        /// <summary>Named <c>TRow</c>, no <c>WithCrud</c> → export accessors + read-DTO context.</summary>
        NamedReadOnly,

        /// <summary>Named <c>TRow</c> + named <c>TCrud</c> → accessors + read-DTO context + <c>TCrud</c> context.</summary>
        NamedWritable,

        /// <summary>
        /// Anonymous read <c>TRow</c> + named <c>TCrud</c> → <c>TCrud</c> context ONLY (the D96 asymmetry:
        /// the anonymous read row is unnameable in generated source, so it emits no accessor / read context).
        /// </summary>
        AnonymousRowNamedCrud,
    }

    // The full Emittable_Shape palette for a named read row (design "Data Models", inherited from D125): a
    // BCL scalar, string, a nullable value type, an enum, a byte[] scalar leaf, an array collection, and a
    // read-only-interface collection. Every entry is emittable, so a named row is always COVERED and the
    // emitter always produces a read-DTO context + accessor map. Index 3 (the enum) needs the enum type
    // declared alongside the row; the rest need no extra declarations.
    private const int EnumMemberIndex = 3;
    private const int MemberTypeCount = 7;

    /// <summary>One generated covered Style A view: its matrix coordinate, flags, identifiers, and row members.</summary>
    private sealed record StyleAInput(
        Shape Shape,
        bool InitOnlyMembers,
        string Namespace,
        string TemplateName,
        string ViewName,
        string RowName,
        string CrudName,
        int[] RowMemberTypeIndices);

    // A valid C# identifier core: an uppercase leading letter followed by 2–6 lowercase letters, so it is
    // never a C# keyword (keywords are all lowercase) and always parses.
    private static readonly Gen<string> GenIdentifierCore =
        from first in Gen.Char['a', 'z']
        from rest in Gen.Char['a', 'z'].Array[2, 6]
        select char.ToUpperInvariant(first) + new string(rest);

    // 1..4 emittable row members, each an index into the Emittable_Shape palette.
    private static readonly Gen<int[]> GenRowMemberTypeIndices =
        Gen.Int[0, MemberTypeCount - 1].Array[1, 4];

    // Distinct prefixes ("Ns"/"Tmpl"/"view_"/"Row"/"Crud") guarantee the names never collide even when their
    // random cores coincide, so every rendered source compiles with distinct names while still varying each.
    private static readonly Gen<StyleAInput> GenStyleAInput =
        from shape in Gen.Int[0, 2]
        from initOnly in Gen.Bool
        from ns in GenIdentifierCore
        from tmpl in GenIdentifierCore
        from view in GenIdentifierCore
        from row in GenIdentifierCore
        from crud in GenIdentifierCore
        from members in GenRowMemberTypeIndices
        select new StyleAInput(
            (Shape)shape,
            initOnly,
            "Ns" + ns,
            "Tmpl" + tmpl,
            "view_" + view.ToLowerInvariant(),
            "Row" + row,
            "Crud" + crud,
            members);

    [Test]
    public void Generated_StyleA_Source_Is_Reflection_Free_Attribute_Free_And_Uses_JsonMetadataServices()
    {
        // Feature: style-a-coverage, Property 5: Generated source is reflection-free, attribute-free, and
        // uses JsonMetadataServices.
        GenStyleAInput.Sample(
            input =>
            {
                var source = RenderStyleAViewSource(input);
                var result = StyleAShapeGeneratorTestHarness.Run(source);

                // The emitted source files (task 5.1/5.2): each GeneratedSourceResult carries .HintName and
                // .SourceText. Exactly one generator runs, so Results[0] holds this generator's output.
                var generated = result.Results[0].GeneratedSources;

                // --- The per-view context (<...>_VistaJsonContext.g.cs) is ALWAYS emitted for a covered
                //     view (a named+emittable read row and/or a named+emittable TCrud). ---
                var contexts = generated
                    .Where(s => s.HintName.EndsWith("_VistaJsonContext.g.cs", StringComparison.Ordinal))
                    .ToArray();
                if (contexts.Length != 1)
                {
                    return false;
                }

                if (!ContextSourceIsReflectionFreeAndUsesJsonMetadataServices(contexts[0].SourceText.ToString()))
                {
                    return false;
                }

                // --- The export accessor map (<...>_VistaAccessors.g.cs) is emitted IFF the read row is a
                //     Named_Type. An anonymous read row is unnameable in generated source, so it emits no
                //     accessor and its read path stays RUC by design (D96/D130). ---
                var accessors = generated
                    .Where(s => s.HintName.EndsWith("_VistaAccessors.g.cs", StringComparison.Ordinal))
                    .ToArray();

                var hasNamedRow = input.Shape is Shape.NamedReadOnly or Shape.NamedWritable;
                if (hasNamedRow)
                {
                    if (accessors.Length != 1)
                    {
                        return false;
                    }

                    if (!AccessorSourceIsReflectionFreeCastOnly(accessors[0].SourceText.ToString()))
                    {
                        return false;
                    }
                }
                else if (accessors.Length != 0)
                {
                    // An anonymous read row must NOT produce an accessor map.
                    return false;
                }

                return true;
            },
            iter: 100,
            // On failure, print the exact view source that broke the property for a reproducible example.
            print: RenderStyleAViewSource);
    }

    // ---- artifact invariants --------------------------------------------------------------------------

    /// <summary>
    /// The per-view <c>IJsonTypeInfoResolver</c> context (<c>&lt;...&gt;_VistaJsonContext.g.cs</c>) is
    /// attribute-free, reflection-free (Core + BCL/shared-framework System.Text.Json only, no ASP.NET Core),
    /// and builds every <c>JsonTypeInfo</c> by hand via <c>JsonMetadataServices</c> (R3.2, R7.3, R7.5).
    /// </summary>
    private static bool ContextSourceIsReflectionFreeAndUsesJsonMetadataServices(string source)
    {
        // ATTRIBUTE-FREE (the generator-of-generator constraint): no [JsonSerializable] attribute route.
        // ("JsonSerializable" is NOT a substring of "JsonSerializerOptions", so this keys on the attribute.)
        if (source.Contains("JsonSerializable", StringComparison.Ordinal))
        {
            return false;
        }

        // REFLECTION-FREE. Keyed on the System.Reflection NAMESPACE, NOT the bare "PropertyInfo" substring:
        // the emitted System.Text.Json JsonPropertyInfo / CreatePropertyInfo<TMember> factory names contain
        // "PropertyInfo" but are AOT-clean metadata factories, not reflection (see the class remarks).
        if (source.Contains("System.Reflection", StringComparison.Ordinal))
        {
            return false;
        }

        // No reflective instantiation — the object creators are `new T(...)`.
        if (source.Contains("Activator.CreateInstance", StringComparison.Ordinal))
        {
            return false;
        }

        // No expression-tree compilation.
        if (source.Contains("Expression.Compile", StringComparison.Ordinal)
            || source.Contains(".Compile(", StringComparison.Ordinal))
        {
            return false;
        }

        // No open-generic closing over a runtime Type.
        if (source.Contains("MakeGenericMethod", StringComparison.Ordinal))
        {
            return false;
        }

        // Core + BCL/STJ only — no ASP.NET Core type in the template's assembly (R7.5).
        if (source.Contains("Microsoft.AspNetCore", StringComparison.Ordinal))
        {
            return false;
        }

        // Builds every JsonTypeInfo by hand via JsonMetadataServices (not by attribute), with the AOT-clean
        // `file sealed` IJsonTypeInfoResolver shape and exactly one before-DI [ModuleInitializer].
        if (!source.Contains("JsonMetadataServices.CreateObjectInfo<", StringComparison.Ordinal))
        {
            return false;
        }

        if (!source.Contains("IJsonTypeInfoResolver", StringComparison.Ordinal))
        {
            return false;
        }

        if (!source.Contains("file sealed class", StringComparison.Ordinal))
        {
            return false;
        }

        return source.Contains(
            "[global::System.Runtime.CompilerServices.ModuleInitializer]", StringComparison.Ordinal);
    }

    /// <summary>
    /// The export accessor map (<c>&lt;...&gt;_VistaAccessors.g.cs</c>) uses ONLY cast + member access
    /// (never reflection) and registers into the existing <c>ViewAccessorRegistry</c> (R2.1, R7.3, R7.5).
    /// The accessor source names no System.Text.Json surface, so — unlike the context — the stricter bare
    /// <c>PropertyInfo</c> check is applied here and holds.
    /// </summary>
    private static bool AccessorSourceIsReflectionFreeCastOnly(string source)
    {
        // REFLECTION-FREE: the accessor names no reflection API at all, so even the bare "PropertyInfo"
        // substring must be absent (a cast + member read never touches System.Reflection).
        if (source.Contains("PropertyInfo", StringComparison.Ordinal)
            || source.Contains("System.Reflection", StringComparison.Ordinal))
        {
            return false;
        }

        if (source.Contains("Activator.CreateInstance", StringComparison.Ordinal))
        {
            return false;
        }

        if (source.Contains("Expression.Compile", StringComparison.Ordinal)
            || source.Contains(".Compile(", StringComparison.Ordinal))
        {
            return false;
        }

        if (source.Contains("MakeGenericMethod", StringComparison.Ordinal))
        {
            return false;
        }

        // Core + BCL only — no ASP.NET Core, no [JsonSerializable] (the accessor has no JSON surface).
        if (source.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)
            || source.Contains("JsonSerializable", StringComparison.Ordinal))
        {
            return false;
        }

        // Cast + member access only: `["Member"] = static row => ((global::Ns.Row)row).Member,`.
        if (!source.Contains("= static row => ((", StringComparison.Ordinal)
            || !source.Contains(")row).", StringComparison.Ordinal))
        {
            return false;
        }

        // A `file static` accessor class registered into the EXISTING Core store via one [ModuleInitializer].
        if (!source.Contains("file static class", StringComparison.Ordinal))
        {
            return false;
        }

        if (!source.Contains(
                "global::a2n.Vista.Metadata.ViewAccessorRegistry.Register(", StringComparison.Ordinal))
        {
            return false;
        }

        return source.Contains(
            "[global::System.Runtime.CompilerServices.ModuleInitializer]", StringComparison.Ordinal);
    }

    // ---- Style A source rendering ---------------------------------------------------------------------

    /// <summary>
    /// Renders a compilable Style A template with a single covered <c>AddView</c> call site of the requested
    /// matrix shape: a named read row (with 1..4 emittable members from the palette — plain writable or
    /// init-only) for the two named shapes, or an anonymous projection for the asymmetry shape; and a named
    /// <c>TCrud</c> (via <c>.WithCrud&lt;TCrud, TEntity&gt;()</c>) for the two writable shapes. All DTO
    /// members are emittable, and the <c>AddView</c> name is a constant string literal, so the view is always
    /// covered.
    /// </summary>
    private static string RenderStyleAViewSource(StyleAInput input)
    {
        var accessor = input.InitOnlyMembers ? "{ get; init; }" : "{ get; set; }";
        var hasNamedRow = input.Shape is Shape.NamedReadOnly or Shape.NamedWritable;
        var isWritable = input.Shape is Shape.NamedWritable or Shape.AnonymousRowNamedCrud;

        var sb = new StringBuilder();
        sb.Append("using System.Linq;\n\n");
        sb.Append("namespace ").Append(input.Namespace).Append("\n{\n");

        // Named read row (TRow) — 1..4 emittable members M0..M{n-1}, plus the enum type when any member uses
        // it. Omitted entirely for the anonymous shape (its row is projected inline and unnameable).
        if (hasNamedRow)
        {
            if (input.RowMemberTypeIndices.Any(i => i == EnumMemberIndex))
            {
                sb.Append("    public enum ").Append(input.RowName).Append("Kind { Alpha, Beta }\n\n");
            }

            sb.Append("    public sealed class ").Append(input.RowName).Append("\n    {\n");
            for (var i = 0; i < input.RowMemberTypeIndices.Length; i++)
            {
                sb.Append("        public ")
                  .Append(MemberTypeExpression(input.RowMemberTypeIndices[i], input.RowName))
                  .Append(" M").Append(i).Append(' ').Append(accessor).Append('\n');
            }

            sb.Append("    }\n\n");
        }

        // Named write model (TCrud) + its target entity — a couple of emittable members so the writable view
        // is covered on its write side (independently of the read row being named or anonymous, R4.2).
        if (isWritable)
        {
            sb.Append("    public sealed class ").Append(input.CrudName).Append("\n    {\n");
            sb.Append("        public string Label ").Append(accessor).Append('\n');
            sb.Append("        public int? Score ").Append(accessor).Append('\n');
            sb.Append("    }\n\n");

            sb.Append("    public sealed class ").Append(input.CrudName).Append("Entity\n    {\n");
            sb.Append("        public int Id { get; set; }\n");
            sb.Append("        public string Label { get; set; }\n");
            sb.Append("        public int? Score { get; set; }\n");
            sb.Append("    }\n\n");
        }

        sb.Append("    public class ").Append(input.TemplateName).Append('\n');
        sb.Append("        : a2n.Vista.Authoring.ViewTemplate<a2n.Vista.TestFixtures.TestDbContext>\n");
        sb.Append("    {\n");
        sb.Append("        protected internal override void Configure(\n");
        sb.Append("            a2n.Vista.Authoring.IViewTemplateBuilder<a2n.Vista.TestFixtures.TestDbContext> views)\n");
        sb.Append("        {\n");

        // Explicit <TRow> for a named row; inferred (anonymous) otherwise. The projection produces the
        // requested row kind; System.Linq.AsQueryable gives the IQueryable<TRow> AddView expects.
        var typeArg = hasNamedRow ? "<" + input.RowName + ">" : string.Empty;
        var projection = hasNamedRow
            ? "new " + input.RowName + "[0].AsQueryable()"
            : "new[] { new { Id = 1, Label = \"x\" } }.AsQueryable()";
        var withCrud = isWritable
            ? ".WithCrud<" + input.CrudName + ", " + input.CrudName + "Entity>()"
            : string.Empty;

        // A constant string literal name (keyable → covered) so artifacts can be keyed statically.
        sb.Append("            views.AddView").Append(typeArg).Append("(\"").Append(input.ViewName)
          .Append("\", (db, sp) => ").Append(projection).Append(')').Append(withCrud).Append(";\n");

        sb.Append("        }\n");
        sb.Append("    }\n");
        sb.Append("}\n");

        return sb.ToString();
    }

    /// <summary>
    /// The C# type expression for a palette member index (the enum member is expressed relative to the row
    /// name so the declared <c>&lt;Row&gt;Kind</c> enum is referenced). Every entry is an Emittable_Shape:
    /// a scalar, string, a nullable scalar, an enum, a <c>byte[]</c> scalar leaf, an array collection, and a
    /// read-only-interface collection.
    /// </summary>
    private static string MemberTypeExpression(int index, string rowName)
        => index switch
        {
            0 => "int",
            1 => "string",
            2 => "int?",
            EnumMemberIndex => rowName + "Kind",
            4 => "byte[]",
            5 => "int[]",
            _ => "global::System.Collections.Generic.IReadOnlyList<string>",
        };
}
