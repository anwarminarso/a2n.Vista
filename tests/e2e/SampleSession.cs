using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace a2n.Vista.E2E;

/// <summary>Locates the repository and the shipped Northwind database.</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string NorthwindProject =>
        Path.Combine(Root, "src", "Examples", "Northwind");

    public static string ShowcaseProject =>
        Path.Combine(Root, "src", "Examples", "a2n.Vista.Examples.AgGridNorthwind");

    public static string DatabaseFile =>
        Path.Combine(Root, "src", "Examples", "DB", "northwind.db");

    /// <summary>
    /// Extracts <c>Northwind SQLite.zip</c> when <c>northwind.db</c> is absent. The database
    /// file is gitignored; the zip is the committed source of truth.
    /// </summary>
    public static void EnsureDatabase()
    {
        if (File.Exists(DatabaseFile))
        {
            return;
        }

        var zip = Path.Combine(Root, "src", "Examples", "DB", "Northwind SQLite.zip");
        if (!File.Exists(zip))
        {
            throw new FileNotFoundException("Northwind SQLite zip was not found.", zip);
        }

        ZipFile.ExtractToDirectory(zip, Path.GetDirectoryName(DatabaseFile)!);
        if (!File.Exists(DatabaseFile))
        {
            throw new FileNotFoundException(
                "Extracting the Northwind zip did not produce northwind.db.", DatabaseFile);
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "a2n.Vista.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (src/a2n.Vista.slnx) from " + AppContext.BaseDirectory);
    }
}

/// <summary>One real Kestrel process, started with <c>dotnet run</c> from the sample directory.</summary>
internal sealed class SampleHost : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _log = new();
    private readonly object _logGate = new();

    private SampleHost(string name, Uri baseAddress, Process process)
    {
        Name = name;
        BaseAddress = baseAddress;
        _process = process;
    }

    public string Name { get; }

    public Uri BaseAddress { get; }

    public static async Task<SampleHost> StartAsync(string name, string projectDirectory, int port, CancellationToken cancellationToken)
    {
        var url = $"http://127.0.0.1:{port}";
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--framework");
        start.ArgumentList.Add("net8.0");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Debug");
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--no-launch-profile");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = url;
        start.Environment["DOTNET_NOLOGO"] = "1";

        var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Failed to start {name}.");
        var host = new SampleHost(name, new Uri(url), process);
        process.OutputDataReceived += (_, e) => host.Append(e.Data);
        process.ErrorDataReceived += (_, e) => host.Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await host.WaitUntilReadyAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var log = host.RecentLog();
            await host.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{name} did not become ready at {url}.{Environment.NewLine}{log}", ex);
        }

        return host;
    }

    public string RecentLog()
    {
        lock (_logGate)
        {
            var text = _log.ToString();
            return text.Length <= 4000 ? text : text[^4000..];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process.HasExited)
        {
            return;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_logGate)
        {
            _log.AppendLine(line);
        }
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var ready = new Uri(BaseAddress, "/");
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"{Name} exited with code {_process.ExitCode}.");
            }

            try
            {
                using var response = await http.GetAsync(ready, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 500)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Still binding.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Per-request timeout.
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>JSON and form calls against a running sample.</summary>
internal sealed class VistaClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<VistaResponse> PostJsonAsync(Uri host, string path, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host, path))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await SendAsync(request).ConfigureAwait(false);
    }

    public async Task<VistaResponse> PostFormAsync(Uri host, string path, IEnumerable<KeyValuePair<string, string>> form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host, path))
        {
            Content = new FormUrlEncodedContent(form),
        };
        return await SendAsync(request).ConfigureAwait(false);
    }

    public async Task<VistaResponse> GetAsync(Uri host, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(host, path));
        return await SendAsync(request).ConfigureAwait(false);
    }

    private async Task<VistaResponse> SendAsync(HttpRequestMessage request)
    {
        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var type = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
        string? etag = null;
        if (response.Headers.ETag is EntityTagHeaderValue tag)
        {
            etag = tag.Tag;
        }
        else if (response.Headers.TryGetValues("ETag", out var values))
        {
            etag = string.Join(",", values);
        }

        return new VistaResponse((int)response.StatusCode, type, bytes, etag);
    }
}

internal sealed record VistaResponse(int Status, string ContentType, byte[] Body, string? ETag)
{
    public string Text => Encoding.UTF8.GetString(Body);

    public System.Text.Json.JsonDocument Json() => System.Text.Json.JsonDocument.Parse(Body);
}

/// <summary>Starts both sample hosts and one Chromium for the whole test session.</summary>
internal static class SampleSession
{
    private static SampleHost? _northwind;
    private static SampleHost? _showcase;

    public static Uri Northwind => _northwind?.BaseAddress
        ?? throw new InvalidOperationException("Northwind host is not running.");

    public static Uri Showcase => _showcase?.BaseAddress
        ?? throw new InvalidOperationException("Showcase host is not running.");

    public static VistaClient Http { get; } = new();

    public static Microsoft.Playwright.IBrowser Browser { get; private set; } = null!;

    public static async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RepoPaths.EnsureDatabase();

        var exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exit != 0)
        {
            throw new InvalidOperationException($"playwright install chromium exited {exit}.");
        }

        _northwind = await SampleHost.StartAsync("Northwind", RepoPaths.NorthwindProject, 18771, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            _showcase = await SampleHost.StartAsync("AgGridNorthwind", RepoPaths.ShowcaseProject, 18772, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (_northwind is not null)
            {
                await _northwind.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }

        var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        Browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-dev-shm-usage"],
        }).ConfigureAwait(false);
    }

    public static async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Browser is not null)
        {
            await Browser.CloseAsync().ConfigureAwait(false);
        }

        if (_showcase is not null)
        {
            await _showcase.DisposeAsync().ConfigureAwait(false);
        }

        if (_northwind is not null)
        {
            await _northwind.DisposeAsync().ConfigureAwait(false);
        }
    }
}
