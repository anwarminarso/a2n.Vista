// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using a2n.Vista.Authoring;
using a2n.Vista.Examples;
using a2n.Vista.Write;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// A deferred read-then-write on two plain SQLite connections is <c>SQLITE_BUSY</c>. The same
/// contention through an endpoint that opens the file with <see cref="SqliteVistaConnection"/>
/// waits out the lock and then misses the concurrency predicate (412).
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests drive the reflection write path by design; trimming is not used for tests.")]
public sealed class SqliteLockUpgradeTests
{
    [Test]
    public async Task Plain_Deferred_Upgrade_Is_Busy_And_Immediate_Begin_Reaches_412()
    {
        await AssertPlainDeferredUpgradeIsBusyAsync();
        await AssertImmediateBeginReachesThePredicateAsync();
    }

    /// <summary>
    /// Two connections, connection string <c>Data Source</c> only. Both start a deferred transaction
    /// (<c>BeginTransaction(deferred: true)</c>; the async begin on this provider is immediate) and
    /// read the row. The first write holds that transaction. The second write is <c>SQLITE_BUSY</c> (5).
    /// On Microsoft.Data.Sqlite 8.0.11 that wait is the command timeout: <c>sqlite3_step</c> is retried
    /// every 150ms until <c>CommandTimeout</c>, even when <c>PRAGMA busy_timeout</c> reads 0, so the
    /// failure is not sub-millisecond here. The test bounds the command to one second so the failure
    /// is deterministic and does not depend on two HTTP requests happening to overlap.
    /// </summary>
    private static async Task AssertPlainDeferredUpgradeIsBusyAsync()
    {
        var path = NewDatabase();
        try
        {
            var connectionString = "Data Source=" + path;
            await using (var seed = new SqliteConnection(connectionString))
            {
                await seed.OpenAsync();
                await using var create = seed.CreateCommand();
                create.CommandText = "CREATE TABLE T(Id INTEGER PRIMARY KEY, V TEXT); INSERT INTO T VALUES (1, 'a');";
                await create.ExecuteNonQueryAsync();
                await using var pragma = seed.CreateCommand();
                pragma.CommandText = "PRAGMA busy_timeout;";
                var busy = await pragma.ExecuteScalarAsync();
                pragma.CommandText = "PRAGMA journal_mode;";
                var journal = await pragma.ExecuteScalarAsync();
                await Assert.That(Convert.ToInt64(busy)).IsEqualTo(0);
                await Assert.That(journal?.ToString()).IsEqualTo("delete");
            }

            await using var first = new SqliteConnection(connectionString);
            await using var second = new SqliteConnection(connectionString);
            await first.OpenAsync();
            await second.OpenAsync();
            // BeginTransactionAsync() is BEGIN IMMEDIATE on this provider (Unspecified is not deferred).
            // The upgrade bug is a deferred transaction, which has to be asked for explicitly.
            using var firstTx = first.BeginTransaction(deferred: true);
            using var secondTx = second.BeginTransaction(deferred: true);
            await ReadAsync(first, firstTx);
            await ReadAsync(second, secondTx);
            await WriteAsync(first, firstTx, "held", commandTimeout: 1);

            var watch = Stopwatch.StartNew();
            SqliteException? busyError = null;
            try
            {
                await WriteAsync(second, secondTx, "other", commandTimeout: 1);
            }
            catch (SqliteException ex)
            {
                busyError = ex;
            }

            watch.Stop();
            await Assert.That(busyError).IsNotNull();
            await Assert.That(busyError!.SqliteErrorCode).IsEqualTo(5);
            await Assert.That(watch.ElapsedMilliseconds).IsLessThan(2500);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(path);
        }
    }

