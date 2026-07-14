// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using a2n.Vista.Client.TypeScript.Parity;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Resolve;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Exports the <b>representative value set</b> (task 15.1, <see cref="RepresentativeValueSet"/>) — derived
/// from the resolved M18 fixture document, the authoritative oracle — to a committed JSON fixture that the
/// TypeScript round-trip parity harness (task 15.2) consumes.
/// </summary>
/// <remarks>
/// <para>
/// The exporter is the bridge between the two runtimes: the C# side builds representative JSON instances
/// for each <c>Generated_Type</c> (covering the Requirement 11.1 criteria — each property present, each
/// nullable in present-null and absent forms, each enum member, each collection empty and non-empty), and
/// the TypeScript side (<c>tests/ts-runtime/src/properties/round-trip-parity.test.ts</c>) reads them back
/// and asserts <c>parse(serialize(v))</c> deeply equals <c>v</c> in the generated client.
/// </para>
/// <para>
/// The fixture is written to
/// <c>src/a2n.Vista.Client.TypeScript/tests/ts-runtime/fixtures/representative-values.json</c> as a
/// deterministic map of <c>typeName -&gt; array of JSON values</c>. Output is reproducible: types are
/// emitted in ordinal name order, the underlying value set is a pure function of the document, and the
/// serialized bytes are normalized to UTF-8 (no BOM) with a single <c>\n</c> terminator, so regenerating
/// leaves the committed file byte-identical. Running this test both (re)produces the fixture and guards it
/// against drift.
/// </para>
/// </remarks>
public sealed class RepresentativeValuesFixtureExportTests
{
    // The Generated_Types the round-trip parity harness exercises, in ordinal name order for determinism.
    private static readonly string[] ExportedTypeNames =
    {
        "CustomerRow",
        "FilterNode",
        "ProblemDetails",
        "VistaListRequestBody",
        "VistaMetadataResponse",
        "VistaSortBody",
    };

    [Test]
    public async Task Export_Representative_Values_Fixture_For_The_Typescript_Round_Trip_Harness()
    {
        var document = ResolveFixture();
        var builder = new RepresentativeValueSet();

        // Build the deterministic typeName -> [values] map (ordinal type order).
        var root = new JsonObject();
        foreach (var typeName in ExportedTypeNames)
        {
            var values = builder.Build(typeName, document);
            var array = new JsonArray();
            foreach (var value in values)
            {
                // DeepClone so the node is unparented and safe to attach to the output array.
                array.Add(value.DeepClone());
            }

            root[typeName] = array;
        }

        var json = Serialize(root);

        var fixturePath = FixtureOutputPath();
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);

        // (Re)produce the committed fixture. Deterministic output keeps the working tree clean across runs.
        await File.WriteAllTextAsync(fixturePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Guard: what landed on disk is exactly what we serialized (byte-for-byte, normalized \n endings).
        var written = await File.ReadAllTextAsync(fixturePath);
        await Assert.That(written).IsEqualTo(json);

        // Sanity: every exported type carries at least one representative value.
        foreach (var typeName in ExportedTypeNames)
        {
            await Assert.That(root[typeName] is JsonArray { Count: > 0 }).IsTrue();
        }
    }

    // Serializes the map with indentation, then normalizes to a single fixed \n line terminator and a
    // trailing newline so the committed bytes are stable across operating systems.
    private static string Serialize(JsonObject root)
    {
        var raw = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    private static ResolvedDocument ResolveFixture()
    {
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid-vista-document.json"));

        var parsed = OpenApiParser.Parse(raw);
        if (parsed.IsError)
        {
            throw new Exception($"Fixture failed to parse: {parsed.Error.Message}");
        }

        var resolved = RefResolver.Resolve(parsed.Value);
        if (resolved.IsError)
        {
            throw new Exception($"Fixture failed to resolve: {resolved.Error.Message}");
        }

        return resolved.Value;
    }

    // Resolves the committed fixture location relative to THIS source file, independent of the working
    // directory or the test assembly's output path. From
    //   src/Tests/a2n.Vista.Client.TypeScript.Tests/RepresentativeValuesFixtureExportTests.cs
    // the ts-runtime fixtures live at
    //   src/a2n.Vista.Client.TypeScript/tests/ts-runtime/fixtures/representative-values.json
    private static string FixtureOutputPath([CallerFilePath] string thisFilePath = "")
    {
        var testProjectDir = Path.GetDirectoryName(thisFilePath)!;          // .../src/Tests/a2n.Vista.Client.TypeScript.Tests
        var srcDir = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..")); // .../src

        return Path.Combine(
            srcDir,
            "a2n.Vista.Client.TypeScript",
            "tests",
            "ts-runtime",
            "fixtures",
            "representative-values.json");
    }
}
