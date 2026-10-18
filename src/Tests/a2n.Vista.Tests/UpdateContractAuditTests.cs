// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
/// The 422 for a renamed writable field names the view field, and an update with no read plan
/// fails before the row is saved.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration test drives the reflection write path and an explicit execution plan; trimming is not used for tests.")]
public sealed class UpdateContractAuditTests
{
    private const string RenamedView = "renamed-length";
    private const string RenamedRoute = "/api/views/" + RenamedView;
    private const string PlanlessView = "planless-write";
    private const int SubjectMaxLength = 8;
    private const string SeedSubject = "seed";
    private const int SeedStamp = 4;

    private static readonly string[] HiddenNames =
    [
        "InternalSubjectCol",
        "InternalRowKey",
        "InternalStampCol",
        "tbl_hidden_subject",
        "col_hidden_subject",
    ];

    /// <summary>
    /// CRUD <c>Title</c> is mapped to <c>InternalSubjectCol</c>. Create and update over
    /// <c>MaxLength</c> are 422, the body names <c>Title</c>, and no entity member, table, or
    /// column name appears. The stored row is unchanged.
    /// </summary>
    [Test]
    public async Task Overlong_Renamed_Field_Names_The_View_Field_On_Create_And_Update()
    {
        await using var app = await RenamedApp.StartAsync();
        app.Seed();
        var tooLong = new string('x', SubjectMaxLength + 1);

        var created = await app.SendAsync(
            $"{RenamedRoute}/create",
            "{\"model\":{\"title\":\"" + tooLong + "\"}}",
            ifMatch: null);
        await AssertViewFieldProblem(created);
        await Assert.That(app.Count()).IsEqualTo(1);
        await Assert.That(app.ReadSubject(1)).IsEqualTo(SeedSubject);
        await Assert.That(app.ReadStamp(1)).IsEqualTo(SeedStamp);

        var updated = await app.SendAsync(
            $"{RenamedRoute}/update",
            "{\"key\":1,\"model\":{\"title\":\"" + tooLong + "\"}}",
            "\"" + SeedStamp + "\"");
        await AssertViewFieldProblem(updated);
        await Assert.That(app.Count()).IsEqualTo(1);
        await Assert.That(app.ReadSubject(1)).IsEqualTo(SeedSubject);
        await Assert.That(app.ReadStamp(1)).IsEqualTo(SeedStamp);
    }

