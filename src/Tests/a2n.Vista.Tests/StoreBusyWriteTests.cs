// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Examples;
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
/// A SQLite lock that outlasts the busy timeout is HTTP 503 <c>write-busy</c> with <c>Retry-After</c>,
/// not 412 and not 500 (Decision Log D160).
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests drive the reflection write path by design; trimming is not used for tests.")]
public sealed class StoreBusyWriteTests
{
    [Test]
    public async Task Sqlite_Busy_And_Locked_Are_Transient_Locks_And_Not_Constraints()
    {
        SqliteConstraintProbe.Register();

        await Assert.That(DbUpdateFailureClassification.IsTransientLock(new SqliteException("database is locked", 5)))
            .IsTrue();
        await Assert.That(DbUpdateFailureClassification.IsTransientLock(new SqliteException("database table is locked", 6)))
            .IsTrue();
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(new SqliteException("database is locked", 5)))
            .IsFalse();
        await Assert.That(DbUpdateFailureClassification.IsConstraintViolation(new SqliteException("database table is locked", 6)))
            .IsFalse();
        await Assert.That(DbUpdateFailureClassification.IsTransientLock(new SqliteException("SQLITE_CONSTRAINT", 19)))
            .IsFalse();
        await Assert.That(DbUpdateFailureClassification.IsTransientLock(new TransientDbException()))
            .IsTrue();
    }

    [Test]
    public async Task An_Exclusive_Lock_With_A_Tiny_Timeout_Is_503_Write_Busy()
    {
        SqliteConstraintProbe.Register();
        await using var app = await BusyApp.StartAsync();
        var created = await app.SendAsync("{\"model\":{\"subject\":\"created\"}}");
        await Assert.That(created.Status).IsEqualTo(HttpStatusCode.OK);

        app.ArmLock();
        var blocked = await app.SendAsync("{\"key\":1,\"model\":{\"subject\":\"blocked\"}}", created.ETag);
        await Assert.That(blocked.Status).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(blocked.RetryAfter).IsEqualTo(VistaStoreBusyException.RetryAfterSeconds.ToString());
        await Assert.That(await blocked.CodeAsync()).IsEqualTo(WriteErrorCodes.StoreBusy);
        await Assert.That(blocked.Body).Contains("Service unavailable");
        await Assert.That(blocked.Body).DoesNotContain("database is locked");
        await Assert.That(app.Subject()).IsEqualTo("created");
    }

    private sealed class TransientDbException : DbException
    {
        public override bool IsTransient => true;
    }

    internal sealed class BusyMemo
    {
        public int Id { get; set; }

        public string Subject { get; set; } = string.Empty;

        public Guid RowVersion { get; set; } = Guid.NewGuid();
    }

    private sealed class BusyRow
    {
        public int Id { get; init; }

        public string Subject { get; init; } = string.Empty;

        public Guid RowVersion { get; init; }
    }

    private sealed class BusyCrud
    {
        public string Subject { get; init; } = string.Empty;
    }

    private sealed class BusyView : View<BusyRow, BusyCrud>
    {
        protected override void Configure(IViewBuilder<BusyRow, BusyCrud> builder)
        {
            builder
                .Named("busy-memo")
                .From<BusyMemo>(memo => new BusyRow
                {
                    Id = memo.Id,
                    Subject = memo.Subject,
                    RowVersion = memo.RowVersion,
                })
                .Field(row => row.Id, field => field.PrimaryKey());

            builder
                .CrudOn<BusyMemo>()
                .MapWritable(crud => crud.Subject, entity => entity.Subject)
                .WithConcurrencyToken(entity => entity.RowVersion);
        }
    }

    internal sealed class BusyContext : DbContext
    {
        public BusyContext(DbContextOptions<BusyContext> options)
            : base(options)
        {
        }

        public DbSet<BusyMemo> Memos => Set<BusyMemo>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BusyMemo>().ToTable("BusyMemos");
            modelBuilder.Entity<BusyMemo>().HasKey(entity => entity.Id);
            modelBuilder.Entity<BusyMemo>().Property(entity => entity.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        }
    }

    private sealed class ExclusiveLock : SaveChangesInterceptor
    {
        private readonly string _path;
        private SqliteConnection? _holder;
        private SqliteTransaction? _transaction;

        public ExclusiveLock(string path)
        {
            _path = path;
        }

        public bool Armed { get; set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Hold();
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Hold();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public void Release()
        {
            _transaction?.Dispose();
            _holder?.Dispose();
            _transaction = null;
            _holder = null;
        }

        private void Hold()
        {
            if (!Armed || _holder is not null)
            {
                return;
            }

            _holder = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                DefaultTimeout = 1,
            }.ToString());
            _holder.Open();
            _transaction = _holder.BeginTransaction(deferred: false);
            using var command = _holder.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = "UPDATE \"BusyMemos\" SET \"Subject\" = \"Subject\" WHERE \"Id\" = 1;";
            command.ExecuteNonQuery();
        }
    }

    private sealed class BusyApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly string _directory;
        private readonly ExclusiveLock _lock;

        private BusyApp(IHost host, string directory, ExclusiveLock exclusiveLock, HttpClient client)
        {
            _host = host;
            _directory = directory;
            _lock = exclusiveLock;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<BusyApp> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "vista-busy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var databasePath = Path.Combine(directory, "busy.db");
            var exclusiveLock = new ExclusiveLock(databasePath);
            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSingleton(exclusiveLock);
                        services.AddDbContext<BusyContext>((sp, options) =>
                            SqliteVistaConnection.Use(options, databasePath, busyTimeoutMilliseconds: 1000)
                                .AddInterceptors(sp.GetRequiredService<ExclusiveLock>()));
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<BusyContext>());
                        services.AddVista(v => v.Register<BusyView>());
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
                scope.ServiceProvider.GetRequiredService<BusyContext>().Database.EnsureCreated();
            }

            return new BusyApp(host, directory, exclusiveLock, host.GetTestClient());
        }

        public void ArmLock() => _lock.Armed = true;

        public async Task<BusyResponse> SendAsync(string json, string? ifMatch = null)
        {
            var path = ifMatch is null ? "/api/views/busy-memo/create" : "/api/views/busy-memo/update";
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }

            var response = await Client.SendAsync(request);
            string? retryAfter = null;
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                retryAfter = values.FirstOrDefault();
            }

            var body = await response.Content.ReadAsStringAsync();
            var status = response.StatusCode;
            string? etag = null;
            if (response.Headers.TryGetValues("ETag", out var etags))
            {
                etag = etags.FirstOrDefault();
            }

            response.Dispose();
            return new BusyResponse(status, etag, retryAfter, body);
        }

        public string Subject()
        {
            using var scope = _host.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<BusyContext>().Memos.AsNoTracking().Single().Subject;
        }

        public async ValueTask DisposeAsync()
        {
            _lock.Release();
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

    private sealed record BusyResponse(HttpStatusCode Status, string? ETag, string? RetryAfter, string Body)
    {
        public async Task<string?> CodeAsync()
        {
            using var doc = JsonDocument.Parse(Body);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
    }
}
