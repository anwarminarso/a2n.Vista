using System;
using System.Linq;
using a2n.Vista.OpenApi;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Example coverage for <see cref="VistaOpenApiOptions"/> and the facet→operation table (spec
/// openapi-emitter, task 5.1; Requirements 8.4, 11.2, 12.1). Asserts the safe defaults, registration-time
/// validation, and that the fixed table encodes exactly the seven core facets with correct methods, path
/// suffixes, and write-facet gating — the single endpoint-parity source task 5.2 iterates.
/// </summary>
public sealed class OpenApiOptionsAndFacetTableTests
{
    // ---- VistaOpenApiOptions defaults -------------------------------------------------------------

    [Test]
    public async Task Options_Have_Safe_Defaults()
    {
        var options = new VistaOpenApiOptions();

        await Assert.That(options.DocumentTitle).IsEqualTo("a2n.Vista API");
        await Assert.That(options.DocumentVersion).IsNull();
        await Assert.That(options.OpenApiVersion).IsEqualTo("3.0.4");
        await Assert.That(options.EndpointPath).IsEqualTo("/openapi/v1.json");
        await Assert.That(options.Security).IsNull();
        await Assert.That(options.IncludeAdapterEndpoints).IsFalse();
    }

    [Test]
    public async Task Default_Options_Validate_Without_Throwing()
    {
        var options = new VistaOpenApiOptions();
        await Assert.That(options.Validate).ThrowsNothing();
    }

    // ---- Validate() rejects invalid options -------------------------------------------------------

    [Test]
    public async Task Validate_Rejects_Empty_Title()
    {
        var options = new VistaOpenApiOptions { DocumentTitle = "  " };
        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task Validate_Rejects_Empty_OpenApiVersion()
    {
        var options = new VistaOpenApiOptions { OpenApiVersion = "" };
        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("2.0")]
    [Arguments("4.0.0")]
    [Arguments("3")]
    [Arguments("bogus")]
    public async Task Validate_Rejects_Non_3x_OpenApiVersion(string version)
    {
        var options = new VistaOpenApiOptions { OpenApiVersion = version };
        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("3.0.4")]
    [Arguments("3.1.0")]
    public async Task Validate_Accepts_3x_OpenApiVersion(string version)
    {
        var options = new VistaOpenApiOptions { OpenApiVersion = version };
        await Assert.That(options.Validate).ThrowsNothing();
    }

    [Test]
    public async Task Validate_Rejects_Empty_EndpointPath()
    {
        var options = new VistaOpenApiOptions { EndpointPath = "" };
        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task Validate_Rejects_Relative_EndpointPath()
    {
        var options = new VistaOpenApiOptions { EndpointPath = "openapi/v1.json" };
        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    // ---- Facet→operation table --------------------------------------------------------------------

    [Test]
    public async Task Table_Contains_Exactly_The_Seven_Core_Facets_In_Order()
    {
        var facets = FacetOperations.All.Select(operation => operation.Facet).ToArray();

        await Assert.That(facets).IsEquivalentTo(new[]
        {
            Facet.List, Facet.Detail, Facet.Metadata, Facet.Export,
            Facet.Create, Facet.Update, Facet.Delete,
        });
    }

    [Test]
    public async Task Only_Metadata_Is_Get_The_Rest_Are_Post()
    {
        foreach (var operation in FacetOperations.All)
        {
            var expected = operation.Facet == Facet.Metadata ? "GET" : "POST";
            await Assert.That(operation.HttpMethod).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Path_Suffixes_Match_The_Facet_Names()
    {
        foreach (var operation in FacetOperations.All)
        {
            var expected = operation.Facet.ToString().ToLowerInvariant();
            await Assert.That(operation.PathSuffix).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Create_Update_Delete_Are_The_Only_Writable_Only_Facets()
    {
        var writable = FacetOperations.All
            .Where(operation => operation.IsWriteFacet)
            .Select(operation => operation.Facet)
            .ToArray();

        await Assert.That(writable).IsEquivalentTo(new[] { Facet.Create, Facet.Update, Facet.Delete });
    }

    [Test]
    public async Task ForView_ReadOnly_Yields_Only_The_Four_Read_Facets()
    {
        var facets = FacetOperations.ForView(isReadOnly: true).Select(operation => operation.Facet).ToArray();

        await Assert.That(facets).IsEquivalentTo(new[]
        {
            Facet.List, Facet.Detail, Facet.Metadata, Facet.Export,
        });
    }

    [Test]
    public async Task ForView_Writable_Yields_All_Seven_Facets()
    {
        var facets = FacetOperations.ForView(isReadOnly: false).Select(operation => operation.Facet).ToArray();
        await Assert.That(facets.Length).IsEqualTo(7);
    }

    [Test]
    public async Task Metadata_Has_No_Request_Body_And_Body_Facets_Carry_400()
    {
        foreach (var operation in FacetOperations.All)
        {
            if (operation.Facet == Facet.Metadata)
            {
                await Assert.That(operation.HasRequestBody).IsFalse();
                await Assert.That(operation.AlwaysErrorCodes).DoesNotContain(400);
            }
            else
            {
                await Assert.That(operation.HasRequestBody).IsTrue();
                await Assert.That(operation.AlwaysErrorCodes).Contains(400);
            }
        }
    }

    [Test]
    public async Task NotFound_Is_On_Detail_Create_Update_Delete()
    {
        foreach (var operation in FacetOperations.All)
        {
            var expectsNotFound = operation.Facet is Facet.Detail or Facet.Create or Facet.Update or Facet.Delete;
            var has404 = operation.AlwaysErrorCodes.Contains(404);
            await Assert.That(has404).IsEqualTo(expectsNotFound);
        }
    }

    [Test]
    public async Task Concurrency_Errors_Gated_Only_On_Update_And_Delete()
    {
        foreach (var operation in FacetOperations.All)
        {
            var expected = operation.Facet is Facet.Update or Facet.Delete;
            await Assert.That(operation.ConcurrencyErrorsWhenTokenDeclared).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Forbidden_Applies_To_Every_Facet()
    {
        foreach (var operation in FacetOperations.All)
        {
            await Assert.That(operation.ForbiddenWhenNotAnonymous).IsTrue();
        }
    }
}
