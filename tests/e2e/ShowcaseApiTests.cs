using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>HTTP checks that exist only on the AG Grid / DataTables showcase host.</summary>
public sealed class ShowcaseApiTests
{
    [Test]
    public async Task Catalog_Lists_Only_Registered_Views()
    {
        var response = await SampleSession.Http.GetAsync(SampleSession.Showcase, "/api/showcase/views");

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        var names = json.RootElement.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToArray();
        await Assert.That(names).IsEquivalentTo(new[] { "vProductCategory", "vOrderDetail", "vOrder", "vWritableMemo" });
    }

    [Test]
    public async Task Northwind_Host_Has_No_Catalog_Route()
    {
        var response = await SampleSession.Http.GetAsync(SampleSession.Northwind, "/api/showcase/views");
        await Assert.That(response.Status).IsEqualTo(404);
    }

    [Test]
    public async Task AgGrid_Protocol_Returns_RowData()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            "/api/views/vProductCategory/aggrid",
            """{"startRow":0,"endRow":2,"sortModel":[{"colId":"ProductName","sort":"asc"}],"filterModel":{}}""");

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("rowCount").GetInt32()).IsEqualTo(77);
        var rows = json.RootElement.GetProperty("rowData");
        await Assert.That(rows.GetArrayLength()).IsEqualTo(2);
        await Assert.That(rows[0].GetProperty("productName").GetString()).IsEqualTo("Alice Mutton");
    }

    [Test]
    public async Task AgGrid_Unknown_ColId_Is_400()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            "/api/views/vProductCategory/aggrid",
            """{"startRow":0,"endRow":2,"sortModel":[{"colId":"notAField","sort":"asc"}],"filterModel":{}}""");

        await Assert.That(response.Status).IsEqualTo(400);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo("filter-unknown-field");
        await Assert.That(json.RootElement.GetProperty("field").GetString()).IsEqualTo("notAField");
    }

    [Test]
    public async Task DataTables_Protocol_On_The_Showcase()
    {
        var response = await SampleSession.Http.PostFormAsync(
            SampleSession.Showcase,
            "/api/views/vProductCategory/datatable",
            [
                new("draw", "1"),
                new("start", "0"),
                new("length", "2"),
                new("columns[0][data]", "ProductName"),
                new("order[0][column]", "0"),
                new("order[0][dir]", "asc"),
            ]);

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("recordsFiltered").GetInt32()).IsEqualTo(77);
        await Assert.That(json.RootElement.GetProperty("data")[0].GetProperty("productName").GetString())
            .IsEqualTo("Alice Mutton");
    }

    [Test]
    public async Task Order_View_List_And_Export()
    {
        var list = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            "/api/views/vOrder/list",
            """{"page":0,"pageSize":1}""");
        await Assert.That(list.Status).IsEqualTo(200);
        using var json = list.Json();
        await Assert.That(json.RootElement.GetProperty("page").GetProperty("totalRows").GetInt32()).IsGreaterThan(0);

        var csv = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            "/api/views/vOrder/export",
            """{"page":0,"pageSize":1,"format":"csv"}""");
        await Assert.That(csv.Status).IsEqualTo(200);
        await Assert.That(csv.ContentType).Contains("text/csv");
        await Assert.That(csv.Text).Contains("Order Id");
    }

    [Test]
    public async Task OpenApi_Document_Includes_The_Fourth_View()
    {
        var response = await SampleSession.Http.GetAsync(SampleSession.Showcase, "/openapi/v1.json");

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("openapi").GetString()).IsEqualTo("3.0.4");
        await Assert.That(json.RootElement.GetProperty("paths").EnumerateObject().Count()).IsEqualTo(19);
    }

    [Test]
    public async Task ReadOnly_Writes_On_The_Showcase_Are_Not_Mapped()
    {
        foreach (var action in new[] { "create", "update", "delete" })
        {
            var response = await SampleSession.Http.PostJsonAsync(
                SampleSession.Showcase,
                "/api/views/vProductCategory/" + action,
                """{"key":1,"model":{"productName":"nope"}}""");
            await Assert.That(response.Status).IsEqualTo(404);
        }
    }
}
