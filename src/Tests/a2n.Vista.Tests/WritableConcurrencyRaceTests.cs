// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using a2n.Vista.Authoring;
using a2n.Vista.Examples;
using a2n.Vista.Ports;
using a2n.Vista.Write;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// The writable-memo lost-update regression: two requests that present the same token must not both
/// commit, the token changes on success, and If-Match accepts a quoted tag, a legacy unquoted token,
/// and rejects <c>*</c>.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests drive the reflection write path by design; trimming is not used for tests.")]
public sealed class WritableConcurrencyRaceTests
{
    private const string Route = "/api/views/race-memo";

    [Test]
    public async Task Parallel_Updates_With_The_Same_Token_Yield_One_200_And_One_412()
    {
        await using var app = await RaceApp.StartAsync();
        var created = await app.CreateAsync("before");
        await Assert.That(created.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(created.ETag).IsNotNull();
        var token = created.ETag!;
        await Assert.That(token.StartsWith("\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(token.EndsWith("\"", StringComparison.Ordinal)).IsTrue();

        var first = app.UpdateAsync("one", token);
        var second = app.UpdateAsync("two", token);
        var results = await Task.WhenAll(first, second);

        var successes = results.Where(result => result.Status == HttpStatusCode.OK).ToArray();
        var conflicts = results.Where(result => result.Status == HttpStatusCode.PreconditionFailed).ToArray();
        await Assert.That(successes.Length).IsEqualTo(1);
        await Assert.That(conflicts.Length).IsEqualTo(1);
        await Assert.That(await conflicts[0].CodeAsync()).IsEqualTo(WriteErrorCodes.ConcurrencyConflict);

        var winner = successes[0];
        await Assert.That(winner.ETag).IsNotEqualTo(token);
        await Assert.That(winner.ETag).IsNotNull();
        var stored = app.Read();
        await Assert.That(stored.RowVersion.ToString()).IsEqualTo(winner.ETag!.Trim('"'));
        await Assert.That(stored.RowVersion.ToString()).IsNotEqualTo(token.Trim('"'));

        var replay = await app.UpdateAsync("replay", token);
        await Assert.That(replay.Status).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await Assert.That(app.Read().Subject).IsEqualTo(stored.Subject);
        await Assert.That(app.Read().RowVersion).IsEqualTo(stored.RowVersion);

        var updateSql = app.Commands.FirstOrDefault(sql =>
            sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase));
        await Assert.That(updateSql).IsNotNull();
        await Assert.That(updateSql!).Contains("RowVersion");
    }

    [Test]
    public async Task File_Sqlite_Fifty_Rounds_Of_Two_Parallel_Updates_Are_One_200_And_One_412()
    {
        await using var app = await RaceApp.StartAsync();
        var created = await app.CreateAsync("seed");
        await Assert.That(created.Status).IsEqualTo(HttpStatusCode.OK);
        var token = created.ETag!;
        for (var round = 0; round < 50; round++)
        {
            token = await OneRoundAsync(app, token, writers: 2, round);
        }
    }

    [Test]
    public async Task File_Sqlite_Twenty_Rounds_Of_Three_Parallel_Updates_Are_One_200_And_The_Rest_412()
    {
        await using var app = await RaceApp.StartAsync();
        var created = await app.CreateAsync("seed");
        await Assert.That(created.Status).IsEqualTo(HttpStatusCode.OK);
        var token = created.ETag!;
        for (var round = 0; round < 20; round++)
        {
            token = await OneRoundAsync(app, token, writers: 3, round);
        }
    }

    private static async Task<string> OneRoundAsync(RaceApp app, string token, int writers, int round)
    {
        var subjects = new string[writers];
        var tasks = new Task<RaceResponse>[writers];
        for (var writer = 0; writer < writers; writer++)
        {
            subjects[writer] = $"r{round}-w{writer}";
            tasks[writer] = app.UpdateAsync(subjects[writer], token);
        }

        var results = await Task.WhenAll(tasks);
        var successes = results.Where(result => result.Status == HttpStatusCode.OK).ToArray();
        var conflicts = results.Where(result => result.Status == HttpStatusCode.PreconditionFailed).ToArray();
        await Assert.That(successes.Length).IsEqualTo(1);
        await Assert.That(conflicts.Length).IsEqualTo(writers - 1);
        foreach (var result in results)
        {
            await Assert.That(result.Status).IsNotEqualTo(HttpStatusCode.InternalServerError);
            await Assert.That(result.Status).IsNotEqualTo(HttpStatusCode.ServiceUnavailable);
            if (result.Status == HttpStatusCode.PreconditionFailed)
            {
                await Assert.That(await result.CodeAsync()).IsEqualTo(WriteErrorCodes.ConcurrencyConflict);
            }
        }

        var stored = app.Read();
        await Assert.That(subjects).Contains(stored.Subject);
        await Assert.That(stored.RowVersion.ToString()).IsEqualTo(successes[0].ETag!.Trim('"'));
        return successes[0].ETag!;
    }

