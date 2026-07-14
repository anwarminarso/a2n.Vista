// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for the parse stage's OpenAPI version gating (task 4.2; Requirement 1.5). The
/// generator supports only the <c>3.0.x</c>–<c>3.1.x</c> range: major <c>3</c> and minor <c>0</c> or
/// <c>1</c>. Any other declared <c>openapi</c> version — a different major, a minor of <c>2</c> or higher,
/// a <c>2.x</c>/<c>4.x</c> document, a non-numeric component, or a single token with no minor — must abort
/// parsing with <see cref="ParseError.UnsupportedVersion"/> that <em>names</em> the offending version, and
/// must produce no document.
/// </summary>
/// <remarks>
/// Both properties feed <see cref="OpenApiParser.Parse(string)"/> a minimal-but-well-formed OpenAPI JSON
/// document that declares the generated version (built through <see cref="JsonSerializer"/> so the version
/// string is always correctly escaped and the surrounding document is always valid JSON). The core property
/// (out-of-range → aborts, naming it) is the Requirement 1.5 gate; the companion property (in-range → not
/// rejected for a version reason) guards against an over-eager gate that would reject a supported document.
/// </remarks>
public sealed class OpenApiVersionGatingPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>
    /// Mirrors <c>OpenApiParser.IsSupportedVersion</c> so the generators can guarantee, by construction,
    /// that a case lands strictly outside (or strictly inside) the supported range. Keeping the predicate
    /// here — rather than reaching into the parser's private method — makes the test an independent oracle.
    /// </summary>
    private static bool IsSupported(string declared)
    {
        string[] parts = declared.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
        {
            return false;
        }

        return major == 3 && minor is 0 or 1;
    }

    /// <summary>
    /// Builds a minimal, well-formed OpenAPI document that declares <paramref name="version"/>. Serializing
    /// through <see cref="JsonSerializer"/> keeps the document valid JSON regardless of what the version
    /// string contains, so the only variable under test is the declared version itself.
    /// </summary>
    private static string BuildDocument(string version)
    {
        var document = new Dictionary<string, object?>
        {
            ["openapi"] = version,
            ["info"] = new Dictionary<string, object?>
            {
                ["title"] = "a2n.Vista API",
                ["version"] = "1.0.0",
            },
            ["paths"] = new Dictionary<string, object?>(),
            ["components"] = new Dictionary<string, object?>
            {
                ["schemas"] = new Dictionary<string, object?>(),
            },
        };

        return JsonSerializer.Serialize(document);
    }

    /// <summary>A dotted major.minor(.patch) version whose components are all numeric.</summary>
    private static Gen<string> NumericVersion(Gen<int> major, Gen<int> minor) =>
        from maj in major
        from min in minor
        from hasPatch in Gen.Bool
        from patch in Gen.Int[0, 30]
        select hasPatch ? $"{maj}.{min}.{patch}" : $"{maj}.{min}";

    /// <summary>
    /// Version strings guaranteed to fall OUTSIDE the supported <c>3.0.x</c>–<c>3.1.x</c> range, spanning
    /// every out-of-range shape: a non-3 major, a 3.x with minor ≥ 2, a non-numeric component, and a
    /// single-token string with no minor. A final <c>Where</c> filters out any candidate that happens to be
    /// supported (belt-and-suspenders — the sub-generators are already constructed to be unsupported).
    /// </summary>
    private static readonly Gen<string> UnsupportedVersion =
        Gen.OneOf(
            // A major other than 3 (0,1,2,4,5,…), any minor — e.g. "2.0", "4.1.7".
            NumericVersion(Gen.Int[0, 12].Where(m => m != 3), Gen.Int[0, 20]),
            // Major 3 but a minor of 2 or higher — e.g. "3.2", "3.5.1".
            NumericVersion(Gen.OneOfConst(3), Gen.Int[2, 20]),
            // A non-numeric component in an otherwise dotted string — e.g. "3.x", "beta.0", "3.0-rc".
            Gen.OneOfConst("3.x", "3.0-rc", "beta.0", "x.y", "v3.0", "3.0.0-preview", "3..0"),
            // A single token with no minor component (parts.Length < 2) — e.g. "3", "42", "latest".
            Gen.OneOfConst("3", "2", "4", "42", "latest", "openapi"))
        .Where(v => !IsSupported(v));

    /// <summary>
    /// Version strings guaranteed to fall INSIDE the supported range: major 3, minor 0 or 1, with an
    /// optional patch — e.g. "3.0", "3.1", "3.0.4", "3.1.12".
    /// </summary>
    private static readonly Gen<string> SupportedVersion =
        NumericVersion(Gen.OneOfConst(3), Gen.OneOfConst(0, 1))
        .Where(IsSupported);

    // Feature: typescript-client, Property 16: Unsupported OpenAPI version aborts, naming it
    //
    // For any declared openapi version outside the supported 3.0.x–3.1.x range, parsing a well-formed
    // document that declares it aborts with ParseError.UnsupportedVersion whose DeclaredVersion is exactly
    // the declared version (it names it), and yields no document.
    //
    // Validates: Requirements 1.5
    [Test]
    public void Unsupported_Version_Aborts_Naming_The_Declared_Version()
    {
        UnsupportedVersion.Sample(
            version =>
            {
                string json = BuildDocument(version);

                Result<OpenApiDocument, ParseError> result = OpenApiParser.Parse(json);

                // The parse must abort — no document is produced for an unsupported version.
                if (result.IsOk)
                {
                    throw new Exception(
                        $"Version '{version}' is outside the supported 3.0.x–3.1.x range but parsing " +
                        "succeeded; an unsupported version must abort with no document.");
                }

                // The abort must be specifically the version gate, not a malformed-document report.
                if (result.Error is not ParseError.UnsupportedVersion unsupported)
                {
                    throw new Exception(
                        $"Version '{version}' aborted with '{result.Error.GetType().Name}' " +
                        $"({result.Error.Message}), expected ParseError.UnsupportedVersion.");
                }

                // The error must name the exact declared version (Requirement 1.5: "naming it").
                if (!string.Equals(unsupported.DeclaredVersion, version, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"UnsupportedVersion named '{unsupported.DeclaredVersion}', expected the declared " +
                        $"'{version}'.");
                }
            },
            iter: Iterations);
    }

    // Feature: typescript-client, Property 16: Unsupported OpenAPI version aborts, naming it (companion)
    //
    // Guard against an over-eager gate: any in-range version (3.0.x or 3.1.x) parses without being rejected
    // for a version reason. This anchors the boundary the core property tests against — the gate rejects
    // ONLY what is outside the range.
    //
    // Validates: Requirements 1.5
    [Test]
    public void Supported_Version_Is_Not_Rejected_For_A_Version_Reason()
    {
        SupportedVersion.Sample(
            version =>
            {
                string json = BuildDocument(version);

                Result<OpenApiDocument, ParseError> result = OpenApiParser.Parse(json);

                // A supported version must never trip the version gate. (The minimal document is otherwise
                // well-formed, so this succeeds; the assertion of interest is the absence of UnsupportedVersion.)
                if (result.IsError && result.Error is ParseError.UnsupportedVersion unsupported)
                {
                    throw new Exception(
                        $"In-range version '{version}' was rejected as unsupported " +
                        $"(named '{unsupported.DeclaredVersion}'); the gate must accept 3.0.x–3.1.x.");
                }
            },
            iter: Iterations);
    }
}
