// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.EntityFrameworkCore.Hosting;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using a2n.Vista.Write;
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
/// Regression tests for audit findings <c>BUG-04</c> / <c>BUG-05</c> (Decision Log D146): a Vista-declared
/// optimistic-concurrency token must be model-backed (otherwise the database performs no atomic check and a
/// lost update is possible), and a successful write must report the token the row carries <em>after</em> the
/// write rather than echoing the client's request value.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests drive the reflection registration/write path by design; trimming is not used for tests.")]
public sealed class ConcurrencyTokenGuardTests
{
    private const string GuardedView = "d146-guarded";

    // ---- BUG-04: the declared token must be a model concurrency token -------------------------------

    /// <summary>
    /// A view declaring a token whose property is <b>not</b> configured <c>IsConcurrencyToken()</c> aborts
    /// startup. Previously such a view passed every Vista-level check while the database emitted no
    /// <c>UPDATE ... WHERE token = @original</c> predicate at all — the declaration read as if concurrency
    /// were handled while nothing enforced it.
    /// </summary>
    [Test]
    public async Task Unbacked_Token_Fails_Startup_Closed()
    {
        using var harness = TokenHarness.Create(modelBacksToken: false);

        // Preconditions the guard depends on: the view is writable, its facet declares a token, and the
        // model does NOT back it. Asserted so a fixture regression cannot make this test vacuously pass.
        await Assert.That(harness.DeclaresToken).IsTrue();
        await Assert.That(harness.TokenIsModelBacked).IsFalse();

        var thrown = await Capture(harness.RunStartupValidatorAsync);

        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown!.Message).Contains(GuardedView);
        await Assert.That(thrown.Message).Contains("IsRowVersion");
    }

    /// <summary>A model-backed token passes the startup guard unchanged.</summary>
    [Test]
    public async Task Model_Backed_Token_Passes_Startup()
    {
        using var harness = TokenHarness.Create(modelBacksToken: true);

        await Assert.That(await Capture(harness.RunStartupValidatorAsync)).IsNull();
    }

    // ---- BUG-05: the post-write token is published, not the request's If-Match ----------------------

    /// <summary>
    /// After a successful update the executor publishes the token read back from the row into the
    /// request-scoped <see cref="IWriteTokenSink"/>. Here a save interceptor bumps the token during
    /// <c>SaveChanges</c> — the store-generated <c>rowversion</c> case in miniature — so the published token
    /// must differ from the <c>If-Match</c> the caller supplied. Echoing the request value (the previous
    /// behaviour) handed the client a token that was already stale, guaranteeing its next update a 409.
    /// </summary>
    [Test]
    public async Task Post_Write_Token_Is_Published_And_Differs_From_The_Request_Token()
    {
        using var harness = TokenHarness.Create(modelBacksToken: true, bumpTokenOnSave: true);

        var seeded = harness.Seed(name: "before", token: "v1");

        var updated = await harness.Executor.UpdateAsync(
            harness.View,
            seeded,
            new TokenCrudModel { Name = "after" },
            new ViewScope(),
            concurrencyToken: "v1",
            CancellationToken.None);

        await Assert.That(updated).IsTrue();

        // The sink carries the row's post-write token, which the interceptor bumped away from "v1".
        await Assert.That(harness.Sink.PostWriteToken).IsNotNull();
        await Assert.That(harness.Sink.PostWriteToken).IsNotEqualTo("v1");
        await Assert.That(harness.Sink.PostWriteToken).IsEqualTo(harness.ReadToken(seeded));
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        return null;
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

    private sealed class TokenSourceEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;
    }

    private sealed class TokenRowModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class TokenCrudModel
    {
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>A writable view declaring <c>Token</c> as its optimistic-concurrency token.</summary>
    private sealed class GuardedTokenView : View<TokenRowModel, TokenCrudModel>
    {
        protected override void Configure(IViewBuilder<TokenRowModel, TokenCrudModel> builder)
        {
            builder
                .Named(GuardedView)
                .From<TokenSourceEntity>(s => new TokenRowModel { Id = s.Id, Name = s.Name })
                .Field(x => x.Id, f => f.PrimaryKey());

            builder
                .CrudOn<TokenSourceEntity>()
                .MapWritable(c => c.Name, e => e.Name)
                .WithConcurrencyToken(e => e.Token);
        }
    }

    /// <summary>
    /// The EF context whose model <b>backs</b> the declared token.
    /// </summary>
    /// <remarks>
    /// The two variants are separate CLR types on purpose: EF Core caches the built model per context type,
    /// so one type configured two ways by a constructor flag would silently reuse whichever model was built
    /// first — and the guard would then be tested against the wrong model.
    /// </remarks>
    private sealed class BackedTokenContext : DbContext
    {
        public BackedTokenContext(DbContextOptions<BackedTokenContext> options)
            : base(options)
        {
        }

        public DbSet<TokenSourceEntity> Sources => Set<TokenSourceEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<TokenSourceEntity>();
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Token).IsConcurrencyToken();
        }
    }

    /// <summary>The EF context whose model deliberately does <b>not</b> back the declared token.</summary>
    private sealed class UnbackedTokenContext : DbContext
    {
        public UnbackedTokenContext(DbContextOptions<UnbackedTokenContext> options)
            : base(options)
        {
        }

        public DbSet<TokenSourceEntity> Sources => Set<TokenSourceEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TokenSourceEntity>().HasKey(x => x.Id);
    }

    /// <summary>Bumps the concurrency token of every modified row during <c>SaveChanges</c>.</summary>
    private sealed class TokenBumpInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Bump(eventData);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Bump(eventData);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void Bump(DbContextEventData eventData)
        {
            var context = eventData.Context;
            if (context is null)
            {
                return;
            }

            foreach (var entry in context.ChangeTracker.Entries<TokenSourceEntity>())
            {
                if (entry.State == EntityState.Modified)
                {
                    entry.Entity.Token = Guid.NewGuid().ToString("N");
                }
            }
        }
    }

    private sealed class TokenHarness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly DbContext _context;

        private TokenHarness(
            SqliteConnection connection,
            ServiceProvider provider,
            DbContext context,
            EfViewExecutor executor,
            ViewMetadata view,
            IWriteTokenSink sink)
        {
            _connection = connection;
            _provider = provider;
            _context = context;
            Executor = executor;
            View = view;
            Sink = sink;
        }

        public EfViewExecutor Executor { get; }

        public ViewMetadata View { get; }

        public IWriteTokenSink Sink { get; }

        /// <summary>Whether the registered write facet declares a concurrency token at all.</summary>
        public bool DeclaresToken =>
            _provider.GetRequiredService<IWriteFacetRegistry>().TryGet(GuardedView, out var facet)
            && facet.ConcurrencyToken is not null;

        /// <summary>Whether the EF model treats the token property as a concurrency token.</summary>
        public bool TokenIsModelBacked =>
            _context.Model.FindEntityType(typeof(TokenSourceEntity))?.FindProperty(nameof(TokenSourceEntity.Token))
                ?.IsConcurrencyToken == true;

        [RequiresUnreferencedCode("Registers a Style B view through the reflection authoring path; trimming is not used for tests.")]
        public static TokenHarness Create(bool modelBacksToken, bool bumpTokenOnSave = false)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            DbContext context;
            if (modelBacksToken)
            {
                var builder = new DbContextOptionsBuilder<BackedTokenContext>().UseSqlite(connection);
                if (bumpTokenOnSave)
                {
                    builder.AddInterceptors(new TokenBumpInterceptor());
                }

                context = new BackedTokenContext(builder.Options);
            }
            else
            {
                var builder = new DbContextOptionsBuilder<UnbackedTokenContext>().UseSqlite(connection);
                if (bumpTokenOnSave)
                {
                    builder.AddInterceptors(new TokenBumpInterceptor());
                }

                context = new UnbackedTokenContext(builder.Options);
            }

            context.Database.EnsureCreated();

            var services = new ServiceCollection();
            services.AddLogging(); // the sibling dialect validator resolves a logger when hosted services are enumerated
            services.AddSingleton(context.GetType(), context);
            services.AddSingleton(context);
            services.AddVista(v => v.Register<GuardedTokenView>());
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IViewRegistry>();
            var planRegistry = provider.GetRequiredService<IViewExecutionPlanRegistry>();
            var view = registry.Get(GuardedView)
                ?? throw new InvalidOperationException($"View '{GuardedView}' was not registered.");

            var executor = new EfViewExecutor(context, provider, planRegistry, new FilterCompiler());
            var sink = provider.GetRequiredService<IWriteTokenSink>();

            return new TokenHarness(connection, provider, context, executor, view, sink);
        }

        /// <summary>Runs only the D146 startup validator from the registered hosted services.</summary>
        public Task RunStartupValidatorAsync()
        {
            foreach (var hosted in _provider.GetServices<IHostedService>())
            {
                if (hosted is VistaConcurrencyTokenStartupValidator validator)
                {
                    return validator.StartAsync(CancellationToken.None);
                }
            }

            throw new InvalidOperationException(
                "AddVista did not register the D146 concurrency-token startup validator.");
        }

        public int Seed(string name, string token)
        {
            var row = new TokenSourceEntity { Name = name, Token = token };
            _context.Set<TokenSourceEntity>().Add(row);
            _context.SaveChanges();
            return row.Id;
        }

        public string? ReadToken(int id) =>
            _context.Set<TokenSourceEntity>().AsNoTracking().SingleOrDefault(s => s.Id == id)?.Token;

        public void Dispose()
        {
            _context.Dispose();
            _provider.Dispose();
            _connection.Dispose();
        }
    }
}
