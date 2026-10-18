using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>
/// HTTP smoke: start a cold Northwind host and immediately send 32 concurrent list/read calls.
/// </summary>
/// <remarks>
/// <para>
/// This is a smoke only. It is not a regression proof of the EF Core 9/10 lazy relational-model
/// race (<see href="https://github.com/dotnet/efcore/issues/39214">dotnet/efcore#39214</see>,
/// Vista issue #13). That race is rare: a green wave does not show it is gone, and a red wave
/// on one run does not measure how often it happens. EF 8 finalizes the relational model during
/// context initialization, so this smoke does not target a net8 host.
/// </para>
/// <para>
/// The host is not warmed with an HTTP call. Readiness is the Kestrel log line "Now listening on".
/// <c>SampleSession</c> is not used, because its ready check is <c>GET /</c>. Vista hosted services
/// still run as part of host start and may touch the <c>DbContext</c> before or during this wave.
/// Northwind's views declare keys, so startup key derivation does not need the relational model.
/// </para>
/// <para>
/// Deterministic evidence for the test-harness warmup lives in unit tests, not here. On
/// <see href="https://github.com/anwarminarso/a2n.Vista/pull/12">PR #12</see> (not merged into
/// the tree this smoke runs against): <c>RelationalModelWarmup.WarmAll</c> finalizes each test
/// <c>DbContext</c> on one thread, and
/// <c>CompositeKeyOrderIndependencePropertyTests.Concurrent_EnsureCreated_After_Warmup_Completes</c>
/// bursts 32 <c>EnsureCreated</c> calls after that warmup. That pull request's own remarks say the
/// burst checks the post-warmup path only. A library host warmup (issue #13, option 1) is not in
/// this tree. There is no retry: a 500 must stay visible.
/// </para>
/// </remarks>
[NotInParallel]
public sealed class StartupBurstSmokeTests
{
    private const int WaveSize = 32;

    [Test]
    [Timeout(180_000)]
    public async Task Net9_Host_First_Request_Wave_Returns_No_500(CancellationToken cancellationToken)
        => await RunAsync("net9.0", 18791, cancellationToken);

    [Test]
    [Timeout(180_000)]
    public async Task Net10_Host_First_Request_Wave_Returns_No_500(CancellationToken cancellationToken)
        => await RunAsync("net10.0", 18792, cancellationToken);

    private static async Task RunAsync(string tfm, int port, CancellationToken cancellationToken)
    {
        var outputDir = Path.Combine(FindRepoRoot(), "src", "Examples", "Northwind", "bin", "Debug", tfm);
        var dll = Path.Combine(outputDir, "a2n.Vista.Examples.Northwind.dll");
        if (!File.Exists(dll))
        {
            throw new FileNotFoundException(
                $"Northwind {tfm} host is not built. Run bash tests/e2e/run-startup-burst.sh.",
                dll);
        }

        var runtimeTfm = ReadRuntimeTfm(outputDir);
        var efVersion = ReadPackageVersion(outputDir, "Microsoft.EntityFrameworkCore");
        await Assert.That(runtimeTfm).IsEqualTo(tfm);

        string workingDirectory;
        {
        await using var host = await ColdHost.StartAsync(tfm, dll, port, cancellationToken);
        workingDirectory = host.WorkingDirectory;
        var copyDirectories = Directory.GetDirectories(host.WorkingDirectory);
        await Assert.That(copyDirectories.Length).IsEqualTo(1);
        await Assert.That(Guid.TryParseExact(Path.GetFileName(copyDirectories[0]), "N", out _)).IsTrue();
        var copyDatabase = Path.Combine(copyDirectories[0], "northwind.db");
        await Assert.That(File.Exists(copyDatabase)).IsTrue();
        var template = Path.GetFullPath(Path.Combine(FindRepoRoot(), "src", "Examples", "DB", "northwind.db"));
        await Assert.That(Path.GetFullPath(copyDatabase)).IsNotEqualTo(template);

        var results = Burst(host.BaseAddress);

        var histogram = string.Join(
            ", ",
            results
                .GroupBy(static r => r.Status)
                .OrderBy(static g => g.Key)
                .Select(static g => $"{g.Key} x{g.Count()}"));
        var failures = results.Where(static r => r.Status is 0 || r.Status >= 500).ToArray();
        var failureText = failures.Length == 0
            ? string.Empty
            : string.Join("\n---\n", failures.Select(static f => $"{f.Label} -> {f.Status}\n{Trim(f.Body)}"));

        Console.WriteLine(
            $"startup-burst tfm={tfm} runtimeTfm={runtimeTfm} ef={efVersion} " +
            $"applicationStartedBeforeBurst={host.ApplicationStartedBeforeBurst} " +
            $"responses={results.Length} statuses=[{histogram}]");
        if (failureText.Length > 0)
        {
            Console.WriteLine(failureText);
        }

        await Assert.That(results.Length).IsEqualTo(WaveSize);
        await Assert.That(failureText).IsEqualTo(string.Empty);
        }

        await Assert.That(Directory.Exists(workingDirectory)).IsFalse();
    }