    /// <summary>
    /// A writable view with no execution plan throws the same metadata-only error Detail throws,
    /// and the database row is unchanged.
    /// </summary>
    [Test]
    public async Task Update_Without_An_Execution_Plan_Fails_Before_Save()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<PlanlessContext>(o => o.UseSqlite(connection));
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<PlanlessContext>());
            services.AddVista(v => v.Register<PlanlessWriteView>());

            await using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PlanlessContext>();
                db.Database.EnsureCreated();
                db.Rows.Add(new PlanlessEntity { Id = 1, Name = "kept" });
                db.SaveChanges();
            }

            await Assert.That(provider.GetRequiredService<IViewExecutionPlanRegistry>().Get(PlanlessView)).IsNull();

            Exception? detailError;
            Exception? updateError;
            using (var scope = provider.CreateScope())
            {
                var executor = scope.ServiceProvider.GetRequiredService<IViewExecutor>();
                var view = scope.ServiceProvider.GetRequiredService<IViewRegistry>().Get(PlanlessView)
                    ?? throw new InvalidOperationException("The planless view was not registered.");
                detailError = await Capture(() =>
                    executor.DetailAsync<PlanlessRow>(view, 1, new ViewScope(), CancellationToken.None));
                updateError = await Capture(() =>
                    executor.UpdateAsync(
                        view,
                        1,
                        new PlanlessCrud { Name = "changed" },
                        new ViewScope(),
                        concurrencyToken: null,
                        CancellationToken.None));
            }

            await Assert.That(detailError).IsTypeOf<InvalidOperationException>();
            await Assert.That(updateError).IsTypeOf<InvalidOperationException>();
            await Assert.That(updateError!.Message).IsEqualTo(detailError!.Message);
            await Assert.That(updateError.Message).Contains("metadata-only");
            await Assert.That(updateError.Message).Contains(PlanlessView);

            using var read = provider.CreateScope();
            var name = read.ServiceProvider.GetRequiredService<PlanlessContext>()
                .Rows.AsNoTracking()
                .Single(row => row.Id == 1)
                .Name;
            await Assert.That(name).IsEqualTo("kept");
        }
        finally
        {
            connection.Dispose();
        }
    }

    private static async Task AssertViewFieldProblem((HttpStatusCode Status, string Body) response)
    {
        await Assert.That(response.Status).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(response.Body);
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo(WriteErrorCodes.ConstraintViolation);
        var detail = problem.RootElement.GetProperty("detail").GetString();
        await Assert.That(detail).Contains("Title");
        foreach (var hidden in HiddenNames)
        {
            await Assert.That(response.Body.Contains(hidden, StringComparison.Ordinal)).IsFalse();
            await Assert.That(detail!.Contains(hidden, StringComparison.Ordinal)).IsFalse();
        }
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    internal sealed class RenamedSubjectEntity
    {
        public int InternalRowKey { get; set; }

        public string InternalSubjectCol { get; set; } = string.Empty;

        public int InternalStampCol { get; set; }
    }

    private sealed class RenamedRow
    {
        public int InternalRowKey { get; set; }

        public string Title { get; set; } = string.Empty;
    }

    private sealed class RenamedCrud
    {
        public string Title { get; set; } = string.Empty;
    }

    internal sealed class RenamedContext : DbContext
    {
        public RenamedContext(DbContextOptions<RenamedContext> options)
            : base(options)
        {
        }

        public DbSet<RenamedSubjectEntity> Rows => Set<RenamedSubjectEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<RenamedSubjectEntity>();
            entity.ToTable("tbl_hidden_subject");
            entity.HasKey(e => e.InternalRowKey);
            entity.Property(e => e.InternalSubjectCol).HasMaxLength(SubjectMaxLength).HasColumnName("col_hidden_subject");
            entity.Property(e => e.InternalStampCol).IsConcurrencyToken();
        }
    }

    private sealed class RenamedViewType : View<RenamedRow, RenamedCrud>
    {
        protected override void Configure(IViewBuilder<RenamedRow, RenamedCrud> builder)
        {
            builder
                .Named(RenamedView)
                .From<RenamedSubjectEntity>(s => new RenamedRow
                {
                    InternalRowKey = s.InternalRowKey,
                    Title = s.InternalSubjectCol,
                })
                .Field(x => x.InternalRowKey, f => f.PrimaryKey());

            builder
                .CrudOn<RenamedSubjectEntity>()
                .MapWritable(c => c.Title, e => e.InternalSubjectCol)
                .WithConcurrencyToken(e => e.InternalStampCol);
        }
    }

    internal sealed class PlanlessEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class PlanlessRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class PlanlessCrud
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class PlanlessContext : DbContext
    {
        public PlanlessContext(DbContextOptions<PlanlessContext> options)
            : base(options)
        {
        }

        public DbSet<PlanlessEntity> Rows => Set<PlanlessEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlanlessEntity>().HasKey(e => e.Id);
        }
    }

    private sealed class PlanlessWriteView : View<PlanlessRow, PlanlessCrud>
    {
        protected override void Configure(IViewBuilder<PlanlessRow, PlanlessCrud> builder)
        {
            builder
                .Named(PlanlessView)
                .From<PlanlessEntity>(s => new PlanlessRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder.CrudOn<PlanlessEntity>().MapWritable(c => c.Name, e => e.Name);
        }
    }

    private sealed class RenamedApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private RenamedApp(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            Client = client;
        }

        private HttpClient Client { get; }

        public static async Task<RenamedApp> StartAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<RenamedContext>(o => o.UseSqlite(connection));
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<RenamedContext>());
                        services.AddVista(v => v.Register<RenamedViewType>(ViewExecutionPlan.Split<RenamedSubjectEntity, RenamedRow>(
                            RenamedView,
                            s => new RenamedRow
                            {
                                InternalRowKey = s.InternalRowKey,
                                Title = s.InternalSubjectCol,
                            })));
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
                scope.ServiceProvider.GetRequiredService<RenamedContext>().Database.EnsureCreated();
            }

            return new RenamedApp(host, connection, host.GetTestClient());
        }

        public void Seed()
        {
            using var scope = _host.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<RenamedContext>();
            ctx.Rows.Add(new RenamedSubjectEntity
            {
                InternalRowKey = 1,
                InternalSubjectCol = SeedSubject,
                InternalStampCol = SeedStamp,
            });
            ctx.SaveChanges();
        }

        public int Count() => Read(ctx => ctx.Rows.Count());

        public string? ReadSubject(int id) =>
            Read(ctx => ctx.Rows.AsNoTracking().Where(s => s.InternalRowKey == id).Select(s => s.InternalSubjectCol).Single());

        public int ReadStamp(int id) =>
            Read(ctx => ctx.Rows.AsNoTracking().Where(s => s.InternalRowKey == id).Select(s => s.InternalStampCol).Single());

        public async Task<(HttpStatusCode Status, string Body)> SendAsync(string url, string json, string? ifMatch)
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
            var body = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, body);
        }

        private T Read<T>(Func<RenamedContext, T> read)
        {
            using var scope = _host.Services.CreateScope();
            return read(scope.ServiceProvider.GetRequiredService<RenamedContext>());
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
