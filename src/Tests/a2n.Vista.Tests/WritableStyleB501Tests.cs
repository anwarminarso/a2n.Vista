// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
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
/// Write-surface guard for the M12 write path (Decision Log D118, D119, D120; Requirements R16.6, R4.7).
/// M10 (D118) made a typed Style B view executable for List/Detail via the generated
/// <see cref="ICompiledViewExecutionPlan"/>; M12 replaced the DR7 <c>501 Not Implemented</c> write stub
/// with a real, wired execution path (<c>HandleWriteAsync</c> → <see cref="IViewExecutor"/> write facet).
/// <list type="bullet">
/// <item>R16.6 — a mapped Create/Update/Delete on a writable view that has a generated plan returns a
/// response <em>other than</em> <c>501</c> over the real ASP.NET Core pipeline (the write facet is now
/// implemented; <c>501</c> is no longer part of the write contract).</item>
/// <item>R4.7 — the generated-plan contract (<see cref="ICompiledViewExecutionPlan"/>) still implements
/// the List/Detail read seam only and carries no write-facet members (DR8 seam split).</item>
/// </list>
/// </summary>
/// <remarks>
/// <see cref="GeneratedExecutionPlanStore"/> is a process-wide static with first-wins idempotency, so the
/// writable view here uses a dedicated, stable name and the plan is seeded directly (as a generated
/// <c>[ModuleInitializer]</c> would) before registration — making the view genuinely executable for reads
/// while the test proves writes are no longer <c>501</c>.
/// </remarks>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration test drives the reflection-based endpoint/authoring path by design; trimming is not used for tests.")]
public sealed class WritableStyleB501Tests
{
    private const string ViewName = "WritableStyleBExec";
    private const string Route = "/api/views/" + ViewName;

