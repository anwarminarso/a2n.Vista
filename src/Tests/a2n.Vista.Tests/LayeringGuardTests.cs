using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Results;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Correctness Property 5 — clean layering (design.md §"Property 5"; authoritative
/// docs/spec/01-view.md §"01-view", Decision Log D48). Asserts the compile-time dependency
/// direction of the three Vista assemblies via reflection over
/// <see cref="Assembly.GetReferencedAssemblies()"/>:
/// <list type="bullet">
/// <item>R11.1 — <c>a2n.Vista.Core</c> references neither EF Core
/// (<c>Microsoft.EntityFrameworkCore*</c>) nor ASP.NET Core (<c>Microsoft.AspNetCore*</c>).</item>
/// <item>R11.3 — <c>a2n.Vista.EntityFrameworkCore</c> and <c>a2n.Vista.AspNetCore</c> do not
/// reference each other (asserted in both directions).</item>
/// </list>
/// <para>
/// Nuance: <see cref="Assembly.GetReferencedAssemblies()"/> reports the DIRECT assembly references
/// the C# compiler emitted into the metadata of the inspected assembly. Transitive references are
/// not listed, and the compiler may omit a referenced assembly that ends up unused in the produced
/// IL. That trimming is exactly the behaviour we want here: it captures real compile-time
/// dependencies. Because Core genuinely never touches EF/ASP.NET types, those assemblies are absent
/// from its reference set, so the "must not reference" assertions are sound. The positive sanity
/// checks below confirm the layering still meets in Core (both adapters DO reference it), proving the
/// absence above is a real layering property and not an artifact of an assembly that references
/// nothing.
/// </para>
/// </summary>
public sealed class LayeringGuardTests
{
    private const string CoreAssemblyName = "a2n.Vista.Core";
    private const string EfAssemblyName = "a2n.Vista.EntityFrameworkCore";
    private const string AspNetCoreAssemblyName = "a2n.Vista.AspNetCore";

    private const string EfCorePrefix = "Microsoft.EntityFrameworkCore";
    private const string AspNetCorePrefix = "Microsoft.AspNetCore";

    // Anchor types: typeof(...).Assembly loads each Vista assembly the test project references.
    private static readonly Assembly CoreAssembly = typeof(ViewQueryRequest).Assembly;
    private static readonly Assembly EfAssembly = typeof(EfViewExecutor).Assembly;
    private static readonly Assembly AspNetCoreAssembly = typeof(IViewAuthorizer).Assembly;

    /// <summary>
    /// R11.1: <c>a2n.Vista.Core</c>'s direct references include no EF Core assembly
    /// (<c>Microsoft.EntityFrameworkCore*</c>).
    /// </summary>
    [Test]
    public async Task Core_Does_Not_Reference_EntityFrameworkCore()
    {
        // Sanity-check the anchors resolve to the expected assemblies.
        await Assert.That(CoreAssembly.GetName().Name).IsEqualTo(CoreAssemblyName);
        await Assert.That(typeof(PagedResult<>).Assembly.GetName().Name).IsEqualTo(CoreAssemblyName);

        var referencesEfCore = ReferencedAssemblyNames(CoreAssembly)
            .Any(name => name.StartsWith(EfCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesEfCore).IsFalse();
    }

    /// <summary>
    /// R11.1: <c>a2n.Vista.Core</c>'s direct references include no ASP.NET Core assembly
    /// (<c>Microsoft.AspNetCore*</c>).
    /// </summary>
    [Test]
    public async Task Core_Does_Not_Reference_AspNetCore()
    {
        var referencesAspNetCore = ReferencedAssemblyNames(CoreAssembly)
            .Any(name => name.StartsWith(AspNetCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesAspNetCore).IsFalse();
    }

    /// <summary>
    /// R11.3: <c>a2n.Vista.EntityFrameworkCore</c> does not reference <c>a2n.Vista.AspNetCore</c>.
    /// </summary>
    [Test]
    public async Task EntityFrameworkCore_Does_Not_Reference_AspNetCore()
    {
        await Assert.That(EfAssembly.GetName().Name).IsEqualTo(EfAssemblyName);

        var referencesAspNetCore = ReferencedAssemblyNames(EfAssembly)
            .Any(name => string.Equals(name, AspNetCoreAssemblyName, StringComparison.Ordinal));

        await Assert.That(referencesAspNetCore).IsFalse();
    }

    /// <summary>
    /// R11.3: <c>a2n.Vista.AspNetCore</c> does not reference <c>a2n.Vista.EntityFrameworkCore</c>.
    /// </summary>
    [Test]
    public async Task AspNetCore_Does_Not_Reference_EntityFrameworkCore()
    {
        await Assert.That(AspNetCoreAssembly.GetName().Name).IsEqualTo(AspNetCoreAssemblyName);

        var referencesEf = ReferencedAssemblyNames(AspNetCoreAssembly)
            .Any(name => string.Equals(name, EfAssemblyName, StringComparison.Ordinal));

        await Assert.That(referencesEf).IsFalse();
    }

    /// <summary>
    /// Supporting (R11.3 spirit): <c>a2n.Vista.AspNetCore</c> shares only Core, so it must not pull in
    /// any EF Core assembly (<c>Microsoft.EntityFrameworkCore*</c>) either. The EF adapter MAY reference
    /// EF Core — that is expected and intentionally not asserted against.
    /// </summary>
    [Test]
    public async Task AspNetCore_Does_Not_Reference_EntityFrameworkCore_Packages()
    {
        var referencesEfCore = ReferencedAssemblyNames(AspNetCoreAssembly)
            .Any(name => name.StartsWith(EfCorePrefix, StringComparison.Ordinal));

        await Assert.That(referencesEfCore).IsFalse();
    }

    /// <summary>
    /// Core, the EF library, and AspNetCore must not reference an EF provider or an ADO.NET provider.
    /// The EF library may reference <c>Microsoft.EntityFrameworkCore</c> itself. SQLite classification
    /// stays in the Northwind hosts, which are the projects that reference <c>Microsoft.Data.Sqlite</c>.
    /// </summary>
    [Test]
    public async Task Vista_Libraries_Do_Not_Reference_Database_Provider_Packages()
    {
        var forbiddenAssemblies = new[]
        {
            "Microsoft.Data.Sqlite",
            "Microsoft.Data.SqlClient",
            "System.Data.SqlClient",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Microsoft.EntityFrameworkCore.SqlServer",
            "Microsoft.EntityFrameworkCore.InMemory",
            "Microsoft.EntityFrameworkCore.Cosmos",
            "Npgsql",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "MySqlConnector",
        };
        var forbiddenPackages = new[]
        {
            "Microsoft.Data.Sqlite",
            "Microsoft.Data.SqlClient",
            "System.Data.SqlClient",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Microsoft.EntityFrameworkCore.SqlServer",
            "Microsoft.EntityFrameworkCore.InMemory",
            "Microsoft.EntityFrameworkCore.Cosmos",
            "Npgsql",
            "MySqlConnector",
            "Oracle.ManagedDataAccess",
            "Oracle.EntityFrameworkCore",
            "Pomelo.EntityFrameworkCore",
        };

        foreach (var assembly in new[] { CoreAssembly, EfAssembly, AspNetCoreAssembly })
        {
            var hit = ReferencedAssemblyNames(assembly)
                .FirstOrDefault(name => forbiddenAssemblies.Contains(name, StringComparer.Ordinal));
            await Assert.That(hit).IsNull();
        }

        foreach (var project in new[]
        {
            "src/a2n.Vista.Core/a2n.Vista.Core.csproj",
            "src/a2n.Vista.EntityFrameworkCore/a2n.Vista.EntityFrameworkCore.csproj",
            "src/a2n.Vista.AspNetCore/a2n.Vista.AspNetCore.csproj",
        })
        {
            var text = File.ReadAllText(FindRepoFile(project));
            foreach (Match match in PackageReferencePattern.Matches(text))
            {
                var package = match.Groups[1].Value;
                var forbidden = forbiddenPackages.FirstOrDefault(name =>
                    package.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || package.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase));
                await Assert.That(forbidden).IsNull();
            }
        }
    }

    /// <summary>
    /// Positive sanity: both adapters DO reference <c>a2n.Vista.Core</c>. This proves the layering meets
    /// in Core and that the "must not reference" assertions above are meaningful (the absence of EF/ASP.NET
    /// from Core is a real layering property, not an empty reference set).
    /// </summary>
    [Test]
    public async Task Both_Adapters_Reference_Core()
    {
        var efReferencesCore = ReferencedAssemblyNames(EfAssembly)
            .Any(name => string.Equals(name, CoreAssemblyName, StringComparison.Ordinal));
        var aspNetCoreReferencesCore = ReferencedAssemblyNames(AspNetCoreAssembly)
            .Any(name => string.Equals(name, CoreAssemblyName, StringComparison.Ordinal));

        await Assert.That(efReferencesCore).IsTrue();
        await Assert.That(aspNetCoreReferencesCore).IsTrue();
    }

    private static readonly Regex PackageReferencePattern = new(
        "Include=\"([^\"]+)\"",
        RegexOptions.CultureInvariant);

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} by walking up from {AppContext.BaseDirectory}.");
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
