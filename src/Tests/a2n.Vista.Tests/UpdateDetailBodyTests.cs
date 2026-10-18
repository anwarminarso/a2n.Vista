// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Ports;
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
/// The update body is the scoped detail read, not a copy of the entity. A masked field stays masked,
/// a row that leaves the caller's scope is 204, and MaxLength applies only to fields the write sets.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration test drives the reflection detail path with an explicit execution plan; trimming is not used for tests.")]
public sealed class UpdateDetailBodyTests
{
    private const string ViewName = "update-detail-body";
    private const string Route = "/api/views/" + ViewName;
    private const string RawSecret = "RAW-SECRET-VALUE";
    private const string LegacyNote = "LEGACY-NOTE-TOO-LONG";
    private const int NameMaxLength = 8;

    /// <summary>
    /// The read plan appends " (view)" to <c>Name</c> and masks <c>Secret</c>. The update body equals
    /// the following detail GET, and the raw secret is absent from the update body.
    /// </summary>
    [Test]
    public async Task Update_Body_Matches_Detail_And_Hides_The_Masked_Field()
    {
        await using var app = await TestApp.StartAsync();
        app.Seed();

        var updated = await app.SendAsync(
            $"{Route}/update",
            "{\"key\":1,\"model\":{\"name\":\"after\",\"bucket\":\"in\"}}",
            "\"7\"");
        var detail = await app.SendAsync($"{Route}/detail", "{\"key\":1}", ifMatch: null);

        await Assert.That(updated.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(updated.Body).IsEqualTo(detail.Body);
        await Assert.That(updated.ETag).IsNotNull();
        await Assert.That(updated.ETag).IsEqualTo(detail.ETag);
        await Assert.That(updated.Body.Contains(RawSecret, StringComparison.Ordinal)).IsFalse();

        using var json = JsonDocument.Parse(updated.Body);
        await Assert.That(json.RootElement.GetProperty("name").GetString()).IsEqualTo("after (view)");
        await Assert.That(json.RootElement.GetProperty("secret").GetString()).IsEqualTo("***");
        await Assert.That(app.ReadName(1)).IsEqualTo("after");
        await Assert.That(app.ReadSecret(1)).IsEqualTo(RawSecret);
    }

    /// <summary>
    /// Moving <c>Bucket</c> out of the trusted scope saves the row, then answers 204 with no body and
    /// no ETag. A later detail and a later update are 404.
    /// </summary>
    [Test]
    public async Task Update_That_Leaves_The_Read_Scope_Is_204_And_A_Later_Read_Is_404()
    {
        await using var app = await TestApp.StartAsync();
        app.Seed();

        var stayed = await app.SendAsync(
            $"{Route}/update",
            "{\"key\":1,\"model\":{\"name\":\"after\",\"bucket\":\"in\"}}",
            "\"7\"");
        await Assert.That(stayed.Status).IsEqualTo(HttpStatusCode.OK);
        var detail = await app.SendAsync($"{Route}/detail", "{\"key\":1}", ifMatch: null);
        await Assert.That(detail.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(stayed.Body).IsEqualTo(detail.Body);
        await Assert.That(stayed.ETag).IsEqualTo(detail.ETag);

        var left = await app.SendAsync(
            $"{Route}/update",
            "{\"key\":1,\"model\":{\"name\":\"after\",\"bucket\":\"out\"}}",
            stayed.ETag!);

        await Assert.That(left.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(left.Body).IsEmpty();
        await Assert.That(left.ETag).IsNull();
        await Assert.That(app.ReadBucket(1)).IsEqualTo("out");

        var gone = await app.SendAsync($"{Route}/detail", "{\"key\":1}", ifMatch: null);
        await Assert.That(gone.Status).IsEqualTo(HttpStatusCode.NotFound);

        var again = await app.SendAsync(
            $"{Route}/update",
            "{\"key\":1,\"model\":{\"name\":\"later\",\"bucket\":\"out\"}}",
            stayed.ETag!);
        await Assert.That(again.Status).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(app.ReadName(1)).IsEqualTo("after");
    }

    /// <summary>
    /// A non-writable column that already exceeds <c>MaxLength</c> does not make the update 422.
    /// A writable field over <c>MaxLength</c> is 422 and leaves the row unchanged.
    /// </summary>
    [Test]
    public async Task Legacy_Overlong_Column_Does_Not_Block_Update_But_A_Writable_Field_Does()
    {
        await using var app = await TestApp.StartAsync();
        app.Seed();

        var tooLong = new string('x', NameMaxLength + 1);
        var rejected = await app.SendAsync(
            $"{Route}/update",
            "{\"key\":1,\"model\":{\"name\":\"" + tooLong + "\",\"bucket\":\"in\"}}",
            "\"7\"");

        await Assert.That(rejected.Status).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(rejected.Body);
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo(WriteErrorCodes.ConstraintViolation);
        await Assert.That(rejected.ETag).IsNull();
        await Assert.That(app.ReadName(1)).IsEqualTo("seed");
        await Assert.That(app.ReadLegacy(1)).IsEqualTo(LegacyNote);
        await Assert.That(app.ReadVersion(1)).IsEqualTo(7);

        var accepted = await app.SendAsync(
            $"{Route}/update",
            "{\"key\":1,\"model\":{\"name\":\"after\",\"bucket\":\"in\"}}",
            "\"7\"");

        await Assert.That(accepted.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(app.ReadName(1)).IsEqualTo("after");
        await Assert.That(app.ReadLegacy(1)).IsEqualTo(LegacyNote);
    }

    internal sealed class MaskSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Secret { get; set; } = string.Empty;

        public string Bucket { get; set; } = string.Empty;

        public int Version { get; set; }

        public string LegacyNote { get; set; } = string.Empty;
    }

    private sealed class MaskRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Secret { get; set; } = string.Empty;

        public int Version { get; set; }
    }

    private sealed class MaskCrud
    {
        public string Name { get; set; } = string.Empty;

        public string Bucket { get; set; } = string.Empty;
    }

    internal sealed class MaskContext : DbContext
    {
        public MaskContext(DbContextOptions<MaskContext> options)
            : base(options)
        {
        }

        public DbSet<MaskSource> Rows => Set<MaskSource>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<MaskSource>();
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(NameMaxLength);
            entity.Property(e => e.LegacyNote).HasMaxLength(4);
            entity.Property(e => e.Version).IsConcurrencyToken();
        }
    }

    private sealed class MaskView : View<MaskRow, MaskCrud>
    {
        protected override void Configure(IViewBuilder<MaskRow, MaskCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<MaskSource>(s => new MaskRow
                {
                    Id = s.Id,
                    Name = s.Name + " (view)",
                    Secret = s.Secret,
                    Version = s.Version,
                })
                .Field(x => x.Id, f => f.PrimaryKey())
                .MaskField(x => x.Secret, _ => true, _ => "***");

            builder
                .CrudOn<MaskSource>()
                .MapWritable(c => c.Name, e => e.Name)
                .MapWritable(c => c.Bucket, e => e.Bucket)
                .WithConcurrencyToken(e => e.Version);
        }
    }

    private sealed class InBucketAuthorizer : IViewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(ViewAuthContext context) => ValueTask.FromResult(true);

        public void ShapeQuery(ViewAuthContext context, IViewScope scope) =>
            scope.AddRowFilter<MaskSource>(s => s.Bucket == "in");
    }

