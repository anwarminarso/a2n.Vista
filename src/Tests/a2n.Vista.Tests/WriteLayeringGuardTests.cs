using System.Linq;
using System.Reflection;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Write-path layering guard (write-path design.md §"Layering enforcement (Requirement 14)";
/// Requirement 14.1–14.6). These assertions complement <see cref="LayeringGuardTests"/> by pinning the
/// hourglass layering specifically for the CRUD write path: the same
/// <see cref="Assembly.GetReferencedAssemblies()"/> reflection over anchor types, but anchored on the
/// write-path types (<see cref="WriteMapper"/>, <see cref="IWriteFacetRegistry"/>,
/// <see cref="WriteErrorCode"/>, the typed write exceptions, <see cref="WriteFacetRegistry"/>, the EF
/// <see cref="WriteMapperResolver"/>/<see cref="EfViewExecutor"/>, and the AspNetCore
/// <see cref="ViewRequestExecutor"/>).
/// <para>
/// The design's non-negotiable is that <b>no new cross-references are introduced</b> by the write path:
/// Core stays EF-free and HTTP-free even after the write contracts were added (R14.1); the write executor
/// lives in <c>a2n.Vista.EntityFrameworkCore</c> and implements the Core <see cref="IViewExecutor"/> port
/// (R14.2); the endpoint reaches the executor only through Core ports and carries no EF reference
/// (R14.3/R14.4/R14.5); and the EF and AspNetCore adapters never reference each other, meeting only at the
/// shared write types that reside in Core (R14.6).
/// </para>
/// <para>
/// Nuance (identical to <see cref="LayeringGuardTests"/>): <see cref="Assembly.GetReferencedAssemblies()"/>
/// reports the DIRECT references the compiler emitted into an assembly's metadata; transitive and
/// compiler-trimmed-unused references are absent. That is exactly the compile-time dependency property we
/// assert. The positive checks (both adapters reference Core; the shared write types' <c>Assembly</c> is
/// Core) prove the absences are real layering properties, not artifacts of empty reference sets.
/// </para>
/// </summary>
public sealed class WriteLayeringGuardTests
{
    private const string CoreAssemblyName = "a2n.Vista.Core";
    private const string EfAssemblyName = "a2n.Vista.EntityFrameworkCore";
    private const string AspNetCoreAssemblyName = "a2n.Vista.AspNetCore";

    private const string EfCorePrefix = "Microsoft.EntityFrameworkCore";
    private const string AspNetCorePrefix = "Microsoft.AspNetCore";

    // Anchor types drawn from the write path. typeof(...).Assembly loads the owning Vista assembly the
    // test project already references, so these resolve without extra wiring.
    private static readonly Assembly CoreAssembly = typeof(WriteFacetRegistry).Assembly;
    private static readonly Assembly EfAssembly = typeof(WriteMapperResolver).Assembly;
    private static readonly Assembly AspNetCoreAssembly = typeof(ViewRequestExecutor).Assembly;

    /// <summary>
    /// R14.1: <c>a2n.Vista.Core</c> — anchored on the write contracts added by this milestone — still has
    /// no direct reference to EF Core (<c>Microsoft.EntityFrameworkCore*</c>) or ASP.NET Core
    /// (<c>Microsoft.AspNetCore*</c>). The write path introduced no new cross-reference into Core.
    /// </summary>
    [Test]
    public async Task Core_With_WritePath_References_Neither_Ef_Nor_AspNetCore()
    {
        // The write anchor types must resolve to the Core assembly (proves they live in Core, not an adapter).
        await Assert.That(CoreAssembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(WriteMapper).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(IWriteFacetRegistry).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(WriteErrorCode).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);

        var referencedNames = ReferencedAssemblyNames(CoreAssembly).ToList();

        var referencesEfCore = referencedNames
            .Any(name => name.StartsWith(EfCorePrefix, StringComparison.Ordinal));
        var referencesAspNetCore = referencedNames
            .Any(name => name.StartsWith(AspNetCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesEfCore).IsFalse();
        await Assert.That(referencesAspNetCore).IsFalse();
    }

    /// <summary>
    /// R14.6 (shared types reside in Core): every write type on which both the EF and AspNetCore adapters
    /// depend — the mapper seam, the facet registry contract and implementation, the error vocabulary, and
    /// the typed write exceptions — has its <see cref="Type.Assembly"/> equal to <c>a2n.Vista.Core</c>. That
    /// is what lets the two adapters meet without referencing each other.
    /// </summary>
    [Test]
    public async Task Shared_Write_Types_Reside_In_Core()
    {
        Type[] sharedWriteTypes =
        [
            typeof(WriteMapper),
            typeof(IWriteFacetRegistry),
            typeof(WriteFacetRegistry),
            typeof(WriteErrorCode),
            typeof(VistaWriteException),
            typeof(VistaWriteKeyException),
            typeof(VistaValidationException),
            typeof(VistaPreconditionRequiredException),
            typeof(VistaConcurrencyConflictException),
            typeof(VistaWriteConflictException),
            typeof(VistaBulkNotEnabledException),
        ];

        foreach (var type in sharedWriteTypes)
        {
            await Assert.That(type.Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        }
    }

    /// <summary>
    /// R14.2: the write executor <see cref="EfViewExecutor"/> is defined in
    /// <c>a2n.Vista.EntityFrameworkCore</c> and implements the Core <see cref="IViewExecutor"/> port, whose
    /// contract type resides in <c>a2n.Vista.Core</c>.
    /// </summary>
    [Test]
    public async Task Write_Executor_Lives_In_Ef_And_Implements_Core_Port()
    {
        await Assert.That(EfAssembly.GetName().Name).IsEqualTo(EfAssemblyName);

        // The executor is in the EF assembly...
        await Assert.That(typeof(EfViewExecutor).Assembly.GetName().Name).IsEqualTo(EfAssemblyName);

        // ...it implements the Core write facet port...
        await Assert.That(typeof(IViewExecutor).IsAssignableFrom(typeof(EfViewExecutor))).IsTrue();

        // ...and that port is defined in Core (the adapters meet at the Core port, R14.2/R14.3).
        await Assert.That(typeof(IViewExecutor).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);
    }

    /// <summary>
    /// R14.5 (and R14.3/R14.4 spirit): the AspNetCore write endpoint layer, anchored on
    /// <see cref="ViewRequestExecutor"/>, carries no direct reference to the EF assembly and no EF Core
    /// package reference — it reaches the write executor only through the Core <see cref="IViewExecutor"/>
    /// port, so it never needs the concrete EF write types.
    /// </summary>
    [Test]
    public async Task Write_Endpoint_Has_No_Ef_Reference()
    {
        await Assert.That(AspNetCoreAssembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);

        var referencedNames = ReferencedAssemblyNames(AspNetCoreAssembly).ToList();

        var referencesEfAssembly = referencedNames
            .Any(name => string.Equals(name, EfAssemblyName, StringComparison.Ordinal));
        var referencesEfCorePackages = referencedNames
            .Any(name => name.StartsWith(EfCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesEfAssembly).IsFalse();
        await Assert.That(referencesEfCorePackages).IsFalse();
    }

    /// <summary>
    /// R14.6: the <c>a2n.Vista.EntityFrameworkCore</c> and <c>a2n.Vista.AspNetCore</c> assemblies do not
    /// reference each other in either direction — asserted here anchored on the write-path types to prove
    /// the write path introduced no adapter-to-adapter edge.
    /// </summary>
    [Test]
    public async Task Ef_And_AspNetCore_Do_Not_Reference_Each_Other()
    {
        var efReferencesAspNetCore = ReferencedAssemblyNames(EfAssembly)
            .Any(name => string.Equals(name, AspNetCoreAssemblyName, StringComparison.Ordinal));
        var aspNetCoreReferencesEf = ReferencedAssemblyNames(AspNetCoreAssembly)
            .Any(name => string.Equals(name, EfAssemblyName, StringComparison.Ordinal));

        await Assert.That(efReferencesAspNetCore).IsFalse();
        await Assert.That(aspNetCoreReferencesEf).IsFalse();
    }

    /// <summary>
    /// Positive sanity (R14.3/R14.4): both the EF and AspNetCore adapters DO reference
    /// <c>a2n.Vista.Core</c>. This proves the "must not reference each other" assertions above are real
    /// layering properties (the adapters genuinely meet in Core) rather than artifacts of empty reference
    /// sets.
    /// </summary>
    [Test]
    public async Task Both_Adapters_Reference_Core_For_Writes()
    {
        var efReferencesCore = ReferencedAssemblyNames(EfAssembly)
            .Any(name => string.Equals(name, CoreAssemblyName, StringComparison.Ordinal));
        var aspNetCoreReferencesCore = ReferencedAssemblyNames(AspNetCoreAssembly)
            .Any(name => string.Equals(name, CoreAssemblyName, StringComparison.Ordinal));

        await Assert.That(efReferencesCore).IsTrue();
        await Assert.That(aspNetCoreReferencesCore).IsTrue();
    }

    /// <summary>
    /// Projects the direct referenced-assembly simple names of <paramref name="assembly"/>
    /// (<see cref="Assembly.GetReferencedAssemblies()"/> → <see cref="AssemblyName.Name"/>), dropping any
    /// null names defensively.
    /// </summary>
    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)!;
}
