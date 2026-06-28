using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.EntityFrameworkCore;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.EntityFrameworkCore.Hosting;
using a2n.Vista.Filter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for the EF startup provider guard (<see cref="VistaDialectStartupValidator"/>, Decision Log
/// D107, Requirement R4.6): a provider-specific dialect on a mismatched provider fails fast at startup,
/// the default dialect on a non-PostgreSQL provider is silent, and the guard is best-effort (skipped
/// when no <see cref="DbContext"/> can be resolved).
/// </summary>
public sealed class DialectStartupGuardTests
{
    [Test]
    public async Task DefaultDialect_On_NonPostgres_Provider_Is_Silent()
    {
        using var provider = BuildProvider(captureWidgetContext: true);
        var logger = new RecordingLogger<VistaDialectStartupValidator>();
        var accessor = provider.GetRequiredService<VistaDbContextAccessor>();
        var validator = new VistaDialectStartupValidator(provider, new DefaultQueryDialect(), accessor, logger);

        await validator.StartAsync(CancellationToken.None);

        await Assert.That(logger.Entries.Any(e => e.Level == LogLevel.Warning)).IsFalse();
    }

    [Test]
    public async Task SpecificDialect_On_Mismatched_Provider_Throws()
    {
        using var provider = BuildProvider(captureWidgetContext: true);
        var accessor = provider.GetRequiredService<VistaDbContextAccessor>();
        var logger = new RecordingLogger<VistaDialectStartupValidator>();
        // SQLite-backed context, but a dialect that targets PostgreSQL — a misconfiguration.
        var validator = new VistaDialectStartupValidator(
            provider,
            new FakeDialect("Npgsql.EntityFrameworkCore.PostgreSQL"),
            accessor,
            logger);

        InvalidOperationException? caught = null;
        try
        {
            await validator.StartAsync(CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("provider");
    }

    [Test]
    public async Task No_Resolvable_DbContext_Is_Skipped()
    {
        // No DbContext registered and no captured context type: the guard cannot observe a provider,
        // so even a "mismatched" specific dialect must not throw (best-effort).
        using var provider = BuildProvider(captureWidgetContext: false);
        var accessor = provider.GetRequiredService<VistaDbContextAccessor>();
        var logger = new RecordingLogger<VistaDialectStartupValidator>();
        var validator = new VistaDialectStartupValidator(
            provider,
            new FakeDialect("Npgsql.EntityFrameworkCore.PostgreSQL"),
            accessor,
            logger);

        await validator.StartAsync(CancellationToken.None);

        await Assert.That(logger.Entries.Any(e => e.Level == LogLevel.Warning)).IsFalse();
    }

    /// <summary>
    /// Builds a service provider with a shared <see cref="VistaDbContextAccessor"/>. When
    /// <paramref name="captureWidgetContext"/> is set, a SQLite-backed <see cref="WidgetContext"/> is
    /// registered and captured so the guard can read its provider name.
    /// </summary>
    private static ServiceProvider BuildProvider(bool captureWidgetContext)
    {
        var services = new ServiceCollection();
        var accessor = new VistaDbContextAccessor();

        if (captureWidgetContext)
        {
            services.AddDbContext<WidgetContext>(o => o.UseSqlite("DataSource=:memory:"));
            accessor.Capture(typeof(WidgetContext));
        }

        services.AddSingleton(accessor);
        return services.BuildServiceProvider();
    }

    /// <summary>A minimal <see cref="IQueryDialect"/> test double with a configurable provider name.</summary>
    private sealed class FakeDialect : IQueryDialect
    {
        public FakeDialect(string providerName) => ProviderName = providerName;

        public string ProviderName { get; }

        public Expression BuildStringMatch(Expression member, string value, StringMatchKind kind) => member;
    }
}