    /// <summary>
    /// The executor opens the file with <see cref="SqliteVistaConnection"/> (WAL, busy timeout).
    /// A second connection holds <c>BEGIN IMMEDIATE</c> and a new token. The in-flight update stays
    /// running while that lock is held, then the predicate matches nothing: 412
    /// <c>write-concurrency-conflict</c>, not 500 and not 503. A one-statement save does not always
    /// open an EF transaction (<c>AutoTransactionBehavior.WhenNeeded</c>); the wait is the provider
    /// retrying the locked statement until <c>Default Timeout</c>.
    /// </summary>
    private static async Task AssertImmediateBeginReachesThePredicateAsync()
    {
        SqliteConstraintProbe.Register();
        await using var app = await LockApp.StartAsync();
        var created = await app.SendAsync("{\"model\":{\"subject\":\"created\"}}");
        await Assert.That(created.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(created.ETag).IsNotNull();
        await Assert.That(app.JournalMode()).IsEqualTo("wal");

        await using var holder = new SqliteConnection("Data Source=" + app.DatabasePath);
        await holder.OpenAsync();
        await using var held = holder.BeginTransaction(deferred: false);
        await using (var update = holder.CreateCommand())
        {
            update.Transaction = held;
            update.CommandText = "UPDATE \"LockMemos\" SET \"RowVersion\" = $v, \"Subject\" = 'held' WHERE \"Id\" = 1;";
            update.Parameters.AddWithValue("$v", Guid.NewGuid().ToString());
            await update.ExecuteNonQueryAsync();
        }

        var watch = Stopwatch.StartNew();
        var updateTask = app.SendAsync(
            "{\"key\":1,\"model\":{\"subject\":\"from-request\"}}",
            created.ETag);
        await Task.Delay(400);
        await Assert.That(updateTask.IsCompleted).IsFalse();

        await held.CommitAsync();
        var blocked = await updateTask.WaitAsync(TimeSpan.FromSeconds(3));
        watch.Stop();
        await Assert.That(blocked.Status).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await Assert.That(await blocked.CodeAsync()).IsEqualTo(WriteErrorCodes.ConcurrencyConflict);
        await Assert.That(watch.ElapsedMilliseconds).IsLessThan(3500);
        await Assert.That(app.Subject()).IsEqualTo("held");
    }

    private static async Task ReadAsync(SqliteConnection connection, DbTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT V FROM T WHERE Id = 1;";
        await command.ExecuteScalarAsync();
    }

    private static async Task WriteAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string value,
        int commandTimeout)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandTimeout = commandTimeout;
        command.CommandText = "UPDATE T SET V = $v WHERE Id = 1;";
        command.Parameters.AddWithValue("$v", value);
        await command.ExecuteNonQueryAsync();
    }

    private static string NewDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vista-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "lock.db");
    }

    private static void TryDelete(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    internal sealed class LockMemo
    {
        public int Id { get; set; }

        public string Subject { get; set; } = string.Empty;

        public Guid RowVersion { get; set; } = Guid.NewGuid();
    }

    private sealed class LockRow
    {
        public int Id { get; init; }

        public string Subject { get; init; } = string.Empty;

        public Guid RowVersion { get; init; }
    }

    private sealed class LockCrud
    {
        public string Subject { get; init; } = string.Empty;
    }

    private sealed class LockView : View<LockRow, LockCrud>
    {
        protected override void Configure(IViewBuilder<LockRow, LockCrud> builder)
        {
            builder
                .Named("lock-memo")
                .From<LockMemo>(memo => new LockRow
                {
                    Id = memo.Id,
                    Subject = memo.Subject,
                    RowVersion = memo.RowVersion,
                })
                .Field(row => row.Id, field => field.PrimaryKey());

            builder
                .CrudOn<LockMemo>()
                .MapWritable(crud => crud.Subject, entity => entity.Subject)
                .WithConcurrencyToken(entity => entity.RowVersion);
        }
    }

    internal sealed class LockContext : DbContext
    {
        public LockContext(DbContextOptions<LockContext> options)
            : base(options)
        {
        }

        internal DbSet<LockMemo> Memos => Set<LockMemo>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<LockMemo>();
            entity.ToTable("LockMemos");
            entity.HasKey(memo => memo.Id);
            entity.Property(memo => memo.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        }
    }

    private sealed class LockApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly string _directory;

        private LockApp(IHost host, string directory, string databasePath, HttpClient client)
        {
            _host = host;
            _directory = directory;
            DatabasePath = databasePath;
            Client = client;
        }

        public HttpClient Client { get; }

        public string DatabasePath { get; }

        public static async Task<LockApp> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "vista-lock-host-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var databasePath = Path.Combine(directory, "lock.db");
            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<LockContext>(options =>
                        {
                            SqliteVistaConnection.Use(options, databasePath);
                        });
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<LockContext>());
                        services.AddVista(v => v.Register<LockView>());
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
                scope.ServiceProvider.GetRequiredService<LockContext>().Database.EnsureCreated();
            }

            return new LockApp(host, directory, databasePath, host.GetTestClient());
        }

        public string JournalMode()
        {
            using var connection = new SqliteConnection("Data Source=" + DatabasePath);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        public async Task<LockResponse> SendAsync(string json, string? ifMatch = null)
        {
            var path = ifMatch is null ? "/api/views/lock-memo/create" : "/api/views/lock-memo/update";
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }

            var response = await Client.SendAsync(request);
            string? etag = null;
            if (response.Headers.TryGetValues("ETag", out var etags))
            {
                etag = etags.FirstOrDefault();
            }

            var body = await response.Content.ReadAsStringAsync();
            var status = response.StatusCode;
            response.Dispose();
            return new LockResponse(status, etag, body);
        }

        public string Subject()
        {
            using var scope = _host.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<LockContext>().Memos.AsNoTracking().Single().Subject;
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
    }

    private sealed record LockResponse(HttpStatusCode Status, string? ETag, string Body)
    {
        public async Task<string?> CodeAsync()
        {
            using var doc = JsonDocument.Parse(Body);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
    }
}
