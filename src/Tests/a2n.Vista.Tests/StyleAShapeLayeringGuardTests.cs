// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Style A (anonymous) coverage packaging / layering assertions (spec style-a-coverage, task 9.3;
// Decision Log D129/D130, D48; Requirements 5.4, 7.1, 7.5). This is the Style A analogue of the runtime
// a2n.Vista.Tests/LayeringGuardTests, HttpSurfaceLayeringGuardTests, WriteLayeringGuardTests, and
// JsonContextLayeringGuardTests — anchored on the surface this phase touches: the two EXISTING Core stores
// the Style A generator reuses (ViewAccessorRegistry, D117; GeneratedJsonContextStore, D125 — NO new store
// is added), the AspNetCore serialization seam that drains the JSON store (VistaJson, D126, unchanged), and
// the generator-consumer TEMPLATE assembly (a2n.Vista.GeneratorStyleASample) the covered Style A artifacts
// are emitted into.
//
// The generator-side half of task 9.3 (the StyleAShapeGenerator is a netstandard2.0 IIncrementalGenerator
// that references no a2n.Vista project — R7.1) is asserted in the SourceGenerators.Tests project
// (StyleAShapeGeneratorPackagingTests), because a2n.Vista.Tests deliberately takes no reference to the
// generator or to Microsoft.CodeAnalysis and therefore cannot load the netstandard2.0 Roslyn component to
// inspect it. This file covers the runtime-assembly half that a2n.Vista.Tests CAN observe: the Core "no new
// store / no STJ-EF-ASP.NET dependency" invariant (R5.4/R7.5), the "generated artifacts emitted into the
// template's own assembly with no ASP.NET Core dependency" invariant (R7.5), and the "STJ-typed drain stays
// in a2n.Vista.AspNetCore" placement (R7.5).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.GeneratorStyleASample;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Style A coverage layering + packaging guard (design.md §"Non-negotiables honored" — "Core stays EF-free,
/// HTTP-free, STJ-free (D48)", "No new store, no new seam"; R5.4, R7.1, R7.5). Pins, via reflection over the
/// produced assemblies and by exercising the reused stores, the structural non-negotiables this phase must
/// preserve:
/// <list type="bullet">
/// <item>R5.4 / R7.5 — <c>a2n.Vista.Core</c>, carrying the two EXISTING stores this phase REUSES
/// (<see cref="ViewAccessorRegistry"/>, D117; <see cref="GeneratedJsonContextStore"/>, D125), still
/// references no <c>System.Text.Json</c>, EF Core, or ASP.NET Core assembly. The feature adds no new store,
/// so it introduces no new Core cross-reference — it only adds store ENTRIES keyed by Style A view names.</item>
/// <item>R7.5 — the covered Style A per-view context and accessor map are emitted into the TEMPLATE assembly
/// (<c>a2n.Vista.GeneratorStyleASample</c>), which carries no ASP.NET Core dependency (a Style A template is
/// authored against an EF <c>DbContext</c>, so it legitimately references the EF layer — only the ASP.NET
/// Core dependency is asserted absent, matching R7.5's wording).</item>
/// <item>R7.5 — the generated artifacts genuinely LAND in that template assembly: running its module
/// initializers populates the Core stores with the covered Style A entries keyed by the CONSTANT
/// <c>AddView</c> names (the D129 keying), proving the emission target is the template's own assembly.</item>
/// <item>R7.5 — the STJ-typed drain that names <c>IJsonTypeInfoResolver</c> to chain the JSON store lives in
/// <c>a2n.Vista.AspNetCore</c> (the seam, <see cref="VistaJson"/>), not in Core, and is unchanged by this
/// phase (D126).</item>
/// </list>
/// <para>
/// Nuance (identical to <see cref="LayeringGuardTests"/>): <see cref="Assembly.GetReferencedAssemblies()"/>
/// reports the DIRECT references the compiler emitted into an assembly's metadata; transitive and
/// compiler-trimmed-unused references are absent. That is exactly the compile-time dependency property we
/// assert. The positive checks (both stores genuinely live in Core; the template assembly genuinely
/// references Core + EF; the seam genuinely lives in AspNetCore and references STJ; the stores are genuinely
/// populated with Style A entries) prove the absences are real layering properties, not artifacts of empty
/// reference sets.
/// </para>
/// <para>
/// <b>Fixture reuse.</b> The covered Style A artifacts come from the referenced
/// <c>a2n.Vista.GeneratorStyleASample</c> assembly (task 8.1), whose <c>[ModuleInitializer]</c>s register the
/// generated per-view contexts into <see cref="GeneratedJsonContextStore"/> and the export accessor maps into
/// <see cref="ViewAccessorRegistry"/> at module load, keyed by the CONSTANT <c>AddView</c> name. No new
/// fixtures are declared here and no shared static (<see cref="VistaJson.Options"/>) is mutated, so there is
/// no first-wins store collision or global-state corruption with the sibling Style A seam/parity tasks.
/// </para>
/// </summary>
public sealed class StyleAShapeLayeringGuardTests
{
    private const string CoreAssemblyName = "a2n.Vista.Core";
    private const string AspNetCoreAssemblyName = "a2n.Vista.AspNetCore";
    private const string TemplateAssemblyName = "a2n.Vista.GeneratorStyleASample";

