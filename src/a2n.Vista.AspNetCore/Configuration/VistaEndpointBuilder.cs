using System.Text.Json.Serialization;
using a2n.Vista.AspNetCore.Authorization;
using a2n.Vista.AspNetCore.Serialization;
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

    /// <inheritdoc />
    public IVistaEndpointBuilder AddVistaJsonContext(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Chain the context into the Vista seam (VistaJson.Options), ahead of the reflection fallback.
        VistaJson.AddContext(context);

        // Mirror it into the ASP.NET Core Results/JsonOptions path so any handler that still returns
        // through the framework JSON pipeline resolves the same view DTOs the same way. Inserting at the
        // front of the chain lets the source-generated context win over the framework's default
        // reflection resolver.
        _services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(jsonOptions =>
        {
            var chain = jsonOptions.SerializerOptions.TypeInfoResolverChain;
            if (!chain.Contains(context))
            {
                chain.Insert(0, context);
            }
        });

        return this;
    }

    /// <inheritdoc />
    public IVistaEndpointBuilder DisableVistaReflectionSerializationFallback()
    {
        VistaJson.DisableReflectionFallback();
        return this;
    }
}
