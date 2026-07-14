using System.Xml.Linq;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Layering and target-framework smoke tests for the generator project (Task 1.3).
///
/// These tests parse the on-disk MSBuild project files (no build-graph reflection) to enforce the
/// architectural boundaries the spec pins:
/// <list type="bullet">
///   <item>the generator multi-targets exactly <c>net8.0;net9.0;net10.0</c> and no others
///     (Requirement 12.4);</item>
///   <item>the generator references neither <c>a2n.Vista.EntityFrameworkCore</c>,
///     <c>a2n.Vista.AspNetCore</c>, <c>a2n.Vista.OpenApi</c>, nor <c>a2n.Vista.Core</c>
///     (Requirements 12.1, 12.2 — it is a pure OpenAPI-document consumer);</item>
///   <item>no server package declares a dependency edge onto this project
///     (Requirements 13.1, 13.3).</item>
/// </list>
/// The project files are located by walking up from the test assembly to the repository root, so the
/// assertions are robust to the working directory the runner is launched from.
/// </summary>
public sealed class LayeringSmokeTests
{
    private const string GeneratorProjectName = "a2n.Vista.Client.TypeScript";

    /// <summary>Server packages that must never depend on the generator (Requirement 13.3).</summary>
    private static readonly string[] ServerPackages =
    {
        "a2n.Vista.Core",
        "a2n.Vista.EntityFrameworkCore",
        "a2n.Vista.AspNetCore",
        "a2n.Vista.OpenApi",
    };

    /// <summary>
    /// Locates the repository root by walking up from the test assembly until the generator project
    /// file is found under <c>src/</c>. Fails loudly if the layout is not as expected.
    /// </summary>
    private static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var probe = Path.Combine(
                    dir.FullName,
                    "src",
                    GeneratorProjectName,
                    GeneratorProjectName + ".csproj");
                if (File.Exists(probe))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
        }
    }

    private static string PackageCsprojPath(string packageName) =>
        Path.Combine(RepositoryRoot, "src", packageName, packageName + ".csproj");

    private static string GeneratorCsprojPath => PackageCsprojPath(GeneratorProjectName);

    /// <summary>Reads the <c>Include</c> attribute of every item with the given element name.</summary>
    private static IReadOnlyList<string> ItemIncludes(XDocument project, string itemName) =>
        project.Descendants()
            .Where(e => e.Name.LocalName == itemName)
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToArray();

    /// <summary>Reads the concatenated text of every element with the given name.</summary>
    private static IReadOnlyList<string> PropertyValues(XDocument project, string propertyName) =>
        project.Descendants()
            .Where(e => e.Name.LocalName == propertyName)
            .Select(e => e.Value.Trim())
            .ToArray();

    [Test]
    public async Task Generator_Multi_Targets_Exactly_Net8_Net9_Net10()
    {
        var project = XDocument.Load(GeneratorCsprojPath);

        // The generator declares TargetFrameworks (plural) explicitly; a single TargetFramework would be
        // a regression away from the required multi-target.
        var singular = PropertyValues(project, "TargetFramework");
        await Assert.That(singular).IsEmpty();

        var plural = PropertyValues(project, "TargetFrameworks");
        await Assert.That(plural.Count).IsEqualTo(1);

        var declared = plural[0];
        await Assert.That(declared).IsEqualTo("net8.0;net9.0;net10.0");

        // Parse the effective set and assert it is exactly the three supported runtimes — order-independent
        // and tolerant of any accidental trailing separator, but rejecting any extra framework.
        var frameworks = declared
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        await Assert.That(frameworks.Length).IsEqualTo(3);
        await Assert.That(frameworks).Contains("net8.0");
        await Assert.That(frameworks).Contains("net9.0");
        await Assert.That(frameworks).Contains("net10.0");
    }

    [Test]
    public async Task Generator_References_No_Server_Package()
    {
        var project = XDocument.Load(GeneratorCsprojPath);

        var projectRefs = ItemIncludes(project, "ProjectReference");
        var packageRefs = ItemIncludes(project, "PackageReference");
        var allRefs = projectRefs.Concat(packageRefs).ToArray();

        // Neither a ProjectReference nor a PackageReference may point at EF, AspNetCore, OpenApi, or Core.
        // The generator is a pure downstream consumer of the OpenAPI document (Requirements 12.1, 12.2).
        foreach (var forbidden in ServerPackages)
        {
            var offenders = allRefs
                .Where(r => r.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            await Assert.That(offenders.Length)
                .IsEqualTo(0)
                .Because($"the generator must not reference {forbidden}");
        }
    }

    [Test]
    public async Task No_Server_Package_References_The_Generator()
    {
        // Scan each server package's project file: none may carry a ProjectReference (or PackageReference)
        // back to the generator, enforcing the one-way dependency edge (Requirements 13.1, 13.3).
        foreach (var package in ServerPackages)
        {
            var csprojPath = PackageCsprojPath(package);
            await Assert.That(File.Exists(csprojPath))
                .IsTrue()
                .Because($"expected server package project at {csprojPath}");

            var project = XDocument.Load(csprojPath);
            var refs = ItemIncludes(project, "ProjectReference")
                .Concat(ItemIncludes(project, "PackageReference"))
                .ToArray();

            var offenders = refs
                .Where(r => r.Contains(GeneratorProjectName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            await Assert.That(offenders.Length)
                .IsEqualTo(0)
                .Because($"{package} must not depend on {GeneratorProjectName}");
        }
    }
}