    private const string EfCorePrefix = "Microsoft.EntityFrameworkCore";
    private const string AspNetCorePrefix = "Microsoft.AspNetCore";
    private const string SystemTextJsonPrefix = "System.Text.Json";

    // The CONSTANT AddView names the covered Style A artifacts are keyed under (the D129 difference from
    // Style B's `new View().Name` keying). Lifted from the fixture template so the keys stay in lock-step.
    private const string CatalogItemsView = GeneratorStyleASampleViews.CatalogItemsViewName;   // "stylea-catalog-items"
    private const string AuditEntriesView = GeneratorStyleASampleViews.AuditEntriesViewName;   // "stylea-audit-entries"

    // Anchor types: typeof(...).Assembly loads the owning Vista assembly the test project references.
    // Core is anchored on BOTH reused stores (D117 accessor registry + D125 JSON context store), so the
    // reference-set assertions are specifically about the surface this phase touches — and prove the "no new
    // store" claim: both stores resolve to the SAME Core assembly, so no store moved or was added elsewhere.
    private static readonly Assembly CoreAssembly = typeof(ViewAccessorRegistry).Assembly;
    private static readonly Assembly AspNetCoreAssembly = typeof(VistaJson).Assembly;
    private static readonly Assembly TemplateAssembly = typeof(GeneratorStyleASampleViews).Assembly;

    static StyleAShapeLayeringGuardTests()
    {
        // Force the template fixture assembly's [ModuleInitializer]s to run (they register the generated
        // Style A per-view contexts into GeneratedJsonContextStore and the export accessor maps into
        // ViewAccessorRegistry). Referencing a type via typeof alone does not guarantee the module .cctor has
        // run, so run it explicitly — keeping the "artifacts landed in the template assembly" guard
        // deterministic whether this class runs in isolation or as part of the full suite (mirroring
        // JsonContextLayeringGuardTests / StyleASeamCoexistenceTests).
        RuntimeHelpers.RunModuleConstructor(TemplateAssembly.ManifestModule.ModuleHandle);
    }

