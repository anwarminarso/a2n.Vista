// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
/// DateTime and DateTimeOffset concurrency tokens use the round-trip "O" form. A DateTime read back
/// from SQLite is Kind Unspecified; labeling it UTC (without shifting) keeps the create, update, and
/// detail ETags identical. A DateTimeOffset keeps its offset so the WHERE predicate still matches.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests drive the reflection write path by design; trimming is not used for tests.")]
public sealed class DateTimeConcurrencyTokenTests
{
    [Test]
    public async Task DateTime_Round_Trips_Under_Invariant_And_Other_Cultures()
    {
        var utc = new DateTime(2026, 10, 17, 15, 7, 30, 123, DateTimeKind.Utc).AddTicks(4567);
        var unspecified = new DateTime(2026, 10, 17, 15, 7, 30, 123, DateTimeKind.Unspecified).AddTicks(4567);
        var local = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);
        foreach (var cultureName in new[] { "id-ID", "en-US" })
        {
            var previous = CultureInfo.CurrentCulture;
            var previousUi = CultureInfo.CurrentUICulture;
            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;

                var utcWire = ConcurrencyTokenFormat.ToWire(utc);
                await Assert.That(utcWire).IsEqualTo(utc.ToString("O", CultureInfo.InvariantCulture));
                await Assert.That(utcWire!).DoesNotContain(" ");
                await Assert.That(utcWire).EndsWith("Z");
                await Assert.That(RoundTripDateTime(utcWire!)).IsEqualTo(utc);

                var unspecifiedWire = ConcurrencyTokenFormat.ToWire(unspecified);
                var labeled = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc);
                await Assert.That(unspecifiedWire).IsEqualTo(labeled.ToString("O", CultureInfo.InvariantCulture));
                await Assert.That(unspecifiedWire).EndsWith("Z");
                await Assert.That(RoundTripDateTime(unspecifiedWire!).Ticks).IsEqualTo(unspecified.Ticks);

