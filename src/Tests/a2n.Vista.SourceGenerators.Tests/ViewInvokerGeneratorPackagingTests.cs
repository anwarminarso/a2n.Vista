// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Packaging / layering assertions for the Phase 4 HTTP-surface dispatch generator
// (spec source-generator-http-surface, task 11.3; Requirements R1.4, R7.1). These are the
// ViewInvokerGenerator analogue of WriteMapperGeneratorPackagingTests — anchored on the GENERATOR
// assembly rather than the runtime assemblies, because the generator's layering rules are about how
// the Roslyn component is packaged and what it may reference.
//
// This half of task 11.3 lives here (SourceGenerators.Tests) rather than in a2n.Vista.Tests because the
// runtime test project deliberately takes NO reference to the generator or to Microsoft.CodeAnalysis,
// so it cannot load the netstandard2.0 Roslyn component to inspect its IIncrementalGenerator interface,
// [Generator] attribute, or reference set. The SourceGenerators.Tests project references the generator
// BOTH as an analyzer and as a normal assembly (ReferenceOutputAssembly=true, see its .csproj), so
// `typeof(ViewInvokerGenerator).Assembly` is the generator's OWN netstandard2.0 assembly and its
// GetReferencedAssemblies() reports exactly the compile-time references the compiler emitted into it.
// The runtime-assembly + RUC-confinement half of task 11.3 is asserted in
// a2n.Vista.Tests/HttpSurfaceLayeringGuardTests.

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
/// Generator packaging / layering guard for the Phase 4 <see cref="ViewInvokerGenerator"/> (design.md
/// §"The central design axis — layering (D48)", §"Layering, packaging, and incremental behavior").
/// Asserts the generator is a netstandard2.0 <see cref="IIncrementalGenerator"/> registered for C# that
/// references no a2n.Vista assembly (it recognizes Vista base types by fully-qualified name only — D48,
/// R1.4, R7.1).
/// </summary>
public sealed class ViewInvokerGeneratorPackagingTests
{
    private const string VistaAssemblyPrefix = "a2n.Vista";
    private const string RoslynAssemblyPrefix = "Microsoft.CodeAnalysis";
    private const string NetStandardFrameworkName = ".NETStandard,Version=v2.0";

    // The generator's own assembly. SourceGenerators.Tests references the generator BOTH as an analyzer
    // and as a normal assembly (ReferenceOutputAssembly=true), so this resolves to the real, shipped
    // netstandard2.0 component under test.
    private static readonly Assembly GeneratorAssembly = typeof(ViewInvokerGenerator).Assembly;

    /// <summary>
    /// R7.1: <see cref="ViewInvokerGenerator"/> is an <see cref="IIncrementalGenerator"/> and is
    /// registered for C# via <c>[Generator(LanguageNames.CSharp)]</c> — a real incremental Roslyn source
    /// generator, not a legacy <c>ISourceGenerator</c> or an analyzer.
    /// </summary>
    [Test]
    public async Task ViewInvokerGenerator_Is_A_CSharp_Incremental_Generator()
    {
        await Assert.That(typeof(IIncrementalGenerator).IsAssignableFrom(typeof(ViewInvokerGenerator)))
            .IsTrue();

        var generatorAttribute = typeof(ViewInvokerGenerator).GetCustomAttribute<GeneratorAttribute>();

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
    /// R1.4 / R7.1: the generator declares no direct reference to ANY a2n.Vista assembly. It recognizes
    /// Vista types (<c>View&lt;TQuery&gt;</c>/<c>View&lt;TQuery, TCrud&gt;</c>, the Core
    /// <c>IViewInvoker</c>/<c>ViewInvokerStore</c> it emits calls against) solely by fully-qualified name
    /// (D48), so no compiled Vista type is referenced — keeping the generator a self-contained Roslyn
    /// component consistent with Phases 1/2/3.
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
    /// null names defensively. Mirrors the helper in <c>WriteMapperGeneratorPackagingTests</c>.
    /// </summary>
    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)!;
}
