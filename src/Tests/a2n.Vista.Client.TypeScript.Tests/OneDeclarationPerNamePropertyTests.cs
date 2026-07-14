// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Property-based test for the one-declaration-per-name invariant of the type emitters (task 7.7;
/// Requirements 2.1, 2.5; design Property 2 — "Each generated type is declared once and referenced by
/// name"). Requirement 2.1 requires the generator to emit <em>exactly one</em> TypeScript type declaration
/// per envelope/DTO, and Requirement 2.5 requires each distinct generated type to be emitted exactly once
/// and referenced <em>by name</em> from every declaration that uses it.
/// </summary>
/// <remarks>
/// <para>
/// The fixture (<c>valid-vista-document.json</c>) is a single fixed document, so the property is made
/// universal by generating <b>variation in the emit inputs</b> the way the buffered pipeline (task 12.2)
/// assembles them: the per-view DTO component names (<c>TypesEmitInput.DtoComponentNames</c>) are shuffled
/// and duplicated, and the write-envelope surface is toggled. Across every such permutation the emitted
/// output set (<c>types.ts</c> + <c>filter-node.ts</c>) must still satisfy the invariant. Two independent
/// facts are asserted:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Declared exactly once (Requirement 2.1).</b> Scanning the combined output for every top-level
///     <c>export interface X</c> / <c>export type X</c>, each distinct declared name appears exactly once —
///     even when <c>DtoComponentNames</c> contains duplicates or is permuted, no name is ever declared
///     twice, and no <c>ViewListResult_*</c> monomorphized component is emitted (it is re-lifted to the
///     single generic, referenced by name).
///   </item>
///   <item>
///     <b>Referenced by name (Requirement 2.5).</b> Every named (PascalCase) type reference that appears in
///     a member/union position of a file resolves by name to a type that is either declared in that same
///     file or imported by name (<c>import type { … } from "./filter-node"</c>). Nothing is inlined or
///     duplicated in place of a name; the single generic row type parameter <c>TRow</c> is the only
///     non-declared identifier permitted.
///   </item>
/// </list>
/// <para>
/// The generated <c>DtoComponentNames</c> are drawn only from components that are present in the document
/// (the read-surface envelopes and the one <c>CustomerRow</c> DTO), so every case is a legitimate,
/// non-fatal emit input; the canonical DTO names the operation graph derives are additionally verified to be
/// declared exactly once, tying the property to the real pipeline derivation.
/// </para>
/// </remarks>
public sealed class OneDeclarationPerNamePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>The single generic row type parameter — the only non-declared identifier a reference may name.</summary>
    private const string RowTypeParameter = "TRow";

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    // Matches a top-level interface/type declaration, capturing the declared name (the capture naturally
    // stops before any generic parameter list or brace).
    private static readonly Regex DeclarationPattern =
        new(@"^export (?:interface|type) ([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    // Extracts the names inside an `import type { A, B, C } from "…";` line.
    private static readonly Regex ImportPattern =
        new(@"^import type \{ ([^}]*) \} from", RegexOptions.Compiled);

    // A PascalCase identifier — the shape every generated Vista type name takes (primitives such as
    // string/number/boolean/unknown/null are lower-case and never match).
    private static readonly Regex NamedReferencePattern =
        new(@"\b[A-Z][A-Za-z0-9_]*\b", RegexOptions.Compiled);

    // A double-quoted string literal (e.g. a FilterOperator union member "Equals" or a module specifier),
    // stripped before scanning for named references so literal content is never read as a type name.
    private static readonly Regex StringLiteralPattern =
        new("\"[^\"]*\"", RegexOptions.Compiled);

    /// <summary>
    /// The pool of present, dedupe-safe component names a generated <c>DtoComponentNames</c> multiset draws
    /// from: the read-surface envelopes (already bound, so they dedupe against the envelope bindings) and the
    /// one real per-view DTO. Deliberately excludes the <c>FilterNode</c> family (declared cross-file in
    /// <c>filter-node.ts</c>) and the monomorphized <c>ViewListResult_*</c> components (re-lifted), which are
    /// not legitimate per-view DTO inputs.
    /// </summary>
    private static readonly string[] Pool =
    {
        EnvelopeCatalog.VistaSortBody,
        EnvelopeCatalog.VistaListRequestBody,
        EnvelopeCatalog.VistaDetailRequestBody,
        EnvelopeCatalog.VistaMetadataResponse,
        EnvelopeCatalog.VistaFieldMetadataResponse,
        EnvelopeCatalog.ProblemDetails,
        "CustomerRow",
    };

    /// <summary>
    /// A permuted, possibly-duplicated multiset of present DTO component names. Zero-to-eight names are drawn
    /// from <see cref="Pool"/> in random order (yielding permutations and duplicates), then the real DTO
    /// (<c>CustomerRow</c>) is appended so the per-view row type is always emitted.
    /// </summary>
    private static readonly Gen<string[]> DtoComponentNames =
        from extras in Gen.OneOfConst(Pool).Array[0, 8]
        select extras.Append("CustomerRow").ToArray();

    /// <summary>An emit-input variation: the permuted/duplicated DTO names and whether the write surface is bound.</summary>
    private static readonly Gen<(string[] DtoNames, bool IncludeWrite)> Cases =
        from dtoNames in DtoComponentNames
        from includeWrite in Gen.Bool
        select (dtoNames, includeWrite);

    // Feature: typescript-client, Property 2: Each generated type is declared once and referenced by name
    //
    // For any permutation/duplication of the per-view DTO component names and either write-surface toggle,
    // the emitted output set (types.ts + filter-node.ts) declares each distinct type exactly once
    // (Requirement 2.1) and references every named type by name — declared in the file or imported by name
    // (Requirement 2.5). No name is ever declared twice, and no monomorphized ViewListResult_* component is
    // emitted (it is re-lifted to the single generic, referenced by name).
    //
    // Validates: Requirements 2.1, 2.5
    [Test]
    public void Every_Generated_Type_Is_Declared_Once_And_Referenced_By_Name()
    {
        ResolvedDocument document = ResolveFixture();

        // The DTO names the operation graph actually derives for the fixture — tied to the real pipeline so
        // the property is not merely about hand-picked inputs.
        IReadOnlyList<string> canonicalDtoNames = DeriveCanonicalDtoNames(document);

        Cases.Sample(
            testCase =>
            {
                (string[] dtoNames, bool includeWrite) = testCase;

                string[] files = EmitTypeFiles(document, dtoNames, includeWrite);
                string combined = string.Join("\n", files);

                // (1) Declared exactly once (Requirement 2.1): no declared name repeats across the output.
                var declarationCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (string name in ScanDeclarations(combined))
                {
                    declarationCounts[name] = declarationCounts.GetValueOrDefault(name) + 1;
                }

                foreach ((string name, int count) in declarationCounts)
                {
                    if (count != 1)
                    {
                        throw new Exception(
                            $"Type '{name}' was declared {count} times across the emitted output; each " +
                            "generated type must be declared exactly once (Requirement 2.1). DtoComponentNames " +
                            $"= [{string.Join(", ", dtoNames)}], includeWrite = {includeWrite}.");
                    }
                }

                // The monomorphized list envelope is re-lifted to the single generic, never emitted verbatim.
                if (declarationCounts.Keys.Any(name => name.StartsWith("ViewListResult_", StringComparison.Ordinal)))
                {
                    throw new Exception(
                        "A monomorphized 'ViewListResult_*' component was declared; it must be re-lifted to the " +
                        "single generic 'ViewListResult<TRow>' and referenced by name (Requirement 2.5/2.6).");
                }

                // The canonical DTO(s) the operation graph derives are declared (exactly once, per the check
                // above) — the property holds for the real pipeline derivation, not just arbitrary inputs.
                foreach (string canonical in canonicalDtoNames)
                {
                    if (!declarationCounts.ContainsKey(canonical))
                    {
                        throw new Exception(
                            $"The operation-graph-derived DTO '{canonical}' was not declared in the output.");
                    }
                }

                // (2) Referenced by name (Requirement 2.5): every named reference in a file resolves to a
                // type declared in that file or imported by name — nothing is inlined in place of a name.
                foreach (string file in files)
                {
                    var declaredHere = new HashSet<string>(ScanDeclarations(file), StringComparer.Ordinal);
                    var importedHere = new HashSet<string>(ScanImports(file), StringComparer.Ordinal);

                    foreach (string reference in ScanNamedReferences(file))
                    {
                        if (string.Equals(reference, RowTypeParameter, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!declaredHere.Contains(reference) && !importedHere.Contains(reference))
                        {
                            throw new Exception(
                                $"Named type '{reference}' is referenced but is neither declared in the file " +
                                "nor imported by name; every generated type must be referenced by name " +
                                "(Requirement 2.5). DtoComponentNames = " +
                                $"[{string.Join(", ", dtoNames)}], includeWrite = {includeWrite}.");
                        }
                    }
                }
            },
            iter: Iterations);
    }

    // Emits types.ts and filter-node.ts from the fixture with the supplied DTO-name variation, asserting the
    // upstream binding/emit steps all succeed (they must, for present inputs).
    private static string[] EmitTypeFiles(ResolvedDocument document, string[] dtoNames, bool includeWrite)
    {
        var notices = new NoticeCollector();

        var envelopes = new EnvelopeCatalog().Bind(document, includeWrite);
        if (envelopes.IsError)
        {
            throw new Exception($"Envelope binding failed unexpectedly: {envelopes.Error.Message}");
        }

        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(document, notices);

        var typesInput = new TypesEmitInput(document, envelopes.Value, reLift, dtoNames, notices);
        var typesResult = TypesEmitter.Emit(typesInput);
        if (typesResult.IsError)
        {
            throw new Exception($"types.ts emit failed unexpectedly: {typesResult.Error.Message}");
        }

        var filterModel = new FilterNodeModelBuilder().Build(document, notices);
        if (filterModel.IsError)
        {
            throw new Exception($"filter-node model build failed unexpectedly: {filterModel.Error.Message}");
        }

        var filterFile = FilterNodeEmitter.Emit(filterModel.Value);

        return new[] { typesResult.Value.Content, filterFile.Content };
    }

    // Derives the DTO component names the operation graph binds for the fixture (each view's RowType/CrudType
    // named references), mirroring how the pipeline populates TypesEmitInput.DtoComponentNames.
    private static IReadOnlyList<string> DeriveCanonicalDtoNames(ResolvedDocument document)
    {
        var notices = new NoticeCollector();
        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(document, notices);
        var views = new OperationGraphBuilder().Build(document, reLift, notices);

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var view in views)
        {
            if (view.RowType is TsNamed row)
            {
                names.Add(row.Name);
            }

            if (view.CrudType is TsNamed crud)
            {
                names.Add(crud.Name);
            }
        }

        return names.ToList();
    }

    // Scans emitted TypeScript for every top-level `export interface X` / `export type X` declared name.
    private static IEnumerable<string> ScanDeclarations(string content)
    {
        foreach (string line in content.Split('\n'))
        {
            var match = DeclarationPattern.Match(line);
            if (match.Success)
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    // Scans emitted TypeScript for the names brought in by `import type { … } from …` lines.
    private static IEnumerable<string> ScanImports(string content)
    {
        foreach (string line in content.Split('\n'))
        {
            var match = ImportPattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            foreach (string name in match.Groups[1].Value.Split(','))
            {
                var trimmed = name.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }
    }

    // Scans emitted TypeScript for every PascalCase named-type reference, skipping comment lines (the file
    // headers) so descriptive prose never masquerades as a type reference.
    private static IEnumerable<string> ScanNamedReferences(string content)
    {
        foreach (string line in content.Split('\n'))
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            // Strip string-literal content (union literals, module specifiers) so only genuine type
            // references remain.
            var stripped = StringLiteralPattern.Replace(line, "\"\"");

            foreach (Match match in NamedReferencePattern.Matches(stripped))
            {
                yield return match.Value;
            }
        }
    }

    // Parses and resolves the canonical fixture document (mirrors the other emitter tests' setup).
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
}