    [Test]
    public async Task Quoted_Unquoted_Star_And_List_If_Match()
    {
        await using var app = await RaceApp.StartAsync();
        var created = await app.CreateAsync("origin");
        var quoted = created.ETag!;
        var bare = quoted.Trim('"');

        var unquoted = await app.UpdateAsync("unquoted", bare);
        await Assert.That(unquoted.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(unquoted.ETag).IsNotEqualTo(quoted);
        var afterUnquoted = unquoted.ETag!;

        var star = await app.UpdateAsync("star", "*");
        await Assert.That(star.Status).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await Assert.That(app.Read().Subject).IsEqualTo("unquoted");

        var weak = await app.UpdateAsync("weak", "W/" + afterUnquoted);
        await Assert.That(weak.Status).IsEqualTo(HttpStatusCode.PreconditionFailed);

        var list = await app.UpdateAsync("listed", afterUnquoted + ", \"00000000-0000-0000-0000-000000000000\"");
        await Assert.That(list.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(list.ETag).IsNotEqualTo(afterUnquoted);

        var miss = await app.UpdateAsync(
            "miss",
            "\"00000000-0000-0000-0000-000000000000\", \"11111111-1111-1111-1111-111111111111\"");
        await Assert.That(miss.Status).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await Assert.That(app.Read().Subject).IsEqualTo("listed");

        var deleted = await app.DeleteAsync(list.ETag!);
        await Assert.That(deleted.Status).IsEqualTo(HttpStatusCode.OK);
        var deleteSql = app.Commands.LastOrDefault(sql =>
            sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase));
        await Assert.That(deleteSql).IsNotNull();
        await Assert.That(deleteSql!).Contains("RowVersion");
    }

    internal sealed class RaceMemo
    {
        public int Id { get; set; }

        public string Subject { get; set; } = string.Empty;

        public Guid RowVersion { get; set; } = Guid.NewGuid();
    }

    private sealed class RaceRow
    {
        public int Id { get; init; }

        public string Subject { get; init; } = string.Empty;

        public Guid RowVersion { get; init; }
    }

    private sealed class RaceCrud
    {
        public string Subject { get; init; } = string.Empty;
    }

    private sealed class RaceView : View<RaceRow, RaceCrud>
    {
        protected override void Configure(IViewBuilder<RaceRow, RaceCrud> builder)
        {
            builder
                .Named("race-memo")
                .From<RaceMemo>(m => new RaceRow
                {
                    Id = m.Id,
                    Subject = m.Subject,
                    RowVersion = m.RowVersion,
                })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<RaceMemo>()
                .MapWritable(c => c.Subject, e => e.Subject)
                .WithConcurrencyToken(e => e.RowVersion);
        }
    }

    internal sealed class RaceContext : DbContext
    {
        public RaceContext(DbContextOptions<RaceContext> options)
            : base(options)
        {
        }

        public DbSet<RaceMemo> Memos => Set<RaceMemo>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<RaceMemo>();
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        }
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Commands.Enqueue(command.CommandText);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class RaceApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly string _directory;
        private readonly SqlCapture _capture;

        private RaceApp(IHost host, string directory, SqlCapture capture, HttpClient client)
        {
            _host = host;
            _directory = directory;
            _capture = capture;
            Client = client;
        }

        public HttpClient Client { get; }

        public IReadOnlyCollection<string> Commands => _capture.Commands;

        public static async Task<RaceApp> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "vista-race-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var databasePath = Path.Combine(directory, "race.db");

            var capture = new SqlCapture();
            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSingleton(capture);
                        services.AddDbContext<RaceContext>((sp, options) =>
                            SqliteVistaConnection.Use(options, databasePath)
                                .AddInterceptors(sp.GetRequiredService<SqlCapture>()));
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<RaceContext>());
                        services.AddVista(v => v.Register<RaceView>());
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                    })
                    .Configure(app =>
                    {
                        app.UseVistaExceptionHandling();
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapVistaViews());
                    }))
                .StartAsync();

            using (var scope = host.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<RaceContext>().Database.EnsureCreated();
            }

            return new RaceApp(host, directory, capture, host.GetTestClient());
        }

        public async Task<RaceResponse> CreateAsync(string subject)
        {
            var response = await Client.PostAsync(
                $"{Route}/create",
                Json($"{{\"model\":{{\"subject\":\"{subject}\"}}}}"));
            return await RaceResponse.ReadAsync(response);
        }

        public async Task<RaceResponse> UpdateAsync(string subject, string ifMatch)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Route}/update")
            {
                Content = Json($"{{\"key\":1,\"model\":{{\"subject\":\"{subject}\"}}}}"),
            };
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            var response = await Client.SendAsync(request);
            return await RaceResponse.ReadAsync(response);
        }

        public async Task<RaceResponse> DeleteAsync(string ifMatch)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Route}/delete")
            {
                Content = Json("{\"key\":1}"),
            };
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            var response = await Client.SendAsync(request);
            return await RaceResponse.ReadAsync(response);
        }

        public (string Subject, Guid RowVersion) Read()
        {
            using var scope = _host.Services.CreateScope();
            var row = scope.ServiceProvider.GetRequiredService<RaceContext>().Memos.AsNoTracking().Single();
            return (row.Subject, row.RowVersion);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    }

    private sealed class RaceResponse
    {
        public required HttpStatusCode Status { get; init; }

        public string? ETag { get; init; }

        public string Body { get; init; } = string.Empty;

        public async Task<string?> CodeAsync()
        {
            using var doc = JsonDocument.Parse(Body);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }

        public static async Task<RaceResponse> ReadAsync(HttpResponseMessage response)
        {
            string? etag = null;
            if (response.Headers.TryGetValues("ETag", out var values))
            {
                etag = values.FirstOrDefault();
            }

            var body = await response.Content.ReadAsStringAsync();
            var status = response.StatusCode;
            response.Dispose();
            return new RaceResponse { Status = status, ETag = etag, Body = body };
        }
    }
}
