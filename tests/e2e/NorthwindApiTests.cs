using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>Session hooks. <c>[BeforeEvery]</c> runs once for the whole assembly, not per class.</summary>
public static class E2ESessionHooks
{
    [BeforeEvery(HookType.TestSession)]
    [Timeout(300_000)]
    public static Task StartAsync(CancellationToken cancellationToken) => SampleSession.StartAsync(cancellationToken);

    [AfterEvery(HookType.TestSession)]
    [Timeout(60_000)]
    public static Task StopAsync(CancellationToken cancellationToken) => SampleSession.StopAsync(cancellationToken);
}

/// <summary>
/// HTTP checks against the real Northwind Kestrel host and the real <c>northwind.db</c>.
/// </summary>
public sealed class NorthwindApiTests
{
    private const string List = "/api/views/vProductCategory/list";

    [Test]
    public async Task List_Returns_A_Page_Of_Products()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            List,
            """{"page":0,"pageSize":2,"sort":[{"field":"ProductName","desc":false}]}""");

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        var page = json.RootElement.GetProperty("page");
        await Assert.That(page.GetProperty("totalRows").GetInt32()).IsEqualTo(77);
        await Assert.That(json.RootElement.GetProperty("totalRowsUnfiltered").GetInt32()).IsEqualTo(77);
        await Assert.That(page.GetProperty("pageIndex").GetInt32()).IsEqualTo(0);
        await Assert.That(page.GetProperty("pageSize").GetInt32()).IsEqualTo(2);
        await Assert.That(page.GetProperty("items").GetArrayLength()).IsEqualTo(2);
        await Assert.That(ItemName(page, 0)).IsEqualTo("Alice Mutton");
    }

    [Test]
    public async Task Paging_Second_Page_Is_A_Different_Window()
    {
        var first = await ListPageAsync(0);
        var second = await ListPageAsync(1);

        using var a = first.Json();
        using var b = second.Json();
        await Assert.That(a.RootElement.GetProperty("page").GetProperty("pageIndex").GetInt32()).IsEqualTo(0);
        await Assert.That(b.RootElement.GetProperty("page").GetProperty("pageIndex").GetInt32()).IsEqualTo(1);
        await Assert.That(a.RootElement.GetProperty("page").GetProperty("totalRows").GetInt32())
            .IsEqualTo(b.RootElement.GetProperty("page").GetProperty("totalRows").GetInt32());
        await Assert.That(ItemName(a.RootElement.GetProperty("page"), 0))
            .IsNotEqualTo(ItemName(b.RootElement.GetProperty("page"), 0));
    }

    [Test]
    public async Task Sort_Descending_Flips_The_First_Row()
    {
        var asc = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind, List,
            """{"page":0,"pageSize":2,"sort":[{"field":"ProductName","desc":false}]}""");
        var desc = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind, List,
            """{"page":0,"pageSize":2,"sort":[{"field":"ProductName","desc":true}]}""");

        using var a = asc.Json();
        using var d = desc.Json();
        var ascName = ItemName(a.RootElement.GetProperty("page"), 0);
        var descName = ItemName(d.RootElement.GetProperty("page"), 0);
        await Assert.That(ascName).IsEqualTo("Alice Mutton");
        await Assert.That(descName).IsNotEqualTo(ascName);
        await Assert.That(string.Compare(descName, ascName, StringComparison.OrdinalIgnoreCase)).IsGreaterThan(0);
    }

    [Test]
    public async Task Filter_Contains_Narrows_To_Chai()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            List,
            """{"page":0,"pageSize":10,"filter":{"field":"ProductName","op":"Contains","value":"Chai"}}""");

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        var page = json.RootElement.GetProperty("page");
        var total = page.GetProperty("totalRows").GetInt32();
        await Assert.That(total).IsGreaterThan(0);
        await Assert.That(total).IsLessThan(77);
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            await Assert.That(item.GetProperty("productName").GetString()!.Contains("Chai", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
    }

    [Test]
    public async Task Detail_By_ProductId_Returns_Chai()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vProductCategory/detail",
            """{"key":1}""");

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("productName").GetString()).IsEqualTo("Chai");
        await Assert.That(json.RootElement.GetProperty("categoryName").GetString()).IsEqualTo("Beverages");
    }

    [Test]
    public async Task Detail_Missing_Key_Is_404()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vProductCategory/detail",
            """{"key":999999}""");

        await Assert.That(response.Status).IsEqualTo(404);
    }

    [Test]
    public async Task Composite_Detail_Returns_The_Order_Line()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vOrderDetail/detail",
            """{"key":{"OrderId":10248,"ProductId":11}}""");

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("orderId").GetInt32()).IsEqualTo(10248);
        await Assert.That(json.RootElement.GetProperty("productId").GetInt32()).IsEqualTo(11);
        await Assert.That(json.RootElement.GetProperty("productName").GetString()).IsEqualTo("Queso Cabrales");
    }

    [Test]
    public async Task Export_Csv_Downloads_The_Product_Table()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vProductCategory/export",
            """{"page":0,"pageSize":5,"format":"csv","sort":[{"field":"ProductName","desc":false}]}""");

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.ContentType).Contains("text/csv");
        var text = response.Text;
        await Assert.That(text).Contains("Product Name");
        await Assert.That(text).Contains("Alice Mutton");
        // Export rewrites the request to MaxExportRows, so a pageSize of 5 still returns the full set.
        await Assert.That(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsGreaterThan(10);
    }

    [Test]
    public async Task Export_Xlsx_Is_A_Spreadsheet_Package()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vProductCategory/export",
            """{"page":0,"pageSize":5,"format":"xlsx"}""");

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.ContentType).Contains("spreadsheetml.sheet");
        await Assert.That(response.Body.Length).IsGreaterThan(100);
        await Assert.That(response.Body[0]).IsEqualTo((byte)'P');
        await Assert.That(response.Body[1]).IsEqualTo((byte)'K');
    }

    [Test]
    public async Task DataTables_Protocol_Returns_Draw_And_Rows()
    {
        var response = await SampleSession.Http.PostFormAsync(
            SampleSession.Northwind,
            "/api/views/vProductCategory/datatable",
            [
                new("draw", "4"),
                new("start", "0"),
                new("length", "2"),
                new("columns[0][data]", "ProductName"),
                new("order[0][column]", "0"),
                new("order[0][dir]", "asc"),
            ]);

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("draw").GetInt32()).IsEqualTo(4);
        await Assert.That(json.RootElement.GetProperty("recordsTotal").GetInt32()).IsEqualTo(77);
        await Assert.That(json.RootElement.GetProperty("data").GetArrayLength()).IsEqualTo(2);
        await Assert.That(json.RootElement.GetProperty("data")[0].GetProperty("productName").GetString())
            .IsEqualTo("Alice Mutton");
    }

    [Test]
    public async Task ReadOnly_Create_Update_Delete_Are_Not_Mapped()
    {
        foreach (var action in new[] { "create", "update", "delete" })
        {
            var response = await SampleSession.Http.PostJsonAsync(
                SampleSession.Northwind,
                "/api/views/vProductCategory/" + action,
                """{"key":1,"model":{"productName":"nope"}}""");
            await Assert.That(response.Status).IsEqualTo(404);
        }
    }

    [Test]
    public async Task OpenApi_Document_Is_Reachable()
    {
        var response = await SampleSession.Http.GetAsync(SampleSession.Northwind, "/openapi/v1.json");

        await Assert.That(response.Status).IsEqualTo(200);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("openapi").GetString()).IsEqualTo("3.0.4");
        await Assert.That(json.RootElement.GetProperty("paths").EnumerateObject().Count()).IsEqualTo(15);
    }

    [Test]
    public async Task QueryBuilder_Schema_Is_Reachable()
    {
        var response = await SampleSession.Http.GetAsync(
            SampleSession.Northwind, "/api/views/vProductCategory/querybuilder");

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Text).Contains("ProductName");
    }

    [Test]
    public async Task AgGrid_Route_Is_Absent_On_The_DataTables_Host()
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vProductCategory/aggrid",
            """{"startRow":0,"endRow":2,"sortModel":[],"filterModel":{}}""");

        await Assert.That(response.Status).IsEqualTo(404);
    }

    private static async Task<VistaResponse> ListPageAsync(int page)
    {
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            List,
            $$"""{"page":{{page}},"pageSize":2,"sort":[{"field":"ProductName","desc":false}]}""");
        await Assert.That(response.Status).IsEqualTo(200);
        return response;
    }

    private static string ItemName(JsonElement page, int index) =>
        page.GetProperty("items")[index].GetProperty("productName").GetString()!;
}
