// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using a2n.Vista.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Registration-level examples for <c>AddVistaOpenApi(...)</c> (spec openapi-emitter, task 7.1;
/// Requirements 11.2, 10.3). Asserts fail-fast options validation at the composition root and that the
/// configured options are registered as the singleton the builder and serve endpoint read.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "AddVistaOpenApi is RUC because DTO schema generation reflects; trimming is not used for tests.")]
public sealed class OpenApiRegistrationTests
{
    [Test]
    public async Task AddVistaOpenApi_Throws_On_Invalid_Options_At_Registration()
    {
        var services = new ServiceCollection();

        // A relative EndpointPath is invalid; the failure must surface here, not later at request time.
        await Assert.That(() => services.AddVistaOpenApi(o => o.EndpointPath = "openapi/v1.json"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddVistaOpenApi_Registers_Configured_Options_As_Singleton()
    {
        var services = new ServiceCollection();
        services.AddVistaOpenApi(o =>
        {
            o.DocumentTitle = "Configured API";
            o.DocumentVersion = "2.3.4";
        });

        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<VistaOpenApiOptions>();
        await Assert.That(options.DocumentTitle).IsEqualTo("Configured API");
        await Assert.That(options.DocumentVersion).IsEqualTo("2.3.4");

        // The same instance is resolved every time (singleton).
        await Assert.That(provider.GetRequiredService<VistaOpenApiOptions>()).IsSameReferenceAs(options);
    }

    [Test]
    public async Task AddVistaOpenApi_With_Default_Options_Does_Not_Throw()
    {
        var services = new ServiceCollection();
        await Assert.That(() => services.AddVistaOpenApi()).ThrowsNothing();
    }
}
