using System.Text.Json;
using Microsoft.Playwright;
using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>
/// Real Chromium against the showcase TanStack page (<c>/tanstack.html</c>) and one direct
/// <c>POST /api/views/vProductCategory/tanstack</c> for an unknown sort id.
/// Screenshots are written only when <c>VISTA_E2E_SCREENSHOT_DIR</c> is set.
/// </summary>
[Timeout(120_000)]
public sealed class TanStackShowcaseTests
{
    private const string Route = "/api/views/vProductCategory/tanstack";

    [Test]
    public async Task Paging_Next_Previous_And_Page_Size(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync("/tanstack.html");
        var page = scope.Page;
        await WaitForGridAsync(page);

        var first = await FirstProductAsync(page);
        await Assert.That(first).IsEqualTo("Alice Mutton");
        await Assert.That(await page.Locator("#grid-body tr").CountAsync()).IsEqualTo(10);
        await ExpectStatusAsync(page, "Showing 1–10 of 77 (page 1 of 8)");
        await ShotAsync(page, "01-paging-initial.png");

        var next = WaitForTanStackAsync(page, "\"pageIndex\":1");
        await page.Locator("#page-next").ClickAsync();
        await next;
        await ExpectStatusAsync(page, "Showing 11–20 of 77 (page 2 of 8)");
        var secondPage = await FirstProductAsync(page);
        await Assert.That(secondPage).IsNotEqualTo(first);
        await ShotAsync(page, "02-paging-next.png");

        var prev = WaitForTanStackAsync(page, "\"pageIndex\":0");
        await page.Locator("#page-prev").ClickAsync();
        await prev;
        await ExpectStatusAsync(page, "Showing 1–10 of 77 (page 1 of 8)");
        await Assert.That(await FirstProductAsync(page)).IsEqualTo("Alice Mutton");
        await ShotAsync(page, "03-paging-prev.png");

        var sized = WaitForTanStackAsync(page, "\"pageSize\":20");
        await page.Locator("#page-size").SelectOptionAsync("20");
        await sized;
        await ExpectStatusAsync(page, "Showing 1–20 of 77 (page 1 of 4)");
        await Assert.That(await page.Locator("#grid-body tr").CountAsync()).IsEqualTo(20);
        await ShotAsync(page, "04-paging-page-size-20.png");
    }

    [Test]
    public async Task Sort_ProductName_Flips_Ascending_And_Descending(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync("/tanstack.html");
        var page = scope.Page;
        await WaitForGridAsync(page);

        var productHeader = page.Locator("#grid-head th").First;
        await Assertions.Expect(productHeader).ToHaveAttributeAsync("aria-sort", "ascending");
        await Assert.That(await FirstProductAsync(page)).IsEqualTo("Alice Mutton");
        await ShotAsync(page, "05-sort-ascending.png");

        var descending = WaitForTanStackAsync(page, "\"desc\":true");
        await productHeader.Locator("button.sort-button").ClickAsync();
        await descending;
        await Assertions.Expect(page.Locator("#grid-head th").First).ToHaveAttributeAsync("aria-sort", "descending");
        var name = await FirstProductAsync(page);
        await Assert.That(name).IsNotEqualTo("Alice Mutton");
        await Assert.That(string.Compare(name, "Alice Mutton", StringComparison.OrdinalIgnoreCase)).IsGreaterThan(0);
        Console.WriteLine("TANSTACK_SORT_DESC_FIRST=" + name);
        await ShotAsync(page, "06-sort-descending.png");
    }

    [Test]
    public async Task Global_Filter_Anton_Shows_Only_Matching_Rows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync("/tanstack.html");
        var page = scope.Page;
        await WaitForGridAsync(page);

        var filtered = WaitForTanStackAsync(page, "anton");
        await page.Locator("#global-filter").FillAsync("anton");
        var response = await filtered;
        await Assertions.Expect(page.Locator("#grid-body tr").First).ToContainTextAsync("Anton");
        using var json = JsonDocument.Parse(await response.TextAsync());
        var rows = json.RootElement.GetProperty("rows");
        var names = rows.EnumerateArray().Select(row => row.GetProperty("productName").GetString()!).ToArray();
        Console.WriteLine("TANSTACK_ANTON_ROWCOUNT=" + json.RootElement.GetProperty("rowCount").GetInt32());
        Console.WriteLine("TANSTACK_ANTON_NAMES=" + string.Join(" | ", names));

        await Assert.That(names.Length).IsGreaterThan(0);
        await Assert.That(names.Length).IsLessThan(77);
        foreach (var row in rows.EnumerateArray())
        {
            var hit = row.EnumerateObject().Any(property =>
                property.Value.ValueKind == JsonValueKind.String
                && property.Value.GetString()!.Contains("anton", StringComparison.OrdinalIgnoreCase));
            await Assert.That(hit).IsTrue();
        }

