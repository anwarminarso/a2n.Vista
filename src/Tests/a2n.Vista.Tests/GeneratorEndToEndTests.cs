using System;
using System.Threading.Tasks;
using a2n.Vista.GeneratorSample;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// End-to-end proof of the source generator (Spec source-generator, Task 6.1, R6.4; validates
/// R2.1/R3.2/R3.3 at real compile + runtime). The <c>a2n.Vista.GeneratorSample</c> library references
/// the generator as an analyzer and declares one partial typed Style B view (<see cref="SampleView"/>,
/// named "GeneratorSampleView"). At compile time the generator emitted, INTO that consumer assembly, a
/// <c>file static</c> accessor map plus a <c>[ModuleInitializer]</c> that registers the map into
/// <see cref="ViewAccessorRegistry"/>. These tests force the sample module to load and then assert the
/// GENERATED accessors are present and functional — proving the generator works end to end and
/// coexists with the existing reflection-based registration.
/// </summary>
public sealed class GeneratorEndToEndTests
{
    private const string SampleViewName = "GeneratorSampleView";

    /// <summary>
    /// Touches a type in the sample assembly so its module — and thus the generated
    /// <c>[ModuleInitializer]</c> that calls <see cref="ViewAccessorRegistry.Register"/> — is loaded
    /// before the assertions run. Instantiating the view is a safe, side-effect-free trigger.
    /// </summary>
    private static void EnsureSampleModuleLoaded() => _ = new SampleView().Name;

    [Test]
    public async Task Generated_Accessor_For_Id_Is_Registered_And_Reads_The_Value()
    {
        EnsureSampleModuleLoaded();

        var found = ViewAccessorRegistry.TryGetAccessor(SampleViewName, "Id", out var idAccessor);

        await Assert.That(found).IsTrue();
        // The generated accessor is a cast + property read: ((SampleRow)row).Id — no reflection.
        await Assert.That(idAccessor!(new SampleRow { Id = 7, Name = "x" })).IsEqualTo((object?)7);
    }

    [Test]
    public async Task Generated_Accessor_For_Name_Is_Registered_And_Reads_The_Value()
    {
        EnsureSampleModuleLoaded();

        var found = ViewAccessorRegistry.TryGetAccessor(SampleViewName, "Name", out var nameAccessor);

        await Assert.That(found).IsTrue();
        await Assert.That(nameAccessor!(new SampleRow { Id = 1, Name = "Ada" })).IsEqualTo((object?)"Ada");
    }

    [Test]
    public async Task Unregistered_View_Name_Returns_False()
    {
        EnsureSampleModuleLoaded();

        // Coexistence sanity: a view with no generated accessors is simply absent from the registry,
        // so the export path would fall back to reflection for it (Property 2).
        var found = ViewAccessorRegistry.TryGetAccessor(
            "no-such-view-" + Guid.NewGuid().ToString("N"), "Id", out var accessor);

        await Assert.That(found).IsFalse();
        await Assert.That(accessor).IsNull();
    }
}