    private static BurstResponse[] Burst(Uri baseAddress)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var calls = Wave().ToArray();
        var results = new BurstResponse[calls.Length];
        var threads = new Thread[calls.Length];
        var gate = new Barrier(calls.Length);
        for (var i = 0; i < calls.Length; i++)
        {
            var index = i;
            threads[i] = new Thread(() =>
            {
                gate.SignalAndWait();
                try
                {
                    results[index] = Send(http, baseAddress, calls[index]);
                }
                catch (Exception ex)
                {
                    results[index] = new BurstResponse(calls[index].Label, 0, ex.ToString());
                }
            })
            {
                IsBackground = true,
                Name = $"startup-burst-{index}",
            };
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        return results;
    }

    private static BurstResponse Send(HttpClient http, Uri baseAddress, BurstCall call)
    {
        using var request = new HttpRequestMessage(call.Method, new Uri(baseAddress, call.Path));
        if (call.Json is not null)
        {
            request.Content = new StringContent(call.Json, Encoding.UTF8, "application/json");
        }
        else if (call.Form is not null)
        {
            request.Content = new FormUrlEncodedContent(call.Form);
        }

        using var response = http.Send(request, HttpCompletionOption.ResponseContentRead);
        var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        return new BurstResponse(call.Label, (int)response.StatusCode, Encoding.UTF8.GetString(bytes));
    }

    private static IEnumerable<BurstCall> Wave()
    {
        BurstCall[] pattern =
        [
            new(HttpMethod.Post, "/api/views/vProductCategory/list", """{"page":0,"pageSize":2}""", null),
            new(HttpMethod.Post, "/api/views/vProductCategory/detail", """{"key":1}""", null),
            new(
                HttpMethod.Post,
                "/api/views/vOrderDetail/detail",
                """{"key":{"OrderId":10248,"ProductId":11}}""",
                null),
            new(HttpMethod.Get, "/api/views/vProductCategory/metadata", null, null),
            new(HttpMethod.Get, "/api/views/vProductCategory/querybuilder", null, null),
            new(HttpMethod.Get, "/openapi/v1.json", null, null),
            new(
                HttpMethod.Post,
                "/api/views/vProductCategory/datatable",
                null,
                [
                    new KeyValuePair<string, string>("draw", "1"),
                    new KeyValuePair<string, string>("start", "0"),
                    new KeyValuePair<string, string>("length", "2"),
                    new KeyValuePair<string, string>("columns[0][data]", "ProductName"),
                    new KeyValuePair<string, string>("order[0][column]", "0"),
                    new KeyValuePair<string, string>("order[0][dir]", "asc"),
                ]),
            new(HttpMethod.Post, "/api/views/vOrderDetail/list", """{"page":0,"pageSize":2}""", null),
        ];

        for (var copy = 0; copy < WaveSize / pattern.Length; copy++)
        {
            foreach (var call in pattern)
            {
                yield return call;
            }
        }
    }

