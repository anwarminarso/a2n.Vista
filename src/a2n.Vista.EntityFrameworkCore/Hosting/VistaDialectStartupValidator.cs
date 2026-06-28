using System;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace a2n.Vista.EntityFrameworkCore.Hosting;

/// <summary>
/// A startup-time hosted service that verifies the registered <see cref="IQueryDialect"/> is consistent
/// with the active EF Core provider (Decision Log D107, Requirement R4.6). Mirrors the AspNetCore
/// <c>VistaStartupValidator</c> auth posture, but for the query dialect:
/// <list type="bullet">
/// <item>A <b>provider-specific</b> dialect (for example <see cref="NpgsqlQueryDialect"/>) registered
/// against a <b>mismatched</b> provider is a misconfiguration and <b>throws</b> at startup — its
/// provider functions (such as <c>ILIKE</c>) will not translate on the wrong provider.</item>
/// <item>The <b>default</b> dialect running on <b>PostgreSQL</b> is allowed but <b>warns</b>: PostgreSQL
/// <c>LIKE</c> is case-sensitive, so text search loses case-insensitive parity until
/// <c>AddVistaNpgsql()</c> opts into <c>ILIKE</c>.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>AddVista</c> via <c>TryAddEnumerable</c> as an <see cref="IHostedService"/>, so it is
/// added at most once regardless of repeat <c>AddVista</c> calls. It resolves the application
/// <see cref="DbContext"/> from a startup scope (the same captured-context-type rule the executor uses)
/// to read <see cref="DatabaseFacade.ProviderName"/>.
/// </para>
/// <para>
/// The check is <b>best-effort</b>: when no <see cref="DbContext"/> can be resolved (for example a Gaya
/// B-only setup with no captured context) or the provider name is unavailable, the guard is skipped
/// rather than failing — it can only validate what it can observe.
/// </para>
/// <para>
/// The EF package intentionally does not reference the Npgsql package, so the PostgreSQL provider name is
/// compared as a well-known string constant rather than via <c>NpgsqlQueryDialect.NpgsqlProviderName</c>.
/// </para>
/// </remarks>
public sealed class VistaDialectStartupValidator : IHostedService
{
    /// <summary>
    /// The EF Core provider name for PostgreSQL (Npgsql). Kept as a local constant so the EF package
    /// takes no dependency on the Npgsql provider package.
    /// </summary>
    internal const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly IServiceProvider _serviceProvider;
    private readonly IQueryDialect _dialect;
    private readonly VistaDbContextAccessor _contextAccessor;
    private readonly ILogger<VistaDialectStartupValidator> _logger;

    /// <summary>
    /// Initializes a new <see cref="VistaDialectStartupValidator"/>.
    /// </summary>
    /// <param name="serviceProvider">The root provider, used to open a startup scope and resolve the <see cref="DbContext"/>.</param>
    /// <param name="dialect">The registered query dialect to validate against the active provider.</param>
    /// <param name="contextAccessor">Records the captured concrete <see cref="DbContext"/> type, if any.</param>
    /// <param name="logger">The logger used to emit the case-sensitivity warning.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public VistaDialectStartupValidator(
        IServiceProvider serviceProvider,
        IQueryDialect dialect,
        VistaDbContextAccessor contextAccessor,
        ILogger<VistaDialectStartupValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(contextAccessor);
        ArgumentNullException.ThrowIfNull(logger);
        _serviceProvider = serviceProvider;
        _dialect = dialect;
        _contextAccessor = contextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Compares the registered dialect against the active provider (D107). Specific dialect on a
    /// mismatched provider → throw; default dialect on PostgreSQL → warn; otherwise no-op. The guard is
    /// skipped when no <see cref="DbContext"/> / provider name can be observed.
    /// </summary>
    /// <param name="cancellationToken">A token tied to host startup (unused; the check is synchronous).</param>
    /// <returns>A completed task when the dialect/provider pairing is acceptable.</returns>
    /// <exception cref="InvalidOperationException">
    /// A provider-specific dialect is registered against a different EF Core provider (R4.6).
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var providerName = ResolveProviderName();
        if (string.IsNullOrEmpty(providerName))
        {
            // Nothing to validate against (no resolvable context, or provider name unavailable).
            return Task.CompletedTask;
        }

        var isDefaultDialect = string.Equals(
            _dialect.ProviderName,
            DefaultQueryDialect.AnyRelationalProvider,
            StringComparison.Ordinal);

        if (isDefaultDialect)
        {
            if (string.Equals(providerName, PostgreSqlProviderName, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Vista is using the default query dialect (SQL-standard LIKE) on PostgreSQL "
                    + "('{Provider}'). PostgreSQL LIKE is case-sensitive, so text search will not be "
                    + "case-insensitive. Call AddVistaNpgsql() after AddVista(...) to use ILIKE for "
                    + "case-insensitive parity.",
                    providerName);
            }

            return Task.CompletedTask;
        }

        // A provider-specific dialect must match the active provider, or its provider functions will not
        // translate (fail-fast at startup rather than at first query).
        if (!string.Equals(providerName, _dialect.ProviderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Vista is configured with the '{_dialect.GetType().Name}' query dialect "
                + $"(targeting provider '{_dialect.ProviderName}'), but the application DbContext uses the "
                + $"'{providerName}' provider. The dialect's provider functions will not translate on this "
                + "provider. Register the dialect that matches your provider (for example AddVistaNpgsql() "
                + "for PostgreSQL), or remove the mismatched dialect registration.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Does nothing; this validator holds no resources.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Resolves the active provider name from the application <see cref="DbContext"/> in a startup scope,
    /// using the captured concrete context type when known (the same rule the executor uses) and falling
    /// back to the <see cref="DbContext"/> base registration. Returns <see langword="null"/> when no
    /// context can be resolved.
    /// </summary>
    private string? ResolveProviderName()
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var dbContext = _contextAccessor.ContextType is null
            ? sp.GetService<DbContext>()
            : sp.GetService(_contextAccessor.ContextType) as DbContext;

        return dbContext?.Database.ProviderName;
    }
}
