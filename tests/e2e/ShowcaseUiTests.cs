using Microsoft.Playwright;
using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>
/// Real Chromium against the showcase (and the Northwind DataTables page).
/// The grids load AG Grid, DataTables, and jQuery from their public CDNs.
/// </summary>
[Timeout(120_000)]
public sealed class ShowcaseUiTests
{
    [Test]
    public async Task AgGrid_Simple_Wiring_Renders_Rows_And_Sort_Flips(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/");
        var page = scope.Page;
        await page.Locator(".ag-row .ag-cell[col-id='ProductName']").First.WaitForAsync();

        var before = (await page.Locator(".ag-row .ag-cell[col-id='ProductName']").First.InnerTextAsync()).Trim();
        await Assert.That(before.Length).IsGreaterThan(0);

        var header = page.Locator(".ag-header-cell[col-id='ProductName'] .ag-header-cell-text");
        var ascendingRequest = page.WaitForResponseAsync(response => IsAgGridSort(response, "asc"));
        await header.ClickAsync();
        await ascendingRequest;
        await Assertions.Expect(page.Locator(".ag-row .ag-cell[col-id='ProductName']").First).ToHaveTextAsync("Alice Mutton");

        var descendingRequest = page.WaitForResponseAsync(response => IsAgGridSort(response, "desc"));
        await header.ClickAsync();
        await descendingRequest;
        var descending = (await page.Locator(".ag-row .ag-cell[col-id='ProductName']").First.InnerTextAsync()).Trim();
        await Assert.That(descending).IsNotEqualTo("Alice Mutton");
        await Assert.That(string.Compare(descending, "Alice Mutton", StringComparison.OrdinalIgnoreCase)).IsGreaterThan(0);
    }

    [Test]
    public async Task AgGrid_Column_Filter_Narrows_To_Chai(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/");
        var page = scope.Page;
        await page.Locator(".ag-floating-filter input").First.WaitForAsync();

        var input = page.Locator(".ag-floating-filter input").First;
        await input.ClickAsync();
        // AG Grid debounces the floating filter, so the wait has to be registered before typing
        // and has to ignore the intermediate "C" / "Ch" / "Cha" posts.
        var filtered = page.WaitForResponseAsync(response =>
            IsAgGridPost(response)
            && (response.Request.PostData ?? string.Empty).Contains("Chai", StringComparison.Ordinal));
        await input.PressSequentiallyAsync("Chai", new() { Delay = 40 });
        await filtered;
        await Assertions.Expect(page.Locator(".ag-row .ag-cell[col-id='ProductName']").First).ToContainTextAsync("Chai");

        var names = await page.Locator(".ag-row .ag-cell[col-id='ProductName']").AllInnerTextsAsync();
        await Assert.That(names.Count).IsGreaterThan(0);
        foreach (var name in names)
        {
            await Assert.That(name.Contains("Chai", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }

        await Assert.That(names.Any(n => n.Contains("Alice", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task AgGrid_Custom_Renderer_Shows_Formatted_Prices(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/custom-renderer.html");
        var page = scope.Page;
        await page.Locator(".ag-row").First.WaitForAsync();
        var prices = await page.Locator(".ag-cell[col-id='UnitPrice']").AllInnerTextsAsync();
        await Assert.That(prices.Count).IsGreaterThan(0);
        await Assert.That(prices.Any(p => p.Contains('$', StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task View_Browser_Paging_Changes_The_First_Row(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/view-browser.html");
        var page = scope.Page;
        // <option> elements are not "visible" to Playwright even after the catalog has filled the select.
        await page.Locator("#viewSelector option[value='vProductCategory']").WaitForAsync(new()
        {
            State = WaitForSelectorState.Attached,
        });
        var loaded = page.WaitForResponseAsync(r => r.Url.Contains("/datatable", StringComparison.Ordinal) && r.Ok);
        await page.Locator("#viewSelector").SelectOptionAsync("vProductCategory");
        await loaded;
        await page.Locator("#grid tbody tr td").First.WaitForAsync();

        var firstPage = (await page.Locator("#grid tbody tr td").First.InnerTextAsync()).Trim();
        await Assert.That(firstPage).IsEqualTo("Alice Mutton");

        var next = page.WaitForResponseAsync(r => r.Url.Contains("/datatable", StringComparison.Ordinal) && r.Ok);
        await page.Locator(".dt-paging-button.next").ClickAsync();
        await next;
        var secondPage = (await page.Locator("#grid tbody tr td").First.InnerTextAsync()).Trim();
        await Assert.That(secondPage).IsNotEqualTo(firstPage);
    }

    [Test]
    public async Task TanStack_Discontinued_Renders_Yes_For_Known_Products(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/tanstack.html");
        var page = scope.Page;
        await page.Locator("#grid-body tr td").First.WaitForAsync();

        // Products.Discontinued is text "0"/"1". These two rows are discontinued; Chai is not.
        await AssertDiscontinuedCellAsync(page, "Alice Mutton", "Yes");
        await AssertDiscontinuedCellAsync(page, "Chef Anton's Gumbo Mix", "Yes");
        await AssertDiscontinuedCellAsync(page, "Chai", "No");
    }

    [Test]
    public async Task Northwind_DataTables_Page_Renders_Rows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Northwind, "/");
        var page = scope.Page;
        await page.Locator("#grid tbody tr td").First.WaitForAsync();
        var name = (await page.Locator("#grid tbody tr td").First.InnerTextAsync()).Trim();
        await Assert.That(name).IsEqualTo("Alice Mutton");
    }

    [Test]
    public async Task Export_Csv_Downloads_From_The_Showcase_Page(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/");
        var page = scope.Page;
        await page.Locator(".ag-row").First.WaitForAsync();

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.EvaluateAsync(
                """
                async () => {
                  const response = await fetch('/api/views/vProductCategory/export', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ page: 0, pageSize: 5, format: 'csv', sort: [{ field: 'ProductName', desc: false }] })
                  });
                  if (!response.ok) {
                    throw new Error('export failed: ' + response.status);
                  }
                  const blob = await response.blob();
                  const url = URL.createObjectURL(blob);
                  const anchor = document.createElement('a');
                  anchor.href = url;
                  anchor.download = 'vProductCategory.csv';
                  document.body.appendChild(anchor);
                  anchor.click();
                }
                """);
        });

        await Assert.That(download.SuggestedFilename).IsEqualTo("vProductCategory.csv");
        var path = await download.PathAsync();
        var text = await File.ReadAllTextAsync(path!);
        await Assert.That(text).Contains("Product Name");
        await Assert.That(text).Contains("Alice Mutton");
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
            AcceptDownloads = true,
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(45_000);
        await page.GotoAsync(new Uri(host, path).AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        return new BrowserPage(context, page);
    }

    private static bool IsAgGridPost(IResponse response) =>
        response.Url.Contains("/aggrid", StringComparison.Ordinal)
        && response.Request.Method == "POST"
        && response.Ok;

    private static bool IsAgGridSort(IResponse response, string direction) =>
        IsAgGridPost(response)
        && (response.Request.PostData ?? string.Empty).Contains($"\"sort\":\"{direction}\"", StringComparison.Ordinal);
}

/// <summary>One browser context. Disposing it closes the page.</summary>
internal sealed class BrowserPage : IAsyncDisposable
{
    private readonly IBrowserContext _context;

    public BrowserPage(IBrowserContext context, IPage page)
    {
        _context = context;
        Page = page;
    }

    public IPage Page { get; }

    public async ValueTask DisposeAsync() => await _context.CloseAsync().ConfigureAwait(false);
}
