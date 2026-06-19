using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace a2n.Vista.AspNetCore.Hosting;

/// <summary>
/// A startup-time hosted service that emits the fail-open warning when no one-door
/// <see cref="IViewAuthorizer"/> was registered (Task 10.4). Without an authorizer, Vista defaults to
/// allow (R7.2); rather than failing silently open, this logs a single <c>Warning</c> at startup so the
/// posture is visible in logs (R7.3).
/// Authoritative behavior: docs/spec/01-view.md §5.6 (Decision Log D43); design.md Property 4
/// ("fail-open sadar"); Requirements R7.2, R7.3.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>AddVistaEndpoints</c> via <c>TryAddEnumerable</c> as an <see cref="IHostedService"/>,
/// so it is added at most once regardless of repeat <c>AddVistaEndpoints</c> calls. It reads the shared
/// <see cref="VistaEndpointOptions"/> singleton and warns only when
/// <see cref="VistaEndpointOptions.HasAuthorizer"/> is <see langword="false"/>.
/// </para>
/// <para>
/// <see cref="StartAsync"/> performs no I/O and returns a completed task, so it does not delay host
/// startup. <see cref="StopAsync"/> is a no-op.
/// </para>
/// </remarks>
public sealed class VistaStartupValidator : IHostedService
{
    private readonly VistaEndpointOptions _options;
    private readonly ILogger<VistaStartupValidator> _logger;

    /// <summary>
    /// Initializes a new <see cref="VistaStartupValidator"/>.
    /// </summary>
    /// <param name="options">The shared endpoint options snapshot (carries the authorizer flag).</param>
    /// <param name="logger">The logger used to emit the fail-open warning.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public VistaStartupValidator(VistaEndpointOptions options, ILogger<VistaStartupValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Emits the fail-open warning once when no authorizer is registered (R7.3); otherwise does nothing.
    /// </summary>
    /// <param name="cancellationToken">A token tied to host startup (unused; no async work is performed).</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.HasAuthorizer)
        {
            _logger.LogWarning(
                "No IViewAuthorizer registered. All Vista views are publicly accessible. "
                + "Register one via AddVistaEndpoints(b => b.UseAuthorizer<T>()) to gate access.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Does nothing; this validator holds no resources.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