        await Assert.That(names.Contains("Chef Anton's Cajun Seasoning")).IsTrue();
        await Assert.That(names.Contains("Chef Anton's Gumbo Mix")).IsTrue();

        var visible = await page.Locator("#grid-body tr").AllInnerTextsAsync();
        await Assert.That(visible.Count).IsEqualTo(names.Length);
        foreach (var text in visible)
        {
            await Assert.That(text.Contains("anton", StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(text.Contains("Alice Mutton", StringComparison.Ordinal)).IsFalse();
        }

        await ShotAsync(page, "07-filter-anton.png");
    }

    [Test]
    public async Task Hiding_Supplier_Removes_The_Column_And_The_Response_Still_Sends_It(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync("/tanstack.html");
        var page = scope.Page;
        await WaitForGridAsync(page);
        await Assertions.Expect(page.Locator("#grid-head")).ToContainTextAsync("Supplier");

        var hidden = WaitForTanStackAsync(page, "\"SupplierName\":false");
        await page.Locator(".visibility-option").Filter(new() { HasText = "Supplier" }).Locator("input").UncheckAsync();
        var response = await hidden;
        await Assertions.Expect(page.Locator("#grid-head")).Not.ToContainTextAsync("Supplier");
        var requestBody = response.Request.PostData ?? string.Empty;
        await Assert.That(requestBody).Contains("\"SupplierName\":false");

        using var json = JsonDocument.Parse(await response.TextAsync());
        var first = json.RootElement.GetProperty("rows")[0];
        var keys = first.EnumerateObject().Select(property => property.Name).ToArray();
        var supplier = first.GetProperty("supplierName").GetString();
        Console.WriteLine("TANSTACK_HIDDEN_KEYS=" + string.Join(",", keys));
        Console.WriteLine("TANSTACK_HIDDEN_SUPPLIER=" + supplier);
        await Assert.That(keys).Contains("supplierName");
        await Assert.That(string.IsNullOrWhiteSpace(supplier)).IsFalse();

        var headers = await page.Locator("#grid-head button").AllInnerTextsAsync();
        await Assert.That(headers.Any(header => header.Contains("Supplier", StringComparison.Ordinal))).IsFalse();
        await Assert.That(await page.Locator("#grid-body tr").First.Locator("td").CountAsync()).IsEqualTo(5);
        await ShotAsync(page, "08-column-supplier-hidden.png");
    }

    [Test]
    public async Task Unknown_Sort_Column_Is_400(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            Route,
            """{"pagination":{"pageIndex":0,"pageSize":10},"sorting":[{"id":"notAField","desc":false}]}""");

        Console.WriteLine("TANSTACK_UNKNOWN_SORT_STATUS=" + response.Status);
        Console.WriteLine("TANSTACK_UNKNOWN_SORT_BODY=" + response.Text);
        await Assert.That(response.Status).IsEqualTo(400);
        using var json = response.Json();
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo("filter-unknown-field");
        await Assert.That(json.RootElement.GetProperty("field").GetString()).IsEqualTo("notAField");
    }

    private static async Task<BrowserPage> OpenAsync(string path)
    {
        var context = await SampleSession.Browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(45_000);
        await page.GotoAsync(new Uri(SampleSession.Showcase, path).AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        return new BrowserPage(context, page);
    }

    private static async Task WaitForGridAsync(IPage page)
    {
        await page.Locator("#grid-body tr").First.WaitForAsync();
        await ExpectStatusAsync(page, "Showing 1–10 of 77 (page 1 of 8)");
    }

    private static Task ExpectStatusAsync(IPage page, string text) =>
        Assertions.Expect(page.Locator("#status-line")).ToHaveTextAsync(text);

    private static async Task<string> FirstProductAsync(IPage page) =>
        (await page.Locator("#grid-body tr").First.Locator("td").First.InnerTextAsync()).Trim();

    private static Task<IResponse> WaitForTanStackAsync(IPage page, string bodyFragment) =>
        page.WaitForResponseAsync(response =>
            response.Url.Contains("/tanstack", StringComparison.Ordinal)
            && response.Request.Method == "POST"
            && response.Ok
            && (response.Request.PostData ?? string.Empty).Contains(bodyFragment, StringComparison.Ordinal));

    private static async Task ShotAsync(IPage page, string fileName)
    {
        var dir = Environment.GetEnvironmentVariable("VISTA_E2E_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(dir, fileName),
            FullPage = true,
        });
    }
}
