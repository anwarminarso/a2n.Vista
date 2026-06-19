namespace a2n.Vista.Authoring;

/// <summary>
/// Non-generic marker for a class-per-view ("Gaya B") definition. It lets the registry and other
/// infrastructure hold and inspect a view without knowing its <c>TQuery</c>/<c>TCrud</c> at compile
/// time, avoiding a <c>View&lt;object&gt;</c> (which would be incompatible with
/// <see cref="View{TQuery, TCrud}"/> for lack of covariance).
/// Authoritative shape: docs/spec/01-view.md §5.1.
/// </summary>
/// <remarks>
/// The base classes <see cref="View{TQuery}"/> and <see cref="View{TQuery, TCrud}"/> implement this
/// interface. In the AOT-clean route the source generator (Pilar 3) emits the implementation; until
/// then the base classes implement it directly by running <c>Configure</c> against an internal builder
/// (see <see cref="View{TQuery}"/>).
/// </remarks>
public interface IConfiguredView
{
    /// <summary>The unique view name, as set during authoring via <c>Named("...")</c>.</summary>
    string Name { get; }

    /// <summary>The CLR type of the projected (read) row, <c>TQuery</c>.</summary>
    Type QueryType { get; }

    /// <summary>
    /// The typed write contract <c>TCrud</c>, or <see langword="null"/> for a read-only view.
    /// </summary>
    Type? CrudType { get; }

    /// <summary>
    /// Configures the view through the non-generic core surface. This is the source-generator interop
    /// entry point (§5.1); the runtime base classes dispatch it to the strongly-typed
    /// <c>Configure(IViewBuilder&lt;TQuery&gt;)</c> overload.
    /// </summary>
    /// <param name="builder">The core builder to configure the view with.</param>
    void ConfigureCore(IViewBuilderCore builder);
}
