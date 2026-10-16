using System.Text.Json;
using Microsoft.Playwright;
using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>
/// Real Chromium against the showcase TanStack page (<c>/tanstack.html</c>) and direct
/// <c>POST /api/views/vProductCategory/tanstack</c> checks.
/// The showcase projection maps the stored text flag with <c>p.Discontinued == "1"</c>, so that
/// column is a bool on the wire and in the grid.
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
        var product = FirstProduct(page);
        await WaitForGridAsync(page);
        await Assertions.Expect(product).ToHaveTextAsync("Alice Mutton");
        await Assertions.Expect(page.Locator("#grid-body tr")).ToHaveCountAsync(10);
        await ExpectStatusAsync(page, "Showing 1–10 of 77 (page 1 of 8)");
        await ShotAsync(page, "01-paging-initial.png");

        var next = WaitForTanStackAsync(page, "\"pageIndex\":1");
        await page.Locator("#page-next").ClickAsync();
        await next;
        await ExpectStatusAsync(page, "Showing 11–20 of 77 (page 2 of 8)");
        await Assertions.Expect(product).ToHaveTextAsync("Chocolade");
        await ShotAsync(page, "02-paging-next.png");

        var prev = WaitForTanStackAsync(page, "\"pageIndex\":0");
        await page.Locator("#page-prev").ClickAsync();
        await prev;
        await ExpectStatusAsync(page, "Showing 1–10 of 77 (page 1 of 8)");
        await Assertions.Expect(product).ToHaveTextAsync("Alice Mutton");
        await ShotAsync(page, "03-paging-prev.png");

        var sized = WaitForTanStackAsync(page, "\"pageSize\":20");
        await page.Locator("#page-size").SelectOptionAsync("20");
        await sized;
        await ExpectStatusAsync(page, "Showing 1–20 of 77 (page 1 of 4)");
        await Assertions.Expect(page.Locator("#grid-body tr")).ToHaveCountAsync(20);
        await Assertions.Expect(FirstProduct(page)).ToHaveTextAsync("Alice Mutton");
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
        await Assertions.Expect(FirstProduct(page)).ToHaveTextAsync("Alice Mutton");
        await ShotAsync(page, "05-sort-ascending.png");

        var descending = WaitForTanStackAsync(page, "\"desc\":true");
        await productHeader.Locator("button.sort-button").ClickAsync();
        await descending;
        await Assertions.Expect(page.Locator("#grid-head th").First).ToHaveAttributeAsync("aria-sort", "descending");
        await Assertions.Expect(FirstProduct(page)).ToHaveTextAsync("Zaanse koeken");
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

        var visible = page.Locator("#grid-body tr");
        await Assertions.Expect(visible).ToHaveCountAsync(names.Length);
        await Assertions.Expect(visible.Filter(new() { HasNotText = "Anton" })).ToHaveCountAsync(0);
        await Assertions.Expect(visible.Filter(new() { HasText = "Alice Mutton" })).ToHaveCountAsync(0);

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

        await Assertions.Expect(page.Locator("#grid-head button").Filter(new() { HasText = "Supplier" })).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#grid-body tr").First.Locator("td")).ToHaveCountAsync(5);
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

    [Test]
    public async Task Alice_Mutton_Shows_Yes_And_An_Active_Product_Shows_No(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync("/tanstack.html");
        var page = scope.Page;
        await WaitForGridAsync(page);

        await AssertDiscontinuedCellAsync(page, "Alice Mutton", "Yes");
        await AssertDiscontinuedCellAsync(page, "Chai", "No");
    }

    [Test]
    public async Task Discontinued_Filter_And_Sort_Use_A_Bool(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var listed = await SampleSession.Http.PostJsonAsync(
            SampleSession.Showcase,
            Route,
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
            Route,
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
            Route,
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
        // Column index comes from the header element, not a text snapshot of the header row.
        // The cell text is a retrying assertion so a late paint is not observed as empty.
        var header = page.Locator("#grid-head th").Filter(new() { HasText = "Discontinued" });
        await Assertions.Expect(header).ToBeVisibleAsync();
        var index = await header.EvaluateAsync<int>("th => th.cellIndex");
        var cell = page.Locator("#grid-body tr")
            .Filter(new() { HasText = productName })
            .Locator("td")
            .Nth(index);
        await Assertions.Expect(cell).ToHaveTextAsync(expected);
    }

    private static async Task<BrowserPage> OpenAsync(string path)
    {
        var context = await SampleSession.Browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(45_000);
        var pending = page.WaitForResponseAsync(response =>
            response.Url.Contains("/tanstack", StringComparison.Ordinal)
            && response.Request.Method == "POST");
        await page.GotoAsync(new Uri(SampleSession.Showcase, path).AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await EnsureSuccessAsync(await pending);
        return new BrowserPage(context, page);
    }

    private static async Task WaitForGridAsync(IPage page)
    {
        await page.Locator("#grid-body tr").First.WaitForAsync();
        await ExpectStatusAsync(page, "Showing 1–10 of 77 (page 1 of 8)");
    }

    private static Task ExpectStatusAsync(IPage page, string text) =>
        Assertions.Expect(page.Locator("#status-line")).ToHaveTextAsync(text);

    private static ILocator FirstProduct(IPage page) =>
        page.Locator("#grid-body tr").First.Locator("td").First;

    /// <summary>
    /// Waits for the TanStack POST whose body contains <paramref name="bodyFragment"/>.
    /// A non-2xx response fails here instead of being skipped until the wait times out.
    /// </summary>
    private static async Task<IResponse> WaitForTanStackAsync(IPage page, string bodyFragment)
    {
        var response = await page.WaitForResponseAsync(candidate =>
            candidate.Url.Contains("/tanstack", StringComparison.Ordinal)
            && candidate.Request.Method == "POST"
            && (candidate.Request.PostData ?? string.Empty).Contains(bodyFragment, StringComparison.Ordinal));
        await EnsureSuccessAsync(response);
        return response;
    }

    private static async Task EnsureSuccessAsync(IResponse response)
    {
        if (response.Status is >= 200 and < 300)
        {
            return;
        }

        var body = await response.TextAsync();
        if (body.Length > 500)
        {
            body = body[..500];
        }

        throw new InvalidOperationException($"POST {response.Url} returned {response.Status}: {body}");
    }

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
