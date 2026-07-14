// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
/// Unit tests for the per-view DTO modeling step (task 7.4; Requirements 3.1–3.7). They assert the builder
/// turns a named object component into a <see cref="TsTypeDecl"/> whose members are mapped verbatim through
/// <see cref="TypeMapper"/> (case-sensitive names, nullable → <c>| null</c>, not-required → <c>?</c>, scalar
/// table), stored in the deterministic by-name order (Requirement 9.2); that a missing component aborts with
/// a fatal <see cref="GenerationError.MissingSchema"/> (Requirement 2.7); and that the by-name row/crud
/// binding helpers expose the references the operation-graph step (task 7.5) consumes.
/// </summary>
public sealed class DtoModelBuilderTests
{
    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

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

    private static string RenderedMember(TsTypeDecl decl, string propertyName) =>
        decl.Members.Single(member => member.Name == propertyName).Render();

    [Test]
    public async Task Builds_The_CustomerRow_Dto_Mapping_Each_Property_Verbatim()
    {
        var result = new DtoModelBuilder().BuildDecl("CustomerRow", ResolveFixture(), new NoticeCollector());

        await Assert.That(result.IsOk).IsTrue();

        var decl = result.Value;
        await Assert.That(decl.Name).IsEqualTo("CustomerRow");

        // Required scalars stay required; nullable + not-required members become optional `| null` unions.
        await Assert.That(RenderedMember(decl, "customerId")).IsEqualTo("customerId: string;");
        await Assert.That(RenderedMember(decl, "companyName")).IsEqualTo("companyName: string;");
        await Assert.That(RenderedMember(decl, "isActive")).IsEqualTo("isActive: boolean;");
        await Assert.That(RenderedMember(decl, "contactName")).IsEqualTo("contactName?: string | null;");
        await Assert.That(RenderedMember(decl, "country")).IsEqualTo("country?: string | null;");
    }

    [Test]
    public async Task Stores_Members_In_Deterministic_Ordinal_Order()
    {
        var decl = new DtoModelBuilder().BuildDecl("CustomerRow", ResolveFixture(), new NoticeCollector()).Value;

        // Ordinal, case-sensitive order by name, independent of the document's property order (Req 9.2).
        await Assert.That(decl.Members.Select(member => member.Name).ToArray())
            .IsEquivalentTo(new[] { "companyName", "contactName", "country", "customerId", "isActive" });
    }

    [Test]
    public async Task Renders_The_Whole_Interface_Declaration_Deterministically()
    {
        var decl = new DtoModelBuilder().BuildDecl("CustomerRow", ResolveFixture(), new NoticeCollector()).Value;

        const string expected =
            "export interface CustomerRow {\n" +
            "  companyName: string;\n" +
            "  contactName?: string | null;\n" +
            "  country?: string | null;\n" +
            "  customerId: string;\n" +
            "  isActive: boolean;\n" +
            "}";

        await Assert.That(decl.Render()).IsEqualTo(expected);
    }

    [Test]
    public async Task Missing_Component_Aborts_With_MissingSchema_Naming_It()
    {
        var result = new DtoModelBuilder().BuildDecl("NotAComponent", ResolveFixture(), new NoticeCollector());

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error is GenerationError.MissingSchema { SchemaName: "NotAComponent" }).IsTrue();
    }

    [Test]
    public async Task Degrades_A_Permissive_Member_To_Unknown_With_A_Notice_Never_Omitting_It()
    {
        // An inline permissive `{}` member is out of the scalar mapper's scope: it degrades to `unknown`
        // and records a non-fatal notice, but is never omitted and never fatal (Requirements 3.6/3.7).
        var permissive = new OpenApiSchema(
            null, null, null, false, Array.Empty<string>(), null, null, null, null, AdditionalPropertiesOpen: true);

        var schema = new OpenApiSchema(
            null,
            "object",
            null,
            false,
            new[] { "id" },
            new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal)
            {
                ["id"] = new(null, "string", null, false, Array.Empty<string>(), null, null, null, null, false),
                ["extra"] = permissive,
            },
            null,
            null,
            null,
            false);

        var notices = new NoticeCollector();
        var decl = new DtoModelBuilder().BuildDecl("WidgetRow", schema, notices);

        await Assert.That(RenderedMember(decl, "id")).IsEqualTo("id: string;");
        await Assert.That(RenderedMember(decl, "extra")).IsEqualTo("extra?: unknown;");
        await Assert.That(notices.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task BindView_Exposes_Row_And_Crud_References()
    {
        var writable = DtoModelBuilder.BindView("Customers", "CustomerRow", "CustomerCrud");
        await Assert.That(writable.ViewName).IsEqualTo("Customers");
        await Assert.That(writable.RowType.Render()).IsEqualTo("CustomerRow");
        await Assert.That(writable.CrudType!.Render()).IsEqualTo("CustomerCrud");

        // A read-only view has no crud reference.
        var readOnly = DtoModelBuilder.BindView("Orders", "OrderRow", null);
        await Assert.That(readOnly.CrudType is null).IsTrue();
        await Assert.That(readOnly.RowType.Render()).IsEqualTo("OrderRow");
    }
}