                var localWire = ConcurrencyTokenFormat.ToWire(local);
                var localUtc = local.ToUniversalTime();
                await Assert.That(localWire).IsEqualTo(localUtc.ToString("O", CultureInfo.InvariantCulture));
                await Assert.That(RoundTripDateTime(localWire!)).IsEqualTo(localUtc);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
                CultureInfo.CurrentUICulture = previousUi;
            }
        }
    }

    [Test]
    public async Task DateTimeOffset_Round_Trips_The_Original_Offset()
    {
        var offset = new DateTimeOffset(2026, 10, 17, 15, 7, 30, 123, TimeSpan.FromHours(7)).AddTicks(4567);
        foreach (var cultureName in new[] { "id-ID", "en-US" })
        {
            var previous = CultureInfo.CurrentCulture;
            var previousUi = CultureInfo.CurrentUICulture;
            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;

                var wire = ConcurrencyTokenFormat.ToWire(offset);
                await Assert.That(wire).IsEqualTo(offset.ToString("O", CultureInfo.InvariantCulture));
                await Assert.That(wire!).Contains("+07:00");
                await Assert.That(wire).DoesNotContain(" ");

                await Assert.That(ConcurrencyTokenFormat.TryFromWire(wire!, typeof(DateTimeOffset), out var parsed)).IsTrue();
                var restored = (DateTimeOffset)parsed!;
                await Assert.That(restored.Offset).IsEqualTo(TimeSpan.FromHours(7));
                await Assert.That(restored).IsEqualTo(offset);
                await Assert.That(restored.UtcDateTime).IsEqualTo(offset.UtcDateTime);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
                CultureInfo.CurrentUICulture = previousUi;
            }
        }
    }

    [Test]
    public async Task DateTime_Token_On_File_Sqlite_Matches_Across_Create_Detail_And_Update()
    {
        await using var app = await StampApp.StartAsync(StampKind.DateTime);
        await AssertRoundTripAsync(app, "/api/views/stamp-datetime");
    }

    [Test]
    public async Task DateTimeOffset_Token_On_File_Sqlite_Matches_Across_Create_Detail_And_Update()
    {
        await using var app = await StampApp.StartAsync(StampKind.DateTimeOffset);
        await AssertRoundTripAsync(app, "/api/views/stamp-offset");
    }

    [Test]
    public async Task DateTimeOffset_With_A_Non_Zero_Offset_Matches_The_Detail_ETag()
    {
        await using var app = await StampApp.StartAsync(StampKind.DateTimeOffset, shiftToPlusSeven: true);
        var (create, detail, update) = await AssertRoundTripAsync(app, "/api/views/stamp-offset");
        await Assert.That(create).Contains("+07:00");
        await Assert.That(detail).Contains("+07:00");
        await Assert.That(update).Contains("+07:00");
    }

    private static async Task<(string Create, string Detail, string Update)> AssertRoundTripAsync(StampApp app, string route)
    {
        var created = await app.SendAsync(HttpMethod.Post, route + "/create", "{\"model\":{\"subject\":\"created\"}}");
        await Assert.That(created.Status).IsEqualTo(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(created.Body))
        {
            await Assert.That(body.RootElement.TryGetProperty("key", out _)).IsTrue();
        }

        var createETag = created.ETag!;
        var detail = await app.SendAsync(HttpMethod.Post, route + "/detail", "{\"key\":1}");
        await Assert.That(detail.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail.ETag).IsEqualTo(createETag);

        var updated = await app.SendAsync(
            HttpMethod.Post,
            route + "/update",
            "{\"key\":1,\"model\":{\"subject\":\"updated\"}}",
            detail.ETag);
        await Assert.That(updated.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(updated.ETag).IsNotEqualTo(createETag);

        var afterUpdate = await app.SendAsync(HttpMethod.Post, route + "/detail", "{\"key\":1}");
        await Assert.That(afterUpdate.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(afterUpdate.ETag).IsEqualTo(updated.ETag);

        var usingDetail = await app.SendAsync(
            HttpMethod.Post,
            route + "/update",
            "{\"key\":1,\"model\":{\"subject\":\"from-detail\"}}",
            afterUpdate.ETag);
        await Assert.That(usingDetail.Status).IsEqualTo(HttpStatusCode.OK);

        var afterSecond = await app.SendAsync(HttpMethod.Post, route + "/detail", "{\"key\":1}");
        await Assert.That(afterSecond.ETag).IsEqualTo(usingDetail.ETag);

        var replay = await app.SendAsync(
            HttpMethod.Post,
            route + "/update",
            "{\"key\":1,\"model\":{\"subject\":\"replay\"}}",
            updated.ETag);
        await Assert.That(replay.Status).IsEqualTo(HttpStatusCode.PreconditionFailed);
        await Assert.That(await replay.CodeAsync()).IsEqualTo(WriteErrorCodes.ConcurrencyConflict);
        await Assert.That(app.Count()).IsEqualTo(1);
        await Assert.That(app.Subject()).IsEqualTo("from-detail");

        return (createETag, detail.ETag!, updated.ETag!);
    }

    private static DateTime RoundTripDateTime(string wire)
    {
        var parsed = ConcurrencyTokenFormat.TryFromWire(wire, typeof(DateTime), out var value);
        if (!parsed)
        {
            throw new InvalidOperationException("DateTime wire did not parse.");
        }

        return (DateTime)value!;
    }

    private enum StampKind
    {
        DateTime,
        DateTimeOffset,
    }

    internal sealed class DateMemo
    {
        public int Id { get; set; }

        public string Subject { get; set; } = string.Empty;

        public DateTime RowVersion { get; set; }
    }

    internal sealed class OffsetMemo
    {
        public int Id { get; set; }

        public string Subject { get; set; } = string.Empty;

        public DateTimeOffset RowVersion { get; set; }
    }

    private sealed class StampRow
    {
        public int Id { get; init; }

        public string Subject { get; init; } = string.Empty;

        public DateTime RowVersion { get; init; }
    }

    private sealed class OffsetRow
    {
        public int Id { get; init; }

        public string Subject { get; init; } = string.Empty;

        public DateTimeOffset RowVersion { get; init; }
    }

    private sealed class StampCrud
    {
        public string Subject { get; init; } = string.Empty;
    }

    private sealed class StampViews : ViewTemplate<StampContext>
    {
        protected override void Configure(IViewTemplateBuilder<StampContext> views)
        {
            views.AddView(
                    "stamp-datetime",
                    (db, _) => db.Dates.Select(memo => new StampRow
                    {
                        Id = memo.Id,
                        Subject = memo.Subject,
                        RowVersion = memo.RowVersion,
                    }))
                .Field(row => row.Id, field => field.PrimaryKey())
                .WithCrud<StampCrud, DateMemo>()
                .MapWritable(crud => crud.Subject, entity => entity.Subject)
                .WithConcurrencyToken(entity => entity.RowVersion);

            views.AddView(
                    "stamp-offset",
                    (db, _) => db.Offsets.Select(memo => new OffsetRow
                    {
                        Id = memo.Id,
                        Subject = memo.Subject,
                        RowVersion = memo.RowVersion,
                    }))
                .Field(row => row.Id, field => field.PrimaryKey())
                .WithCrud<StampCrud, OffsetMemo>()
                .MapWritable(crud => crud.Subject, entity => entity.Subject)
                .WithConcurrencyToken(entity => entity.RowVersion);
        }
    }

    internal sealed class StampShift
    {
        public bool ToPlusSeven { get; init; }
    }

    internal sealed class StampContext : DbContext
    {
        private readonly bool _shiftToPlusSeven;

        public StampContext(DbContextOptions<StampContext> options, StampShift shift)
            : base(options)
        {
            _shiftToPlusSeven = shift.ToPlusSeven;
        }

        public DbSet<DateMemo> Dates => Set<DateMemo>();

        public DbSet<OffsetMemo> Offsets => Set<OffsetMemo>();

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ShiftOffset();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ShiftOffset();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DateMemo>().HasKey(entity => entity.Id);
            modelBuilder.Entity<DateMemo>().Property(entity => entity.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
            modelBuilder.Entity<OffsetMemo>().HasKey(entity => entity.Id);
            modelBuilder.Entity<OffsetMemo>().Property(entity => entity.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        }

        private void ShiftOffset()
        {
            if (!_shiftToPlusSeven)
            {
                return;
            }

            foreach (var entry in ChangeTracker.Entries<OffsetMemo>())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified))
                {
                    continue;
                }

                var current = (DateTimeOffset)entry.Property(nameof(OffsetMemo.RowVersion)).CurrentValue!;
                entry.Property(nameof(OffsetMemo.RowVersion)).CurrentValue =
                    current.ToOffset(TimeSpan.FromHours(7));
            }
        }
    }

    private sealed class StampApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly string _directory;
        private readonly StampKind _kind;

        private StampApp(IHost host, string directory, StampKind kind, HttpClient client)
        {
            _host = host;
            _directory = directory;
            _kind = kind;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<StampApp> StartAsync(StampKind kind, bool shiftToPlusSeven = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "vista-stamp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var databasePath = Path.Combine(directory, "stamp.db");
            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSingleton(new StampShift { ToPlusSeven = shiftToPlusSeven });
                        services.AddDbContext<StampContext>((_, options) =>
                            SqliteVistaConnection.Use(options, databasePath));
                        services.AddVista(v => v.RegisterTemplate<StampViews, StampContext>());
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
                scope.ServiceProvider.GetRequiredService<StampContext>().Database.EnsureCreated();
            }

            return new StampApp(host, directory, kind, host.GetTestClient());
        }

        public async Task<StampResponse> SendAsync(HttpMethod method, string path, string json, string? ifMatch = null)
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }

            var response = await Client.SendAsync(request);
            string? etag = null;
            if (response.Headers.TryGetValues("ETag", out var values))
            {
                etag = values.FirstOrDefault();
            }

            var body = await response.Content.ReadAsStringAsync();
            var status = response.StatusCode;
            response.Dispose();
            return new StampResponse(status, etag, body);
        }

        public int Count()
        {
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StampContext>();
            return _kind == StampKind.DateTime ? db.Dates.Count() : db.Offsets.Count();
        }

        public string Subject()
        {
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StampContext>();
            return _kind == StampKind.DateTime
                ? db.Dates.AsNoTracking().Single().Subject
                : db.Offsets.AsNoTracking().Single().Subject;
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

    private sealed record StampResponse(HttpStatusCode Status, string? ETag, string Body)
    {
        public async Task<string?> CodeAsync()
        {
            using var doc = JsonDocument.Parse(Body);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
    }
}
