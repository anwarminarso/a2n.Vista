using a2n.Vista.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace a2n.Vista.AspNetCore.Configuration;

/// <summary>
/// Default <see cref="IVistaEndpointBuilder"/>. Mutates the shared <see cref="VistaEndpointOptions"/>
/// singleton and registers the one-door authorizer into the service collection. Created and driven by
/// <c>AddVistaEndpoints</c>; not intended for direct construction by application code.
/// </summary>
internal sealed class VistaEndpointBuilder : IVistaEndpointBuilder
{
    private readonly IServiceCollection _services;
    private readonly VistaEndpointOptions _options;

    internal VistaEndpointBuilder(IServiceCollection services, VistaEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _services = services;
        _options = options;
    }

    /// <inheritdoc />
    public IVistaEndpointBuilder UseAuthorizer<T>() where T : class, IViewAuthorizer
    {
        // Scoped so the authorizer may depend on request-scoped services (see IVistaEndpointBuilder).
        _services.TryAddScoped<IViewAuthorizer, T>();
        _options.AuthorizerType = typeof(T);
        return this;
    }

    /// <inheritdoc />
    public IVistaEndpointBuilder AllowAnonymousAccess()
    {
        _options.AllowAnonymous = true;
        return this;
    }

    /// <inheritdoc />
    public IVistaEndpointBuilder EnableMetadataCaching(int maxAgeSeconds = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxAgeSeconds);
        _options.EnableMetadataCaching = true;
        _options.MetadataCacheMaxAgeSeconds = maxAgeSeconds;
        return this;
    }
}