    /// <summary>
    /// R5.4 / R7.5 (no new store): <c>a2n.Vista.Core</c>, anchored on the two EXISTING stores this phase
    /// reuses (<see cref="ViewAccessorRegistry"/> and <see cref="GeneratedJsonContextStore"/>), has no direct
    /// reference to <c>System.Text.Json</c>, EF Core (<c>Microsoft.EntityFrameworkCore*</c>), or ASP.NET Core
    /// (<c>Microsoft.AspNetCore*</c>). Both stores resolving to the SAME Core assembly proves the feature
    /// added no new store (it only adds ENTRIES keyed by Style A view names), so it introduces no new Core
    /// cross-reference and preserves the pluggable-serializer boundary.
    /// </summary>
    [Test]
    public async Task Core_Reusing_Both_Stores_References_Neither_Stj_Ef_Nor_AspNetCore()
    {
        // Both reused stores must resolve to the Core assembly — no new store lives in an adapter or a new
        // assembly (R5.4 "no new store").
        await Assert.That(CoreAssembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(ViewAccessorRegistry).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(GeneratedJsonContextStore).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);

        var referencedNames = ReferencedAssemblyNames(CoreAssembly).ToList();

        var referencesStj = referencedNames
            .Any(name => name.StartsWith(SystemTextJsonPrefix, StringComparison.Ordinal));
        var referencesEfCore = referencedNames
            .Any(name => name.StartsWith(EfCorePrefix, StringComparison.Ordinal));
        var referencesAspNetCore = referencedNames
            .Any(name => name.StartsWith(AspNetCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesStj).IsFalse();
        await Assert.That(referencesEfCore).IsFalse();
        await Assert.That(referencesAspNetCore).IsFalse();
    }

    /// <summary>
    /// R7.5: the covered Style A per-view context and accessor map are emitted into the TEMPLATE assembly
    /// (<c>a2n.Vista.GeneratorStyleASample</c>), which carries no ASP.NET Core dependency — neither the
    /// <c>a2n.Vista.AspNetCore</c> assembly nor any <c>Microsoft.AspNetCore*</c> package. The generated code
    /// uses only <c>System.Text.Json</c> from the shared framework, never introducing an ASP.NET Core
    /// reference into a domain assembly. (Unlike the Phase 5 JsonTypeInfo sample, a Style A template is
    /// authored against an EF <c>DbContext</c>, so it legitimately references the EF layer — hence only the
    /// ASP.NET Core dependency is asserted absent, matching R7.5's wording.)
    /// </summary>
    [Test]
    public async Task Template_Assembly_With_Generated_Artifacts_Has_No_AspNetCore_Dependency()
    {
        // The template (and thus its generated artifacts) genuinely lives in the template assembly.
        await Assert.That(TemplateAssembly.GetName().Name).IsEqualTo(TemplateAssemblyName);
        await Assert.That(typeof(CatalogItemRow).Assembly.GetName().Name).IsEqualTo(TemplateAssemblyName);

        var referencedNames = ReferencedAssemblyNames(TemplateAssembly).ToList();

        var referencesVistaAspNetCore = referencedNames
            .Any(name => string.Equals(name, AspNetCoreAssemblyName, StringComparison.Ordinal));
        var referencesAspNetCorePackages = referencedNames
            .Any(name => name.StartsWith(AspNetCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesVistaAspNetCore).IsFalse();
        await Assert.That(referencesAspNetCorePackages).IsFalse();
    }

    /// <summary>
    /// R7.5 (positive sanity): the template assembly DOES reference <c>a2n.Vista.Core</c> (the stores its
    /// generated <c>[ModuleInitializer]</c>s target) and EF Core (<c>Microsoft.EntityFrameworkCore*</c>,
    /// since a Style A template is authored against an EF <c>DbContext</c> — the <c>DbContext</c>/<c>DbSet</c>
    /// types the projections use come from there). This proves the "no ASP.NET Core" assertion above is a
    /// real layering property over a POPULATED reference set that intentionally pulls in substantial
    /// dependencies — not an artifact of an assembly that references almost nothing — and documents exactly
    /// why EF is NOT asserted absent for the template (only ASP.NET Core is, per R7.5). Anchored on the EF
    /// PACKAGE (<c>Microsoft.EntityFrameworkCore*</c>, genuinely used by the fixture's <c>DbContext</c>)
    /// rather than the Vista EF adapter assembly, since the compiler trims an unused metadata reference.
    /// </summary>
    [Test]
    public async Task Template_Assembly_References_Core_And_EfCore_But_Not_AspNetCore()
    {
        var referencedNames = ReferencedAssemblyNames(TemplateAssembly).ToList();

        var referencesCore = referencedNames
            .Any(name => string.Equals(name, CoreAssemblyName, StringComparison.Ordinal));
        var referencesEfCore = referencedNames
            .Any(name => name.StartsWith(EfCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesCore).IsTrue();
        await Assert.That(referencesEfCore).IsTrue();
    }

    /// <summary>
    /// R7.5 (emission target): the generated per-view context and accessor map genuinely LAND in the template
    /// assembly. Running that assembly's module initializers registers the covered Style A entries into the
    /// Core stores keyed by the CONSTANT <c>AddView</c> names (the D129 keying), so a covered named-row view
    /// (<c>stylea-catalog-items</c>) has BOTH a generated export accessor (in <see cref="ViewAccessorRegistry"/>)
    /// and a generated per-view context (in <see cref="GeneratedJsonContextStore"/>), and the writable
    /// anonymous-row view (<c>stylea-audit-entries</c>) has its <c>TCrud</c> context registered (the D96
    /// asymmetry — write model covered even though the read row is unnameable). This proves the emission
    /// target is the template's OWN assembly, complementing the "no ASP.NET Core dependency" guard above.
    /// </summary>
    [Test]
    public async Task Generated_Style_A_Artifacts_Are_Registered_From_The_Template_Assembly()
    {
        // The named-row read view's export accessor map was emitted into the template assembly and registered
        // into ViewAccessorRegistry keyed by its constant AddView name.
        await Assert.That(ViewAccessorRegistry.TryGetAccessor(CatalogItemsView, "ItemId", out _)).IsTrue();

        // The named-row view's per-view IJsonTypeInfoResolver was emitted into the template assembly and
        // registered into GeneratedJsonContextStore keyed by the same constant name.
        await Assert.That(GeneratedJsonContextStore.TryGet(CatalogItemsView, out var readContext)).IsTrue();
        await Assert.That(readContext).IsNotNull();

        // The writable ANONYMOUS-row view's TCrud context was also emitted into the template assembly (the
        // D96 asymmetry: the write model is nameable/covered even though the read row is not), proving the
        // write-side artifact lands in the template's own assembly too.
        await Assert.That(GeneratedJsonContextStore.TryGet(AuditEntriesView, out var writeContext)).IsTrue();
        await Assert.That(writeContext).IsNotNull();
    }

    /// <summary>
    /// R7.5 (positive sanity + placement): the STJ-typed drain — the only code that must NAME
    /// <c>System.Text.Json.Serialization.Metadata</c> resolver types to chain the JSON store — lives in
    /// <c>a2n.Vista.AspNetCore</c> (the serialization seam <see cref="VistaJson"/>) and is unchanged by this
    /// phase (D126). This proves the "Core has no STJ" assertion above is a real layering property: the
    /// STJ-touching drain genuinely lives in the ASP.NET Core package, not in Core.
    /// </summary>
    [Test]
    public async Task Stj_Typed_Drain_Resides_In_AspNetCore()
    {
        await Assert.That(AspNetCoreAssembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);
        await Assert.That(typeof(VistaJson).Assembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);

        // The drain references System.Text.Json — proving the STJ-typed code lives here, not in Core.
        var referencesStj = ReferencedAssemblyNames(AspNetCoreAssembly)
            .Any(name => name.StartsWith(SystemTextJsonPrefix, StringComparison.Ordinal));
        await Assert.That(referencesStj).IsTrue();
    }

    /// <summary>
    /// Projects the direct referenced-assembly simple names of <paramref name="assembly"/>
    /// (<see cref="Assembly.GetReferencedAssemblies()"/> → <see cref="AssemblyName.Name"/>), dropping any
    /// null names defensively. Mirrors the helper in <see cref="LayeringGuardTests"/>.
    /// </summary>
    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)!;
}
