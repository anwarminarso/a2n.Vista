// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Linq;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Focused coverage for the document-level (top-level) <c>security</c> requirement (Requirements 7.2, 7.5).
/// The canonical Vista fixture secures its operations <em>only</em> at the document root
/// (<c>security: [ { "Bearer": [] } ]</c>) with no per-operation <c>security</c>. This proves the parser
/// captures that root requirement in <see cref="OpenApiDocument.Security"/> and that the default
/// <see cref="SecurityPostureBuilder.Build(ResolvedDocument)"/> overload honors it, so the customers view
/// operations classify as secured rather than being misclassified as anonymous.
/// </summary>
public sealed class DocumentLevelSecurityTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid-vista-document.json");

    private static OpenApiDocument ParseFixture()
    {
        var raw = File.ReadAllBytes(FixturePath);
        Result<OpenApiDocument, ParseError> result = OpenApiParser.Parse(raw);

        if (result.IsError)
        {
            throw new Exception($"The valid Vista fixture must parse, but failed: {result.Error}");
        }

        return result.Value;
    }

    [Test]
    public async Task Parsing_The_Fixture_Captures_The_Top_Level_Security_Requirement()
    {
        OpenApiDocument document = ParseFixture();

        // The fixture's root-level "security" is [ { "Bearer": [] } ] — a single Bearer requirement.
        await Assert.That(document.Security).IsNotEmpty();
        await Assert.That(document.Security.Any(r => r.SchemeName == "Bearer")).IsTrue();
    }

    [Test]
    public async Task Default_Posture_Build_Classifies_Customers_Operations_As_Secured()
    {
        OpenApiDocument document = ParseFixture();

        Result<ResolvedDocument, ResolveError> resolveResult = RefResolver.Resolve(document);
        await Assert.That(resolveResult.IsOk).IsTrue();
        ResolvedDocument resolved = resolveResult.Value;

        // The single-arg overload must pass the document-level default through, so operations that declare
        // no per-operation security inherit the top-level Bearer requirement (Requirements 7.2, 7.5).
        SecurityPosture posture = new SecurityPostureBuilder().Build(resolved);

        await Assert.That(posture.DocumentDefaultSchemeNames).Contains("Bearer");

        // Every customers view operation is secured by the top-level Bearer requirement.
        var customersOperations = document.Paths
            .Where(path => path.Key.Contains("/customers/"))
            .SelectMany(path => path.Value.Operations.Values)
            .ToArray();

        await Assert.That(customersOperations).IsNotEmpty();
        foreach (OpenApiOperation operation in customersOperations)
        {
            await Assert.That(posture.IsSecured(operation))
                .IsTrue();
        }
    }
}
