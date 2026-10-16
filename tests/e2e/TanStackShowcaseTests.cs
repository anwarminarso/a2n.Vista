using System.Text.Json;
using Microsoft.Playwright;
using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>
/// The TanStack page (<c>/tanstack.html</c>) over <c>vProductCategory</c>. The showcase projection
/// maps the stored text flag with <c>p.Discontinued == "1"</c>, so the column is a bool on the wire
/// and in the grid.
/// </summary>
[Timeout(120_000)]
public sealed class TanStackShowcaseTests
{
    [Test]
    public async Task Alice_Mutton_Shows_Yes_And_An_Active_Product_Shows_No(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/tanstack.html");
        var page = scope.Page;
        await page.Locator("#grid-body tr td").First.WaitForAsync();

        await AssertDiscontinuedCellAsync(page, "Alice Mutton", "Yes");
        await AssertDiscontinuedCellAsync(page, "Chai", "No");
    }

    [Test]
    public async Task Discontinued_Filter_And_Sort_Use_A_Bool(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var listed = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            "/api/views/vProductCategory/tanstack",
            """
            {
              "pagination": { "pageIndex": 0, "pageSize": 10 },
              "sorting": [{ "id": "ProductName", "desc": false }],
              "columnFilters": [],
              "globalFilter": "",
              "columnVisibility": {}
            }
            """);
        await Assert.That(listed.Status).IsEqualTo(200);
        using (var json = listed.Json())
        {
            var rows = json.RootElement.GetProperty("rows");
            await Assert.That(Flag(rows, "Alice Mutton")).IsEqualTo(JsonValueKind.True);
            await Assert.That(Flag(rows, "Chai")).IsEqualTo(JsonValueKind.False);
        }

        var filtered = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            "/api/views/vProductCategory/tanstack",
            """
            {
              "pagination": { "pageIndex": 0, "pageSize": 20 },
              "sorting": [{ "id": "ProductName", "desc": false }],
              "columnFilters": [{ "id": "Discontinued", "value": { "op": "equals", "value": true } }],
              "globalFilter": "",
              "columnVisibility": {}
            }
            """);
        await Assert.That(filtered.Status).IsEqualTo(200);
        using (var json = filtered.Json())
        {
            var rows = json.RootElement.GetProperty("rows");
            await Assert.That(rows.GetArrayLength()).IsGreaterThan(0);
            var names = new List<string>();
            foreach (var row in rows.EnumerateArray())
            {
                await Assert.That(row.GetProperty("discontinued").ValueKind).IsEqualTo(JsonValueKind.True);
                names.Add(row.GetProperty("productName").GetString()!);
            }

            await Assert.That(names).Contains("Alice Mutton");
            await Assert.That(names.Contains("Chai")).IsFalse();
        }

        var sorted = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            "/api/views/vProductCategory/tanstack",
            """
            {
              "pagination": { "pageIndex": 0, "pageSize": 5 },
              "sorting": [{ "id": "Discontinued", "desc": false }],
              "columnFilters": [],
              "globalFilter": "",
              "columnVisibility": {}
            }
            """);
        await Assert.That(sorted.Status).IsEqualTo(200);
        using (var json = sorted.Json())
        {
            var rows = json.RootElement.GetProperty("rows");
            await Assert.That(rows.GetArrayLength()).IsEqualTo(5);
            foreach (var row in rows.EnumerateArray())
            {
                await Assert.That(row.GetProperty("discontinued").ValueKind).IsEqualTo(JsonValueKind.False);
            }
        }
    }

    private static JsonValueKind Flag(JsonElement rows, string productName)
    {
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("productName").GetString() == productName)
            {
                return row.GetProperty("discontinued").ValueKind;
            }
        }

        throw new InvalidOperationException($"'{productName}' was not on the first page.");
    }

    private static async Task AssertDiscontinuedCellAsync(IPage page, string productName, string expected)
    {
        var row = page.Locator("#grid-body tr").Filter(new() { HasText = productName });
        await Assertions.Expect(row).ToHaveCountAsync(1);
        var discontinued = (await row.Locator("td").Last.InnerTextAsync()).Trim();
        await Assert.That(discontinued).IsEqualTo(expected);
    }

    private static async Task<BrowserPage> OpenAsync(Uri host, string path)
    {
        var context = await SampleSession.Browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(45_000);
        await page.GotoAsync(new Uri(host, path).AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        return new BrowserPage(context, page);
    }
}