    private sealed class TestApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private TestApp(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            Client = client;
        }

        private HttpClient Client { get; }

        public static async Task<TestApp> StartAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<MaskContext>(o => o.UseSqlite(connection));
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MaskContext>());
                        services.AddVista(v => v.Register<MaskView>(ViewExecutionPlan.Split<MaskSource, MaskRow>(
                            ViewName,
                            s => new MaskRow
                            {
                                Id = s.Id,
                                Name = s.Name + " (view)",
                                Secret = s.Secret,
                                Version = s.Version,
                            })));
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                        services.AddScoped<IViewAuthorizer>(_ => new InBucketAuthorizer());
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
                scope.ServiceProvider.GetRequiredService<MaskContext>().Database.EnsureCreated();
            }

            return new TestApp(host, connection, host.GetTestClient());
        }

        public void Seed()
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaskContext>();
            ctx.Rows.Add(new MaskSource
            {
                Id = 1,
                Name = "seed",
                Secret = RawSecret,
                Bucket = "in",
                Version = 7,
                LegacyNote = LegacyNote,
            });
            ctx.SaveChanges();
        }

        public async Task<(HttpStatusCode Status, string? ETag, string Body)> SendAsync(
            string url,
            string json,
            string? ifMatch)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
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
                etag = string.Concat(values);
            }

            var body = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, etag, body);
        }

        public string? ReadName(int id) => Read(ctx => ctx.Rows.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).Single());

        public string? ReadSecret(int id) => Read(ctx => ctx.Rows.AsNoTracking().Where(s => s.Id == id).Select(s => s.Secret).Single());

        public string? ReadBucket(int id) => Read(ctx => ctx.Rows.AsNoTracking().Where(s => s.Id == id).Select(s => s.Bucket).Single());

        public string? ReadLegacy(int id) => Read(ctx => ctx.Rows.AsNoTracking().Where(s => s.Id == id).Select(s => s.LegacyNote).Single());

        public int ReadVersion(int id) => Read(ctx => ctx.Rows.AsNoTracking().Where(s => s.Id == id).Select(s => s.Version).Single());

        private T Read<T>(Func<MaskContext, T> read)
        {
            using var scope = _host.Services.CreateScope();
            return read(scope.ServiceProvider.GetRequiredService<MaskContext>());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            _connection.Dispose();
        }
    }
}
