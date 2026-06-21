using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace a2n.Vista.AspNetCore.Hosting;

/// <summary>
/// A startup-time hosted service that enforces Vista's fail-safe authorization posture (D94). When no
/// one-door <see cref="IViewAuthorizer"/> is registered, the behavior depends on the hosting
/// environment: in Development, access defaults to allow and a single startup <c>Warning</c> is logged
/// (frictionless dev, R7.3); in any non-Development environment it is a **fail-closed startup error**
/// unless anonymous access was explicitly opted into via
/// <see cref="IVistaEndpointBuilder.AllowAnonymousAccess"/>. This ensures a forgotten authorizer cannot
/// silently expose every view in production.
/// Authoritative behavior: docs/spec/01-view.md §5.6 + §13.2 (Decision Log D94, revising D43);
/// Requirements R1.2, R1.3, R1.4, R1.5.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>AddVistaEndpoints</c> via <c>TryAddEnumerable</c> as an <see cref="IHostedService"/>,
/// so it is added at most once regardless of repeat <c>AddVistaEndpoints</c> calls. It reads the shared
/// <see cref="VistaEndpointOptions"/> singleton and the ambient <see cref="IHostEnvironment"/>.
/// </para>
/// <para>
/// <see cref="StartAsync"/> performs no I/O. When it throws (the fail-closed case), the exception
/// propagates through host startup and aborts the application, which is the intended behavior.
/// <see cref="StopAsync"/> is a no-op.
/// </para>
/// </remarks>
public sealed class VistaStartupValidator : IHostedService
{
    private readonly VistaEndpointOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<VistaStartupValidator> _logger;

    /// <summary>
    /// Initializes a new <see cref="VistaStartupValidator"/>.
    /// </summary>
    /// <param name="options">The shared endpoint options snapshot (authorizer + anonymous-opt-in flags).</param>
    /// <param name="environment">The hosting environment, used to decide warn vs fail-closed (D94).</param>
    /// <param name="logger">The logger used to emit the posture warning.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public VistaStartupValidator(
        VistaEndpointOptions options,
        IHostEnvironment environment,
        ILogger<VistaStartupValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Enforces the fail-safe auth posture (D94). Authorizer present → no-op. No authorizer: in
    /// Development → warn; in non-Development with an explicit <c>AllowAnonymousAccess()</c> opt-in →
    /// warn (open by choice); in non-Development without the opt-in → throw to abort startup.
    /// </summary>
    /// <param name="cancellationToken">A token tied to host startup (unused; no async work is performed).</param>
    /// <returns>A completed task when the posture is acceptable.</returns>
    /// <exception cref="InvalidOperationException">
    /// No authorizer is registered in a non-Development environment and anonymous access was not
    /// explicitly opted into (R1.3).
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.HasAuthorizer)
        {
            return Task.CompletedTask;
        }

        if (_environment.IsDevelopment())
        {
            // Frictionless development: open + warn (R1.2).
            _logger.LogWarning(
                "No IViewAuthorizer registered. All Vista views are publicly accessible in the "
                + "'{Environment}' environment. Register one via AddVistaEndpoints(b => b.UseAuthorizer<T>()) "
                + "to gate access.",
                _environment.EnvironmentName);
            return Task.CompletedTask;
        }

        if (_options.AllowAnonymous)
        {
            // Explicit, reviewed opt-in to open access in a non-Development environment (R1.4).
            _logger.LogWarning(
                "No IViewAuthorizer registered, but AllowAnonymousAccess() was called. All Vista views "
                + "are publicly accessible in the '{Environment}' environment by explicit opt-in.",
                _environment.EnvironmentName);
            return Task.CompletedTask;
        }

        // Fail-closed: a forgotten authorizer must not silently expose views outside Development (R1.3).
        throw new InvalidOperationException(
            $"No IViewAuthorizer is registered and the '{_environment.EnvironmentName}' environment is not "
            + "Development, so Vista refuses to start with all views publicly accessible. Register an "
            + "authorizer via AddVistaEndpoints(b => b.UseAuthorizer<T>()), or, if open access is "
            + "intentional, opt in explicitly via AddVistaEndpoints(b => b.AllowAnonymousAccess()).");
    }

    /// <summary>Does nothing; this validator holds no resources.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
