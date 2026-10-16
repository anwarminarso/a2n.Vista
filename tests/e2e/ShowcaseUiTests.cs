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
        // The cell node exists before AG Grid paints the row text. Reading InnerTextAsync there
        // observed "" on PR #12 (run 38882214341, issue #18): string.Compare("", "Alice Mutton") is -12.
        await using var scope = await OpenAsync(SampleSession.Showcase, "/", IsAgGridPost);
        var page = scope.Page;
        var product = page.Locator(".ag-row .ag-cell[col-id='ProductName']").First;
        await Assertions.Expect(product).Not.ToHaveTextAsync(string.Empty);

        var header = page.Locator(".ag-header-cell[col-id='ProductName'] .ag-header-cell-text");
        var ascendingRequest = WaitForDataAsync(page, response => IsAgGridSort(response, "asc"));
        await header.ClickAsync();
        await ascendingRequest;
        await Assertions.Expect(product).ToHaveTextAsync("Alice Mutton");

        var descendingRequest = WaitForDataAsync(page, response => IsAgGridSort(response, "desc"));
        await header.ClickAsync();
        await descendingRequest;
        await Assertions.Expect(product).ToHaveTextAsync("Zaanse koeken");
    }

    [Test]
    public async Task AgGrid_Column_Filter_Narrows_To_Chai(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/", IsAgGridPost);
        var page = scope.Page;
        await page.Locator(".ag-floating-filter input").First.WaitForAsync();

        var input = page.Locator(".ag-floating-filter input").First;
        await input.ClickAsync();
        // AG Grid debounces the floating filter, so the wait has to be registered before typing
        // and has to ignore the intermediate "C" / "Ch" / "Cha" posts.
        var filtered = WaitForDataAsync(page, response =>
            IsAgGridPost(response)
            && (response.Request.PostData ?? string.Empty).Contains("Chai", StringComparison.Ordinal));
        await input.PressSequentiallyAsync("Chai", new() { Delay = 40 });
        await filtered;
        var products = page.Locator(".ag-row .ag-cell[col-id='ProductName']");
        await Assertions.Expect(products.First).ToContainTextAsync("Chai");
        await Assertions.Expect(products.Filter(new() { HasNotText = "Chai" })).ToHaveCountAsync(0);
        await Assertions.Expect(products.Filter(new() { HasText = "Alice" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task AgGrid_Custom_Renderer_Shows_Formatted_Prices(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/custom-renderer.html", IsAgGridPost);
        var page = scope.Page;
        await Assertions.Expect(page.Locator(".ag-cell[col-id='UnitPrice']").First).ToContainTextAsync("$");
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
        var loaded = WaitForDataAsync(page, IsDataTablesPost);
        await page.Locator("#viewSelector").SelectOptionAsync("vProductCategory");
        await loaded;
        var firstCell = page.Locator("#grid tbody tr td").First;
        await Assertions.Expect(firstCell).ToHaveTextAsync("Alice Mutton");

        var next = WaitForDataAsync(page, IsDataTablesPost);
        await page.Locator(".dt-paging-button.next").ClickAsync();
        await next;
        await Assertions.Expect(firstCell).ToHaveTextAsync("Chocolade");
    }

    [Test]
    public async Task Northwind_DataTables_Page_Renders_Rows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Northwind, "/", IsDataTablesPost);
        var page = scope.Page;
        await Assertions.Expect(page.Locator("#grid tbody tr td").First).ToHaveTextAsync("Alice Mutton");
    }

    [Test]
    public async Task Export_Csv_Downloads_From_The_Showcase_Page(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = await OpenAsync(SampleSession.Showcase, "/", IsAgGridPost);
        var page = scope.Page;

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

    private static async Task<BrowserPage> OpenAsync(Uri host, string path, Func<IResponse, bool>? dataResponse = null)
    {
        var context = await SampleSession.Browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            AcceptDownloads = true,
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(45_000);
        // Register before navigation so the first data POST is not missed.
        var pending = dataResponse is null ? null : page.WaitForResponseAsync(dataResponse);
        await page.GotoAsync(new Uri(host, path).AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (pending is not null)
        {
            await EnsureSuccessAsync(await pending);
        }

        return new BrowserPage(context, page);
    }

    /// <summary>
    /// Waits for the POST whose request body matches, then fails immediately when that response
    /// is not 2xx. Matching on <c>response.Ok</c> would ignore the error and wait until timeout.
    /// </summary>
    private static async Task<IResponse> WaitForDataAsync(IPage page, Func<IResponse, bool> match)
    {
        var response = await page.WaitForResponseAsync(match);
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

    private static bool IsAgGridPost(IResponse response) =>
        response.Url.Contains("/aggrid", StringComparison.Ordinal)
        && response.Request.Method == "POST";

    private static bool IsDataTablesPost(IResponse response) =>
        response.Url.Contains("/datatable", StringComparison.Ordinal)
        && response.Request.Method == "POST";

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
