// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the Phase 3 (M9, D121/D122) WriteMapperGenerator's emitted write-mapper source
// (task 6.3).
//
// Feature: source-generator-write-mapper, Property 2: For any analyzable typed Style B writable view, the
// emitted write-mapper source contains no System.Activator.CreateInstance invocation, no
// System.Reflection.PropertyInfo member access (no GetValue/SetValue), and no System.Linq.Expressions
// expression-tree Compile call.
//
// Validates: Requirements 3.2, 3.3, 3.4
//
// Strategy: the emitter turns an analyzable typed Style B writable view into a reflection-free
// <View>_VistaWriteMapper.g.cs (cast + direct member assignments). A CsCheck generator produces a random
// ANALYZABLE view — 1..6 MapWritable mappings, each onto a randomly-typed scalar entity member (int,
// string, long, double, decimal, bool, System.Guid, System.DateTime, byte[], and nullable int/double), all
// simple member selectors, all non-key/non-token scalar targets — so the "safe subset" equals the full
// whitelist and the emitter always produces a mapper. The generated shapes therefore span the full range
// of scalar assignments the emitter must render. The view source is driven through the WriteMapperGenerator
// via CSharpGeneratorDriver (WriteMapperGeneratorTestHarness), and the emitted <View>_VistaWriteMapper
// source TEXT is asserted to contain NONE of the reflection markers:
//
//   * System.Activator.CreateInstance      (R3.2 — no reflective instantiation)
//   * System.Reflection.PropertyInfo / .GetValue( / .SetValue(   (R3.3 — no PropertyInfo member access)
//   * System.Linq.Expressions / .Compile(  (R3.4 — no expression-tree compilation)
//
// Minimum 100 generated cases (CsCheck default iter = 100). PBT library: CsCheck (imperative Sample, pairs
// cleanly with TUnit [Test]). Only the generated source TEXT is inspected; no generated [ModuleInitializer]
// is executed.

using System;
using System.Linq;
using System.Text;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

public sealed class ReflectionFreeEmissionPropertyTests
{
    // A palette of scalar member types (value types, string, byte[], and nullable value types). Every one
    // is a Scalar_Member (value type with Nullable<T> unwrapped, string, or byte[]), so a mapping onto it
    // is a "safe" assignment the emitter keeps — never a VISTA0031 non-scalar target. Both the entity and
    // the CRUD contract expose a member of the same type per mapping so MapWritable<TProp> binds.
    private static readonly string[] ScalarTypes =
    {
        "int",
        "string",
        "long",
        "double",
        "decimal",
        "bool",
        "System.Guid",
        "System.DateTime",
        "byte[]",
        "int?",
        "double?",
    };

    /// <summary>One mapping: the scalar type shared by its source (TCrud) and target (TEntity) member.</summary>
    private sealed record MappingSpec(int TypeIndex);

    private static readonly Gen<MappingSpec> GenMapping =
        Gen.Select(Gen.Int[0, ScalarTypes.Length - 1], static typeIndex => new MappingSpec(typeIndex));

    // An analyzable chain of 1..6 mappings (at least one, so it never collapses into the zero-mapping
    // VISTA0030 branch). A random class-name suffix varies the input the generator sees.
    private sealed record ViewInput(MappingSpec[] Mappings, int Suffix);

    private static readonly Gen<ViewInput> GenViewInput =
        from count in Gen.Int[1, 6]
        from mappings in GenMapping.Array[count]
        from suffix in Gen.Int[0, 999]
        select new ViewInput(mappings, suffix);

    [Test]
    public void Emitted_Write_Mapper_Source_Is_Reflection_Free()
    {
        // Feature: source-generator-write-mapper, Property 2: For any analyzable typed Style B writable
        // view, the emitted write-mapper source contains no System.Activator.CreateInstance invocation, no
        // System.Reflection.PropertyInfo member access (no GetValue/SetValue), and no
        // System.Linq.Expressions expression-tree Compile call.
        GenViewInput.Sample(
            input =>
            {
                var source = RenderViewSource(input);
                var result = WriteMapperGeneratorTestHarness.Run(source);

                // The chain is analyzable and every target is a safe scalar, so the emitter always produces
                // the view's write-mapper source.
                var hintFragment = "GenView" + input.Suffix + "_VistaWriteMapper";
                if (!result.HasGeneratedSourceContaining(hintFragment))
                {
                    return false;
                }

                var generated = result.GeneratedSourceContaining(hintFragment);

                // R3.2: no reflective instantiation.
                if (generated.Contains("Activator.CreateInstance", StringComparison.Ordinal))
                {
                    return false;
                }

                // R3.3: no PropertyInfo member access (neither the type reference nor Get/SetValue).
                if (generated.Contains("PropertyInfo", StringComparison.Ordinal)
                    || generated.Contains(".GetValue(", StringComparison.Ordinal)
                    || generated.Contains(".SetValue(", StringComparison.Ordinal))
                {
                    return false;
                }

                // R3.4: no expression-tree compilation.
                if (generated.Contains("System.Linq.Expressions", StringComparison.Ordinal)
                    || generated.Contains(".Compile(", StringComparison.Ordinal))
                {
                    return false;
                }

                return true;
            },
            iter: 100,
            // On failure, print the exact view source that broke the property for a reproducible example.
            print: RenderViewSource);
    }

    /// <summary>
    /// Renders a compilable typed Style B writable view whose CRUD facet declares the given analyzable
    /// MapWritable chain. The entity (<c>Source</c>) and CRUD contract (<c>WriteCrud</c>) each expose a
    /// member <c>E{i}</c> / <c>C{i}</c> of the mapping's scalar type (all non-key, non-token scalars, so
    /// every simple mapping is a "safe" assignment the emitter keeps).
    /// </summary>
    private static string RenderViewSource(ViewInput input)
    {
        var suffix = input.Suffix;
        var chain = input.Mappings;

        var sb = new StringBuilder();
        sb.Append("namespace App\n{\n");

        // Entity (TEntity for CrudOn<Source>) — carries the E{i} scalar targets.
        sb.Append($"    public sealed class Source{suffix}\n    {{\n");
        for (var i = 0; i < chain.Length; i++)
        {
            sb.Append($"        public {ScalarTypes[chain[i].TypeIndex]} E{i} {{ get; set; }}\n");
        }

        sb.Append("    }\n\n");

        // TQuery row — minimal; the write mapper does not read it.
        sb.Append($"    public sealed class Row{suffix} {{ }}\n\n");

        // TCrud write contract — carries the C{i} scalar sources.
        sb.Append($"    public sealed class WriteCrud{suffix}\n    {{\n");
        for (var i = 0; i < chain.Length; i++)
        {
            sb.Append($"        public {ScalarTypes[chain[i].TypeIndex]} C{i} {{ get; set; }}\n");
        }

        sb.Append("    }\n\n");

        // The writable view: CrudOn<Source>() then one MapWritable per mapping, in declaration order.
        sb.Append($"    public partial class GenView{suffix} : a2n.Vista.Authoring.View<Row{suffix}, WriteCrud{suffix}>\n    {{\n");
        sb.Append($"        public void Configure(a2n.Vista.Authoring.IViewBuilder<Row{suffix}, WriteCrud{suffix}> builder)\n");
        sb.Append("            => builder\n");
        sb.Append($"                .CrudOn<Source{suffix}>()\n");

        for (var i = 0; i < chain.Length; i++)
        {
            sb.Append($"                .MapWritable(c => c.C{i}, e => e.E{i})\n");
        }

        sb.Append("                ;\n");
        sb.Append("    }\n}\n");

        return sb.ToString();
    }
}
