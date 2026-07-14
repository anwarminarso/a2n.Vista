using System.Text;
using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Unit tests for the parse-stage handling of malformed documents (Requirement 1.6, task 4.3).
/// <para>
/// Two shapes of malformation are covered:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Truncated / invalid JSON</b> — the bytes are not well-formed JSON at all. The parser must
///     catch the underlying <see cref="System.Text.Json.JsonException"/> and degrade to
///     <see cref="ParseError.Malformed"/>, populating both <c>Location</c> and <c>Detail</c> so the
///     failure site and nature are reportable.
///   </item>
///   <item>
///     <b>Structurally malformed but valid JSON</b> — the bytes parse as JSON but do not describe an
///     OpenAPI document (a non-object root, or an object without the required <c>openapi</c> field). The
///     parser must report <see cref="ParseError.Malformed"/> at a location that points at the problem
///     (<c>"$"</c> for a bad root, <c>"openapi"</c> for the missing version field).
///   </item>
/// </list>
/// <para>
/// Every case asserts the result <see cref="Result{T, E}.IsError"/> (no document produced) and that the
/// parser never throws for these expected, malformed inputs.
/// </para>
/// </summary>
public sealed class OpenApiParserMalformedTests
{
    /// <summary>The directory the fixture documents are copied into, alongside the test assembly.</summary>
    private static string FixturesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Test]
    public async Task Parse_TruncatedJsonFixture_ReturnsMalformed_WithLocationAndDetail_NoDocument()
    {
        // The shipped malformed fixture is a document truncated mid-structure (unterminated JSON).
        var path = Path.Combine(FixturesDirectory, "malformed-document.json");
        var raw = await File.ReadAllBytesAsync(path);

        var result = OpenApiParser.Parse(raw);

        // No document is produced for a malformed input.
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<ParseError.Malformed>();

        var malformed = (ParseError.Malformed)result.Error;
        // Both the location and the nature of the failure must be populated so the report is actionable.
        await Assert.That(malformed.Location).IsNotNullOrEmpty();
        await Assert.That(malformed.Detail).IsNotNullOrEmpty();
        // The English stderr message identifies this as a malformed-document failure.
        await Assert.That(malformed.Message).Contains("Malformed OpenAPI document");
    }

    [Test]
    public async Task Parse_InlineTruncatedJson_ReturnsMalformed_WithLocationAndDetail()
    {
        // An object that is opened but never closed: invalid JSON the strict parser must reject.
        const string truncated = "{ \"openapi\": \"3.0.4\", \"paths\": {";

        var result = OpenApiParser.Parse(truncated);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<ParseError.Malformed>();

        var malformed = (ParseError.Malformed)result.Error;
        await Assert.That(malformed.Location).IsNotNullOrEmpty();
        await Assert.That(malformed.Detail).IsNotNullOrEmpty();
    }

    [Test]
    public async Task Parse_GarbageBytes_ReturnsMalformed()
    {
        // Not JSON at all.
        const string garbage = "this is not json at all: <<<>>>";

        var result = OpenApiParser.Parse(garbage);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<ParseError.Malformed>();
        await Assert.That(((ParseError.Malformed)result.Error).Detail).IsNotNullOrEmpty();
    }

    [Test]
    public async Task Parse_JsonArrayRoot_ReturnsMalformed_AtDollarRoot()
    {
        // Well-formed JSON, but the root is an array rather than the required OpenAPI object.
        const string arrayRoot = "[ { \"openapi\": \"3.0.4\" } ]";

        var result = OpenApiParser.Parse(arrayRoot);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<ParseError.Malformed>();

        var malformed = (ParseError.Malformed)result.Error;
        // A bad root is reported at the document root location "$".
        await Assert.That(malformed.Location).IsEqualTo("$");
        await Assert.That(malformed.Detail).IsNotNullOrEmpty();
    }

    [Test]
    public async Task Parse_JsonScalarRoot_ReturnsMalformed_AtDollarRoot()
    {
        // Well-formed JSON, but the root is a bare string — still not an OpenAPI object.
        const string scalarRoot = "\"3.0.4\"";

        var result = OpenApiParser.Parse(scalarRoot);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<ParseError.Malformed>();
        await Assert.That(((ParseError.Malformed)result.Error).Location).IsEqualTo("$");
    }

    [Test]
    public async Task Parse_ObjectMissingOpenApiField_ReturnsMalformed_AtOpenApiLocation()
    {
        // A valid JSON object that is a plausible document but omits the required 'openapi' version field.
        const string missingVersion =
            "{ \"info\": { \"title\": \"a2n.Vista API\", \"version\": \"1.0.0\" }, \"paths\": {} }";

        var result = OpenApiParser.Parse(missingVersion);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<ParseError.Malformed>();

        var malformed = (ParseError.Malformed)result.Error;
        // The missing version is reported at the "openapi" location, naming the offending field.
        await Assert.That(malformed.Location).IsEqualTo("openapi");
        await Assert.That(malformed.Detail).Contains("openapi");
    }

    [Test]
    public async Task Parse_OpenApiFieldNotAString_ReturnsMalformed_AtOpenApiLocation()
    {
        // The 'openapi' member is present but has the wrong JSON type (a number, not a version string).
        const string numericVersion = "{ \"openapi\": 3.04, \"paths\": {} }";

        var result = OpenApiParser.Parse(numericVersion);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error).IsTypeOf<ParseError.Malformed>();
        await Assert.That(((ParseError.Malformed)result.Error).Location).IsEqualTo("openapi");
    }

    [Test]
    public async Task Parse_MalformedInputs_NeverThrow()
    {
        // A spread of malformed inputs: empty, whitespace, truncated, garbage, wrong root kinds, and the
        // structurally-invalid-but-valid-JSON cases. None may escape as an exception (Requirement 1.6).
        string[] malformedInputs =
        {
            string.Empty,
            "   ",
            "{",
            "{ \"openapi\": ",
            "not json",
            "[]",
            "42",
            "true",
            "null",
            "{ \"info\": {} }",
            "{ \"openapi\": 3.0 }",
        };

        foreach (var input in malformedInputs)
        {
            // The call itself must not throw; it must always yield a Result.
            var stringResult = OpenApiParser.Parse(input);
            await Assert.That(stringResult.IsError).IsTrue();
            await Assert.That(stringResult.Error).IsTypeOf<ParseError.Malformed>();

            // The byte-based overload takes the same path and must also stay exception-free.
            var byteResult = OpenApiParser.Parse(Encoding.UTF8.GetBytes(input));
            await Assert.That(byteResult.IsError).IsTrue();
        }
    }
}