    private static string ReadRuntimeTfm(string outputDir)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "a2n.Vista.Examples.Northwind.runtimeconfig.json")));
        return json.RootElement.GetProperty("runtimeOptions").GetProperty("tfm").GetString()
            ?? throw new InvalidOperationException("runtimeconfig.json has no tfm.");
    }

    private static string ReadPackageVersion(string outputDir, string packageId)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "a2n.Vista.Examples.Northwind.deps.json")));
        foreach (var library in json.RootElement.GetProperty("libraries").EnumerateObject())
        {
            var slash = library.Name.IndexOf('/');
            if (slash > 0 && library.Name[..slash] == packageId)
            {
                return library.Name[(slash + 1)..];
            }
        }

        return "unknown";
    }

    private static string FindRepoRoot()
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

    private static string Trim(string text)
    {
        const int limit = 2000;
        var flat = text.Replace("\r", "", StringComparison.Ordinal);
        return flat.Length <= limit ? flat : flat[..limit];
    }

    private sealed record BurstCall(
        HttpMethod Method,
        string Path,
        string? Json,
        IReadOnlyList<KeyValuePair<string, string>>? Form)
    {
        public string Label => $"{Method.Method} {Path}";
    }

    private readonly record struct BurstResponse(string Label, int Status, string Body);

    private sealed class ColdHost : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly string _workingDirectory;

        private ColdHost(Uri baseAddress, Process process, bool applicationStartedBeforeBurst, string workingDirectory)
        {
            BaseAddress = baseAddress;
            _process = process;
            ApplicationStartedBeforeBurst = applicationStartedBeforeBurst;
            _workingDirectory = workingDirectory;
        }

        public Uri BaseAddress { get; }

        public bool ApplicationStartedBeforeBurst { get; }

        public string WorkingDirectory => _workingDirectory;

        public static async Task<ColdHost> StartAsync(
            string tfm,
            string dll,
            int port,
            CancellationToken cancellationToken)
        {
            var url = $"http://127.0.0.1:{port}";
            var projectDir = Path.GetFullPath(Path.Combine(FindRepoRoot(), "src", "Examples", "Northwind"));
            // The host copies the pinned template into a GUID directory under this parent and opens
            // only that copy. Kill does not run the host's shutdown hook, so Dispose deletes the parent.
            var workingDirectory = Path.Combine(
                Path.GetTempPath(),
                "a2n-vista-startup-burst",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(dll);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_URLS"] = url;
            start.Environment["DOTNET_NOLOGO"] = "1";
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["NORTHWIND_WORKING_DIRECTORY"] = workingDirectory;

            var process = Process.Start(start)
                ?? throw new InvalidOperationException($"Failed to start the {tfm} Northwind host.");
            process.EnableRaisingEvents = true;
            var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLine(string? line)
            {
                if (line is null)
                {
                    return;
                }

                LineBuffer.Append(process, line);
                if (line.Contains("Now listening on", StringComparison.Ordinal))
                {
                    listening.TrySetResult();
                }
            }

            process.OutputDataReceived += (_, e) => OnLine(e.Data);
            process.ErrorDataReceived += (_, e) => OnLine(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.Exited += (_, _) =>
            {
                if (!listening.Task.IsCompleted)
                {
                    listening.TrySetException(new InvalidOperationException(
                        $"Northwind {tfm} exited with code {SafeExitCode(process)} before listening.\n{LineBuffer.Text(process)}"));
                }
            };

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await using var _ = linked.Token.Register(() =>
                listening.TrySetCanceled(linked.Token));
            try
            {
                await listening.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TryKill(process);
                DeleteWorkingDirectory(workingDirectory);
                throw new InvalidOperationException(
                    $"Northwind {tfm} did not log 'Now listening on' at {url}.\n{LineBuffer.Text(process)}",
                    ex);
            }

            var started = LineBuffer.Text(process).Contains("Application started", StringComparison.Ordinal);
            return new ColdHost(new Uri(url), process, started, workingDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            TryKill(_process);
            if (!_process.HasExited)
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }

            DeleteWorkingDirectory(_workingDirectory);

            LineBuffer.Remove(_process);
            _process.Dispose();
        }

        private static string SafeExitCode(Process process)
        {
            try
            {
                return process.ExitCode.ToString();
            }
            catch (InvalidOperationException)
            {
                return "unknown";
            }
        }

        private static void DeleteWorkingDirectory(string workingDirectory)
        {
            for (var attempt = 0; attempt < 10 && Directory.Exists(workingDirectory); attempt++)
            {
                try
                {
                    DeleteWalSidecars(workingDirectory);
                    Directory.Delete(workingDirectory, recursive: true);
                }
                catch (IOException) when (attempt < 9)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 9)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static void DeleteWalSidecars(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                TryDelete(file + "-wal");
                TryDelete(file + "-shm");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        }
    }

    /// <summary>Log lines for a process that has not been wrapped in <see cref="ColdHost"/> yet.</summary>
    private static class LineBuffer
    {
        private static readonly Dictionary<int, StringBuilder> Buffers = new();

        public static void Append(Process process, string line)
        {
            lock (Buffers)
            {
                if (!Buffers.TryGetValue(process.Id, out var builder))
                {
                    builder = new StringBuilder();
                    Buffers[process.Id] = builder;
                }

                builder.AppendLine(line);
            }
        }

        public static string Text(Process process)
        {
            lock (Buffers)
            {
                return Buffers.TryGetValue(process.Id, out var builder) ? builder.ToString() : string.Empty;
            }
        }

        public static void Remove(Process process)
        {
            lock (Buffers)
            {
                Buffers.Remove(process.Id);
            }
        }
    }
}
