using a2n.Vista.Export;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition-root wiring for Vista export writers (Decision Log D115). Lives in the
/// <c>Microsoft.Extensions.DependencyInjection</c> namespace by convention so
/// <see cref="AddVistaExportWriter{TWriter}"/> surfaces on <see cref="IServiceCollection"/> without an
/// extra <c>using</c>.
/// </summary>
public static class VistaExportServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TWriter"/> as an <see cref="IViewExportWriter"/>. Writers are
    /// resolved by their <see cref="IViewExportWriter.Format"/> (case-insensitive); the export endpoint
    /// picks the <b>last</b> registered writer for a format, so a custom writer registered after the
    /// built-ins (CSV/XLSX) overrides the built-in for that format.
    /// </summary>
    /// <typeparam name="TWriter">The export writer implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddVistaExportWriter<TWriter>(this IServiceCollection services)
        where TWriter : class, IViewExportWriter
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IViewExportWriter, TWriter>());
        return services;
    }
}
