// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Packaging / layering assertions for the Phase 5 per-view JsonTypeInfo generator
// (spec source-generator-json-typeinfo, task 9.3; Decision Log D125/D126; Requirements R7.1). These are
// the ViewJsonContextGenerator analogue of ViewInvokerGeneratorPackagingTests /
// WriteMapperGeneratorPackagingTests — anchored on the GENERATOR assembly rather than the runtime
// assemblies, because the generator's layering rules are about how the Roslyn component is packaged and
// what it may reference.
//
// This half of task 9.3 lives here (SourceGenerators.Tests) rather than in a2n.Vista.Tests because the
// runtime test project deliberately takes NO reference to the generator or to Microsoft.CodeAnalysis, so
// it cannot load the netstandard2.0 Roslyn component to inspect its IIncrementalGenerator interface,
// [Generator] attribute, or reference set. The SourceGenerators.Tests project references the generator
// BOTH as an analyzer and as a normal assembly (ReferenceOutputAssembly=true, see its .csproj), so
// `typeof(ViewJsonContextGenerator).Assembly` is the generator's OWN netstandard2.0 assembly and its
// GetReferencedAssemblies() reports exactly the compile-time references the compiler emitted into it. The
// Core-reference / consumer-assembly / drain-placement / cast-guard half of task 9.3 is asserted in
// a2n.Vista.Tests/JsonContextLayeringGuardTests.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.SourceGenerators.Tests;

/// <summary>
/// Generator packaging / layering guard for the Phase 5 <see cref="ViewJsonContextGenerator"/> (design.md
/// §"The central design axis — layering (D48)", §"Layering, packaging, and incremental behavior"; R7.1).
/// Asserts the generator is a netstandard2.0 <see cref="IIncrementalGenerator"/> registered for C# that
/// references no a2n.Vista assembly (it recognizes Vista base types — <c>View&lt;TQuery&gt;</c> /
/// <c>View&lt;TQuery, TCrud&gt;</c>, the Vista read envelopes, and the Core
/// <c>GeneratedJsonContextStore</c> its emitted <c>[ModuleInitializer]</c> targets — by fully-qualified
/// name only, D48).
/// </summary>
public sealed class ViewJsonContextGeneratorPackagingTests
{
    private const string VistaAssemblyPrefix = "a2n.Vista";
    private const string RoslynAssemblyPrefix = "Microsoft.CodeAnalysis";
    private const string NetStandardFrameworkName = ".NETStandard,Version=v2.0";

    // The generator's own assembly. SourceGenerators.Tests references the generator BOTH as an analyzer
    // and as a normal assembly (ReferenceOutputAssembly=true), so this resolves to the real, shipped
    // netstandard2.0 component under test.
    private static readonly Assembly GeneratorAssembly = typeof(ViewJsonContextGenerator).Assembly;

    /// <summary>
    /// R7.1: <see cref="ViewJsonContextGenerator"/> is an <see cref="IIncrementalGenerator"/> and is
    /// registered for C# via <c>[Generator(LanguageNames.CSharp)]</c> — a real incremental Roslyn source
    /// generator, not a legacy <c>ISourceGenerator</c> or an analyzer.
    /// </summary>
    [Test]
    public async Task ViewJsonContextGenerator_Is_A_CSharp_Incremental_Generator()
    {
        await Assert.That(typeof(IIncrementalGenerator).IsAssignableFrom(typeof(ViewJsonContextGenerator)))
            .IsTrue();

        var generatorAttribute = typeof(ViewJsonContextGenerator).GetCustomAttribute<GeneratorAttribute>();

        await Assert.That(generatorAttribute).IsNotNull();
        await Assert.That(generatorAttribute!.Languages).Contains(LanguageNames.CSharp);
    }

    /// <summary>
    /// R7.1: the generator assembly targets <c>netstandard2.0</c> — the mandated generator TFM so the
    /// single compiled component loads uniformly into the Roslyn analyzer host for every consumer
    /// regardless of the consumer's own target framework (net8/9/10).
    /// </summary>
    [Test]
    public async Task Generator_Assembly_Targets_NetStandard2_0()
    {
        var targetFramework = GeneratorAssembly.GetCustomAttribute<TargetFrameworkAttribute>();

        await Assert.That(targetFramework).IsNotNull();
        await Assert.That(targetFramework!.FrameworkName).IsEqualTo(NetStandardFrameworkName);
    }

    /// <summary>
    /// R7.1: the generator declares no direct reference to ANY a2n.Vista assembly. It recognizes Vista
    /// types (<c>View&lt;TQuery&gt;</c>/<c>View&lt;TQuery, TCrud&gt;</c>, the
    /// <c>ViewListResult&lt;T&gt;</c>/<c>PagedResult&lt;T&gt;</c> read envelopes, and the Core
    /// <c>GeneratedJsonContextStore</c> its emitted registration calls into) solely by fully-qualified
    /// name (D48), so no compiled Vista type is referenced — keeping the generator a self-contained
    /// Roslyn component consistent with Phases 1/2/3/4.
    /// </summary>
    [Test]
    public async Task Generator_Assembly_References_No_Vista_Assembly()
    {
        var referencesVista = ReferencedAssemblyNames(GeneratorAssembly)
            .Any(name => name.StartsWith(VistaAssemblyPrefix, StringComparison.Ordinal));

        await Assert.That(referencesVista).IsFalse();
    }

    /// <summary>
    /// Positive sanity: the generator DOES reference the Roslyn compiler platform
    /// (<c>Microsoft.CodeAnalysis*</c>). This proves the "no Vista reference" assertion above is a real
    /// layering property over a populated reference set, not an artifact of an assembly that references
    /// almost nothing.
    /// </summary>
    [Test]
    public async Task Generator_Assembly_References_The_Roslyn_Platform()
    {
        var referencesRoslyn = ReferencedAssemblyNames(GeneratorAssembly)
            .Any(name => name.StartsWith(RoslynAssemblyPrefix, StringComparison.Ordinal));

        await Assert.That(referencesRoslyn).IsTrue();
    }

    /// <summary>
    /// Projects the direct referenced-assembly simple names of <paramref name="assembly"/>
    /// (<see cref="Assembly.GetReferencedAssemblies()"/> → <see cref="AssemblyName.Name"/>), dropping any
    /// null names defensively. Mirrors the helper in <c>ViewInvokerGeneratorPackagingTests</c>.
    /// </summary>
    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)!;
}
