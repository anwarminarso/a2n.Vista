using System.Text.Json;
using a2n.Vista.Client.TypeScript.Model;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Trivial smoke tests proving (a) the TUnit harness runs, (b) the generator project reference resolves
/// (the test can name a type from <c>a2n.Vista.Client.TypeScript</c>), and (c) the sample OpenAPI documents
/// under <c>Fixtures/</c> are copied next to the test assembly and readable at runtime (Requirement 11.1).
/// Real pipeline coverage (parse/resolve/model/emit) is added by later tasks.
/// </summary>
public sealed class FixtureSmokeTests
{
    /// <summary>The directory the fixture documents are copied into, alongside the test assembly.</summary>
    private static string FixturesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Test]
    public async Task Harness_Runs_And_Can_Reference_The_Generator()
    {
        // Naming a public type from the generator proves the ProjectReference resolves at compile time.
        var info = new OpenApiInfo("a2n.Vista API", "1.0.0");

        await Assert.That(info.Title).IsEqualTo("a2n.Vista API");
        await Assert.That(info.Version).IsEqualTo("1.0.0");
    }

    [Test]
    public async Task All_Sample_Fixtures_Are_Copied_To_Output()
    {
        string[] expected =
        {
            "valid-vista-document.json",
            "malformed-document.json",
            "unsupported-version.json",
            "dangling-ref.json",
            "missing-envelope.json",
        };

        await Assert.That(Directory.Exists(FixturesDirectory)).IsTrue();

        foreach (var name in expected)
        {
            var path = Path.Combine(FixturesDirectory, name);
            await Assert.That(File.Exists(path)).IsTrue();
        }
    }

    [Test]
    public async Task Valid_Vista_Document_Fixture_Parses_As_OpenApi_3_0_4()
    {
        var path = Path.Combine(FixturesDirectory, "valid-vista-document.json");

        var raw = await File.ReadAllTextAsync(path);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("openapi", out var version)).IsTrue();
        await Assert.That(version.GetString()).IsEqualTo("3.0.4");

        await Assert.That(root.TryGetProperty("paths", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("components", out _)).IsTrue();
    }

    [Test]
    public async Task Malformed_Document_Fixture_Is_Not_Well_Formed_Json()
    {
        var path = Path.Combine(FixturesDirectory, "malformed-document.json");
        var raw = await File.ReadAllTextAsync(path);

        // The malformed fixture must fail JSON parsing, exercising the parse-stage fatal path later on.
        await Assert.That(() => JsonDocument.Parse(raw)).Throws<JsonException>();
    }
}
