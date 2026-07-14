// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Emit.Runtime;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for the "no embedded credential" invariant of the generated output (task 10.10;
/// Requirement 7.1; design Property 20 — "No embedded credential in the output"). Requirement 7.1 requires
/// the generator to <b>never</b> embed a credential, token, or secret value in any generated file: the
/// emitted client is secure by default and forces the consumer to supply auth (the default bearer scheme is
/// <c>bearerAuth(tokenProvider)</c>), so nothing is baked in.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is scanned.</b> The property assembles the <em>full</em> generated output the pipeline produces
/// and scans every file's bytes for an embedded secret:
/// </para>
/// <list type="bullet">
///   <item>
///     The six fixed-content runtime files — <c>runtime/http-transport.ts</c>, <c>runtime/auth.ts</c>,
///     <c>runtime/result.ts</c>, <c>runtime/url.ts</c>, <c>runtime/client-context.ts</c>,
///     <c>runtime/raw-payload.ts</c> — which do not vary with the document. <c>auth.ts</c> in particular is
///     the one file that constructs a bearer header, and it must do so only via the template
///     <c>`Bearer ${token}`</c> that references a consumer-supplied variable, never a literal token.
///   </item>
///   <item>
///     The document-derived files — <c>types.ts</c> (bound with the write surface on/off per the config's
///     <c>EmitWriteFacets</c>), <c>filter-node.ts</c>, and the per-view <c>views/{view}.ts</c> clients from
///     the canonical fixture.
///   </item>
///   <item>
///     Additional per-view <c>views/{view}.ts</c> clients emitted from randomly generated documents that
///     mix writable and read-only views with varied names, so the view-file surface is exercised across
///     many facet/view shapes (Requirement 4.1's facet set is derived from the document).
///   </item>
/// </list>
/// <para>
/// <b>Variation (≥ 100 cases).</b> Each case varies the config the way the CLI assembles it — a random
/// <c>DefaultBaseUrl</c> (present or absent) and <c>EmitWriteFacets</c> on/off — plus a random set of view
/// specs. A <c>DefaultBaseUrl</c> is a URL, not a credential; the property additionally asserts that when one
/// is supplied it is never derived into any generated file, so no credential can leak from config either.
/// </para>
/// <para>
/// <b>Secret-detection checks</b> applied to every file's content:
/// </para>
/// <list type="number">
///   <item>
///     <b>No hard-coded bearer token.</b> The only permitted construction after the literal <c>"Bearer "</c>
///     is a template interpolation (<c>${…}</c>) or a documentation placeholder (<c>&lt;…&gt;</c>); a
///     concrete token literal following <c>Bearer </c> (e.g. <c>"Bearer eyJ…"</c>) fails.
///   </item>
///   <item>
///     <b>No secret-named literal assignment.</b> No identifier named token/secret/password/apiKey/
///     credential/accessToken/privateKey/clientSecret is assigned or keyed to a non-empty quoted string
///     literal (e.g. <c>const token = "abc123"</c>).
///   </item>
///   <item>
///     <b>No JWT-looking literal.</b> No three base64url segments separated by dots.
///   </item>
///   <item>
///     <b>No secret-looking blob literal.</b> No quoted string literal that is a long, high-entropy
///     base64/hex run (a plausible embedded secret).
///   </item>
/// </list>
/// </remarks>
public sealed class NoEmbeddedCredentialPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy — ≥ 100).</summary>
    private const int Iterations = 100;

    private const string SchemaRefPrefix = "#/components/schemas/";
    private const string ListRequestBodyName = "VistaListRequestBody";

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    // ---- Secret-detection patterns -------------------------------------------------------------------

    // Every double- or single-quoted string literal (no escaped quotes appear in generated output).
    private static readonly Regex StringLiteralPattern =
        new("\"([^\"\\\\]*)\"|'([^'\\\\]*)'", RegexOptions.Compiled);

    // A secret-named identifier assigned/keyed to a non-empty quoted string literal, e.g. `token = "abc"` or
    // `apiKey: "abc"`. A bare type annotation (`password: string`) has no quotes and is not matched.
    private static readonly Regex SecretAssignmentPattern =
        new(
            "\\b(token|secret|password|passwd|apiKey|api_key|apikey|accessToken|access_token|refreshToken|" +
            "credential|clientSecret|client_secret|privateKey|private_key)\\b\\s*[:=]\\s*" +
            "(?<q>[\"'`])(?<val>[^\"'`]+)\\k<q>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Three base64url segments separated by dots — the shape of a JWT.
    private static readonly Regex JwtPattern =
        new("[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}", RegexOptions.Compiled);

    // A high-entropy base64 blob: 24+ base64/base64url chars carrying at least one digit and one letter.
    private static readonly Regex Base64BlobPattern =
        new("^(?=.*[A-Za-z])(?=.*[0-9])[A-Za-z0-9+=_-]{24,}$", RegexOptions.Compiled);

    // A hex blob: 32+ hex digits (a plausible key/hash secret).
    private static readonly Regex HexBlobPattern = new("^[0-9a-fA-F]{32,}$", RegexOptions.Compiled);

    // ---- Generators ----------------------------------------------------------------------------------

    /// <summary>A random <c>DefaultBaseUrl</c>: absent, or an HTTPS/loopback URL — never a credential.</summary>
    private static readonly Gen<string?> DefaultBaseUrl =
        from present in Gen.Bool
        from host in Gen.Char['a', 'z'].Array[3, 8].Select(chars => new string(chars))
        from port in Gen.Int[1, 65535]
        from https in Gen.Bool
        select present
            ? (https ? $"https://{host}.example.com/api" : $"http://localhost:{port}/api")
            : (string?)null;

    /// <summary>A lower-case identifier of 3–7 letters, usable as a view name and URL segment.</summary>
    private static readonly Gen<string> ViewName =
        Gen.Char['a', 'z'].Array[3, 7].Select(chars => new string(chars));

    /// <summary>A single view spec: a name and whether it is writable.</summary>
    private static readonly Gen<ViewSpec> ViewSpecGen =
        from name in ViewName
        from writable in Gen.Bool
        select new ViewSpec(name, writable);

    /// <summary>A non-empty set of distinctly-named view specs, mixing writable and read-only views.</summary>
    private static readonly Gen<IReadOnlyList<ViewSpec>> ViewSpecsGen =
        ViewSpecGen.Array[1, 5]
            .Select(specs => (IReadOnlyList<ViewSpec>)specs
                .GroupBy(spec => spec.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray());

    /// <summary>One generated case: the config variation plus a random view-spec set.</summary>
    private static readonly Gen<(bool EmitWriteFacets, string? DefaultBaseUrl, IReadOnlyList<ViewSpec> Specs)> Cases =
        from emitWrite in Gen.Bool
        from baseUrl in DefaultBaseUrl
        from specs in ViewSpecsGen
        select (emitWrite, baseUrl, specs);

    // Feature: typescript-client, Property 20: No embedded credential in the output
    //
    // For any config variation (DefaultBaseUrl present/absent, EmitWriteFacets on/off) and any set of
    // writable/read-only views, every file in the full generated output is free of an embedded credential,
    // token, or secret: no hard-coded bearer token (only the `Bearer ${token}` template referencing a
    // consumer-supplied variable is allowed), no secret-named literal assignment, no JWT-looking literal, and
    // no high-entropy base64/hex blob literal. A supplied DefaultBaseUrl (a URL, not a secret) never appears
    // in any generated file, so no credential can be derived from config either.
    //
    // Validates: Requirements 7.1
    [Test]
    public void No_Generated_File_Embeds_A_Credential_Token_Or_Secret()
    {
        // The fixed runtime files never vary with the document; assemble them once.
        var runtimeFiles = new[]
        {
            HttpTransportEmitter.Emit(),
            AuthEmitter.Emit(),
            ResultEmitter.Emit(),
            UrlEmitter.Emit(),
            ClientContextEmitter.Emit(),
            RawPayloadEmitter.Emit(),
        };

        // The canonical fixture supplies the envelope-bearing files. types.ts depends on the write-surface
        // toggle, so pre-bind it both ways; filter-node.ts and the fixture view clients are invariant.
        ResolvedDocument fixture = ResolveFixture();
        GeneratedFile fixtureTypesReadOnly = EmitFixtureTypes(fixture, includeWriteEnvelopes: false);
        GeneratedFile fixtureTypesWithWrite = EmitFixtureTypes(fixture, includeWriteEnvelopes: true);
        GeneratedFile fixtureFilterNode = EmitFixtureFilterNode(fixture);
        IReadOnlyList<GeneratedFile> fixtureViews = EmitFixtureViews(fixture);

        Cases.Sample(
            testCase =>
            {
                (bool emitWrite, string? defaultBaseUrl, IReadOnlyList<ViewSpec> specs) = testCase;

                // The config the CLI would assemble for this run (DefaultBaseUrl is a URL, never a secret).
                var config = new GenerationConfig(
                    new OpenApiSourceLocation.File(Path.Combine(FixturesDirectory, "valid-vista-document.json")),
                    OutputDirectory: "out",
                    EmitWriteFacets: emitWrite,
                    DefaultBaseUrl: defaultBaseUrl);

                // Assemble the full generated output for this case.
                var files = new List<GeneratedFile>(runtimeFiles);
                files.Add(config.EmitWriteFacets ? fixtureTypesWithWrite : fixtureTypesReadOnly);
                files.Add(fixtureFilterNode);
                files.AddRange(fixtureViews);
                files.AddRange(EmitViewsFor(specs)); // varied view/facet shapes

                foreach (var file in files)
                {
                    var violation = FindEmbeddedSecret(file.Content);
                    if (violation is not null)
                    {
                        throw new Exception(
                            $"Generated file '{file.RelativePath}' embeds a credential/secret " +
                            $"(Requirement 7.1): {violation}. Case: EmitWriteFacets={emitWrite}, " +
                            $"DefaultBaseUrl={defaultBaseUrl ?? "<none>"}, views=[{string.Join(", ", specs.Select(s => s.Name))}].");
                    }

                    // No credential is ever derived from config: a supplied DefaultBaseUrl (a URL) does not
                    // appear baked into any generated file.
                    if (config.DefaultBaseUrl is not null
                        && file.Content.Contains(config.DefaultBaseUrl, StringComparison.Ordinal))
                    {
                        throw new Exception(
                            $"Generated file '{file.RelativePath}' baked in the configured DefaultBaseUrl " +
                            $"'{config.DefaultBaseUrl}'; the generator must not derive any embedded value from " +
                            "config into the output (Requirement 7.1).");
                    }
                }
            },
            iter: Iterations);
    }

    // ---- Secret detection ----------------------------------------------------------------------------

    /// <summary>
    /// Scans a file's content for an embedded credential/secret and returns a human-readable description of
    /// the first violation found, or <c>null</c> when the content is clean.
    /// </summary>
    private static string? FindEmbeddedSecret(string content)
    {
        // (1) No hard-coded bearer token: after "Bearer " only a template `${…}` or a `<…>` placeholder is
        // allowed; a concrete literal token fails.
        const string bearer = "Bearer ";
        for (var index = content.IndexOf(bearer, StringComparison.Ordinal);
             index >= 0;
             index = content.IndexOf(bearer, index + bearer.Length, StringComparison.Ordinal))
        {
            var afterIndex = index + bearer.Length;
            if (afterIndex >= content.Length)
            {
                continue;
            }

            var next = content[afterIndex];
            if (next is not ('$' or '<'))
            {
                var snippet = content.Substring(index, Math.Min(32, content.Length - index));
                return $"a hard-coded token follows a 'Bearer ' header (\"{snippet}\"); only the "
                    + "`Bearer ${token}` template is permitted";
            }
        }

        // (2) No secret-named identifier assigned/keyed to a non-empty quoted string literal.
        var assignment = SecretAssignmentPattern.Match(content);
        if (assignment.Success)
        {
            return $"a secret-named identifier is assigned a literal value (\"{assignment.Value.Trim()}\")";
        }

        // (3) No JWT-looking literal.
        var jwt = JwtPattern.Match(content);
        if (jwt.Success)
        {
            return $"a JWT-looking literal is present (\"{Truncate(jwt.Value, 40)}\")";
        }

        // (4) No high-entropy base64/hex blob string literal (a plausible embedded secret). URLs, paths, and
        // media types contain '/'/':' and are excluded; short words and lower-case identifiers lack the
        // digit/letter entropy the blob patterns require.
        foreach (Match literal in StringLiteralPattern.Matches(content))
        {
            var value = literal.Groups[1].Success ? literal.Groups[1].Value : literal.Groups[2].Value;
            if (value.Length < 24 || value.Contains('/') || value.Contains(':'))
            {
                continue;
            }

            if (Base64BlobPattern.IsMatch(value) || HexBlobPattern.IsMatch(value))
            {
                return $"a high-entropy blob string literal is present (\"{Truncate(value, 40)}\")";
            }
        }

        return null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    // ---- Fixture assembly ----------------------------------------------------------------------------

    private static ResolvedDocument ResolveFixture()
    {
        var raw = File.ReadAllText(Path.Combine(FixturesDirectory, "valid-vista-document.json"));

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

    private static GeneratedFile EmitFixtureTypes(ResolvedDocument document, bool includeWriteEnvelopes)
    {
        var notices = new NoticeCollector();

        var envelopes = new EnvelopeCatalog().Bind(document, includeWriteEnvelopes);
        if (envelopes.IsError)
        {
            throw new Exception($"Fixture envelope binding failed: {envelopes.Error.Message}");
        }

        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(document, notices);
        var input = new TypesEmitInput(document, envelopes.Value, reLift, new[] { "CustomerRow" }, notices);

        var result = TypesEmitter.Emit(input);
        if (result.IsError)
        {
            throw new Exception($"Fixture types.ts emit failed: {result.Error.Message}");
        }

        return result.Value;
    }

    private static GeneratedFile EmitFixtureFilterNode(ResolvedDocument document)
    {
        var model = new FilterNodeModelBuilder().Build(document, new NoticeCollector());
        if (model.IsError)
        {
            throw new Exception($"Fixture filter-node model build failed: {model.Error.Message}");
        }

        return FilterNodeEmitter.Emit(model.Value);
    }

    private static IReadOnlyList<GeneratedFile> EmitFixtureViews(ResolvedDocument document)
    {
        var notices = new NoticeCollector();
        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(document, notices);
        var views = new OperationGraphBuilder().Build(document, reLift, notices);
        return ViewClientEmitter.EmitAll(views);
    }

    // Emits the per-view clients for a randomly generated document (mixed writable/read-only views), giving
    // the property varied view/facet shapes beyond the single fixture view.
    private static IReadOnlyList<GeneratedFile> EmitViewsFor(IReadOnlyList<ViewSpec> specs)
    {
        var document = BuildDocument(specs);

        var resolved = RefResolver.Resolve(document);
        if (resolved.IsError)
        {
            throw new Exception($"A well-formed document must resolve: {resolved.Error.Message}");
        }

        var notices = new NoticeCollector();
        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(resolved.Value, notices);
        var views = new OperationGraphBuilder().Build(resolved.Value, reLift, notices);
        return ViewClientEmitter.EmitAll(views);
    }

    // ---- Record-document builders (mirrors the write-facet gating property's minimal builder) ---------

    private static string RefValue(string name) => SchemaRefPrefix + name;

    private static OpenApiSchema Ref(string name) =>
        new(RefValue(name), null, null, false, Array.Empty<string>(), null, null, null, null, false);

    private static OpenApiSchema Component() =>
        new(
            null,
            "object",
            null,
            false,
            Array.Empty<string>(),
            new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["leaf"] = new(null, "string", null, false, Array.Empty<string>(), null, null, null, null, false),
            },
            null,
            null,
            null,
            false);

    private static OpenApiOperation Operation(string operationId, string? requestBodyRef, bool tokenBearing)
    {
        OpenApiRequestBody? requestBody = requestBodyRef is null
            ? null
            : new OpenApiRequestBody(true, Ref(requestBodyRef));

        var responses = new Dictionary<string, OpenApiResponse>(StringComparer.Ordinal)
        {
            ["200"] = new OpenApiResponse(null),
        };

        if (tokenBearing)
        {
            responses["428"] = new OpenApiResponse(null);
            responses["409"] = new OpenApiResponse(null);
        }

        return new OpenApiOperation(operationId, requestBody, responses, Array.Empty<OpenApiSecurityRequirement>());
    }

    private static OpenApiDocument Document(
        IReadOnlyDictionary<string, OpenApiSchema> schemas,
        IReadOnlyDictionary<string, OpenApiPathItem> paths) =>
        new(
            "3.0.4",
            new OpenApiInfo("a2n.Vista API", "1.0.0"),
            paths,
            new OpenApiComponents(schemas, new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal)),
            Array.Empty<OpenApiSecurityRequirement>());

    private static OpenApiDocument BuildDocument(IReadOnlyList<ViewSpec> specs)
    {
        var schemas = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
        {
            [ListRequestBodyName] = Component(),
        };
        var paths = new Dictionary<string, OpenApiPathItem>(StringComparer.Ordinal);

        foreach (var spec in specs)
        {
            var root = "/api/views/" + spec.Name;

            void AddFacet(string suffix, string method, string? requestBodyRef, bool tokenBearing)
            {
                var operationId = spec.Name + "_" + suffix;
                var operations = new Dictionary<string, OpenApiOperation>(StringComparer.Ordinal)
                {
                    [method] = Operation(operationId, requestBodyRef, tokenBearing),
                };
                paths[root + "/" + suffix] = new OpenApiPathItem(operations);
            }

            AddFacet(OperationGraphBuilder.ListSuffix, "post", ListRequestBodyName, tokenBearing: false);
            AddFacet(OperationGraphBuilder.MetadataSuffix, "get", requestBodyRef: null, tokenBearing: false);

            if (spec.Writable)
            {
                var crudName = "Crud_" + spec.Name;
                schemas[crudName] = Component();

                AddFacet(OperationGraphBuilder.CreateSuffix, "post", crudName, tokenBearing: false);
                AddFacet(OperationGraphBuilder.UpdateSuffix, "post", crudName, tokenBearing: true);
                AddFacet(OperationGraphBuilder.DeleteSuffix, "post", requestBodyRef: null, tokenBearing: true);
            }
        }

        return Document(schemas, paths);
    }

    /// <summary>A generated view spec: a distinct view name and whether the view is writable.</summary>
    private sealed record ViewSpec(string Name, bool Writable);
}