    [Test]
    public async Task Create_On_Writable_Generated_View_Is_Not_501()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{Route}/create", JsonContent("{\"model\":{\"name\":\"x\"}}"));

        // R16.6: a mapped write no longer returns 501 — the write facet is implemented and wired.
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NotImplemented);
    }

    [Test]
    public async Task Update_On_Writable_Generated_View_Is_Not_501()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{Route}/update", JsonContent("{\"key\":1,\"model\":{\"name\":\"x\"}}"));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NotImplemented);
    }

    [Test]
    public async Task Delete_On_Writable_Generated_View_Is_Not_501()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{Route}/delete", JsonContent("{\"key\":1}"));

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NotImplemented);
    }

    /// <summary>
    /// R4.7: the generated-plan contract is read-only (List/Detail). The seam exposes the read members
    /// (scoped queryable, member-access, sort appliers, mask accessors) and carries none of the
    /// <see cref="IViewExecutor"/> write-facet members (<c>CreateAsync</c>/<c>UpdateAsync</c>/
    /// <c>DeleteAsync</c>), and it does not inherit the RUC <see cref="IViewExecutionPlan"/>.
    /// </summary>
    [Test]
    public async Task Generated_Plan_Contract_Exposes_List_Detail_Only_No_Write_Members()
    {
        var planType = typeof(ICompiledViewExecutionPlan);

        var memberNames = planType
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToArray();

        // The List/Detail read seam is present.
        await Assert.That(memberNames).Contains(nameof(ICompiledViewExecutionPlan.CreateScopedQueryable));
        await Assert.That(memberNames).Contains(nameof(ICompiledViewExecutionPlan.TryGetMemberAccess));
        await Assert.That(memberNames).Contains(nameof(ICompiledViewExecutionPlan.ApplyPrimarySort));
        await Assert.That(memberNames).Contains(nameof(ICompiledViewExecutionPlan.ApplyThenSort));

        // No write-facet members leak onto the generated read plan (the IViewExecutor write surface).
        var writeFacetMembers = new[] { "CreateAsync", "UpdateAsync", "DeleteAsync" };
        foreach (var name in memberNames)
        {
            await Assert.That(writeFacetMembers.Contains(name)).IsFalse();
        }

        // The compiled plan deliberately does NOT inherit the RUC IViewExecutionPlan (DR8 seam split).
        await Assert.That(typeof(IViewExecutionPlan).IsAssignableFrom(planType)).IsFalse();
    }

    private static StringContent JsonContent(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    /// <summary>EF source entity the writable Style B view projects from (single-source, Id-keyed).</summary>
    private sealed class WritableSource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Projected (read) row type sent to clients.</summary>
    private sealed class WritableRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Typed write contract for the writable Style B view (closes mass-assignment, D38).</summary>
    private sealed class WritableCrud
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>Minimal EF context backing the writable Style B view.</summary>
    private sealed class WritableContext : DbContext
    {
        public WritableContext(DbContextOptions<WritableContext> options)
            : base(options)
        {
        }

        public DbSet<WritableSource> Sources => Set<WritableSource>();
    }

    /// <summary>
    /// A writable class-per-view (Style B) definition: a typed write facet (<c>CrudOn</c> +
    /// <c>MapWritable</c>) makes it non-read-only, so the routing layer maps create/update/delete. It is
    /// single-source over <see cref="WritableSource"/> and declares an explicit primary key.
    /// </summary>
    private sealed class WritableStyleBExecView : View<WritableRow, WritableCrud>
    {
        protected override void Configure(IViewBuilder<WritableRow, WritableCrud> builder)
        {
            builder
                .Named(ViewName)
                .From<WritableSource>(s => new WritableRow { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<WritableSource>()
                .MapWritable(c => c.Name, e => e.Name);
        }
    }

    /// <summary>
    /// A minimal <see cref="IViewExecutor"/> whose write facet always succeeds, used to exercise the
    /// endpoint's write wiring (Task 6.4) independently of the EF write execution (a later task). Create
    /// returns a fixed primary key; Update/Delete report a matched row. The read facet is not exercised
    /// by the write-surface assertions and throws if invoked.
    /// </summary>
    private sealed class FakeWriteExecutor : IViewExecutor
    {
        [RequiresUnreferencedCode("Read facet is out of scope for the write-surface guard test.")]
        public Task<ViewListResult<TRow>> ListAsync<TRow>(
            ViewMetadata view,
            ViewQueryRequest request,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Read execution is out of scope for the write-surface guard test.");

        [RequiresUnreferencedCode("Read facet is out of scope for the write-surface guard test.")]
        public Task<TRow?> DetailAsync<TRow>(
            ViewMetadata view,
            object key,
            IViewScope scope,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Read execution is out of scope for the write-surface guard test.");

        [RequiresUnreferencedCode("Write mapping is resolved from metadata at runtime; use the source generator path for AOT.")]
        public Task<object> CreateAsync<TCrud>(
            ViewMetadata view,
            TCrud model,
            IViewScope scope,
            CancellationToken cancellationToken)
            where TCrud : class =>
            Task.FromResult<object>(1);

        [RequiresUnreferencedCode("Write mapping is resolved from metadata at runtime; use the source generator path for AOT.")]
        public Task<bool> UpdateAsync<TCrud>(
            ViewMetadata view,
            object key,
            TCrud model,
            IViewScope scope,
            string? concurrencyToken,
            CancellationToken cancellationToken)
            where TCrud : class =>
            Task.FromResult(true);

        [RequiresUnreferencedCode("Delete key resolution is built from metadata at runtime; use the source generator path for AOT.")]
        public Task<bool> DeleteAsync(
            ViewMetadata view,
            object key,
            IViewScope scope,
            string? concurrencyToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// A minimal <see cref="ICompiledViewExecutionPlan"/> test double seeded into the store so the view is
    /// a <em>generated</em> (executable) view. Only the contract values the registration path reads
    /// (<see cref="ViewName"/>, <see cref="RowType"/>) are meaningful; the read execution members throw
    /// because these write-surface assertions never drive a List/Detail read.
    /// </summary>
    private sealed class FakeCompiledPlan : ICompiledViewExecutionPlan
    {
        public FakeCompiledPlan(string viewName, Type rowType)
        {
            ViewName = viewName;
            RowType = rowType;
        }

        public string ViewName { get; }

        public Type RowType { get; }

        public Type SourceType => typeof(WritableSource);

        public bool IsSingleSource => true;

        public IReadOnlyList<MaskAccessor> MaskAccessors => Array.Empty<MaskAccessor>();

        public IQueryable CreateScopedQueryable(DbContext dbContext, IServiceProvider services, IViewScope scope) =>
            throw new NotSupportedException("Read execution is out of scope for the write-surface guard test.");

        public bool TryGetMemberAccess(string fieldName, out LambdaExpression accessor) =>
            throw new NotSupportedException("Read execution is out of scope for the write-surface guard test.");

        public IOrderedQueryable ApplyPrimarySort(IQueryable source, string fieldName, bool descending) =>
            throw new NotSupportedException("Read execution is out of scope for the write-surface guard test.");

        public IOrderedQueryable ApplyThenSort(IOrderedQueryable source, string fieldName, bool descending) =>
            throw new NotSupportedException("Read execution is out of scope for the write-surface guard test.");
    }

    /// <summary>A started in-process host + its test client, owning the in-memory SQLite connection.</summary>
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

        public HttpClient Client { get; }

        [RequiresUnreferencedCode("Vista endpoint mapping uses the reflection bridge by design.")]
        public static async Task<TestApp> StartAsync()
        {
            // Seed the generated plan as a [ModuleInitializer] would, BEFORE registration runs, so the
            // writable view is genuinely executable for reads (first-wins idempotency makes this safe).
            GeneratedExecutionPlanStore.Add(ViewName, new FakeCompiledPlan(ViewName, typeof(WritableRow)));

            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<WritableContext>(o => o.UseSqlite(connection));

                        // Style B Register<TView>() does not capture the context type, so Vista's executor
                        // resolves the base DbContext — forward it to the concrete context (the same
                        // pattern the read-path integration/property tests use).
                        services.AddScoped<DbContext>(sp => sp.GetRequiredService<WritableContext>());
                        services.AddVista(v => v.Register<WritableStyleBExecView>());
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());

                        // Task 6.4 wires the write endpoint to the Core IViewExecutor write facet. The EF
                        // implementation of that facet is a later task, so this test drives the endpoint
                        // through a minimal fake executor: it proves the mapper binds, authorizes, forwards,
                        // and maps the outcome (200) — i.e. a mapped write is no longer 501 (R16.6) — without
                        // depending on the EF write execution. Registered after AddVista so it wins.
                        services.AddScoped<IViewExecutor>(_ => new FakeWriteExecutor());
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
                var ctx = scope.ServiceProvider.GetRequiredService<WritableContext>();
                ctx.Database.EnsureCreated();
            }

            return new TestApp(host, connection, host.GetTestClient());
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
