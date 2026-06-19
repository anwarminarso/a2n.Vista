namespace a2n.Vista.Authoring;

/// <summary>
/// Non-generic portion of the class-per-view ("Gaya B") builder. It exists so the
/// source-generator interop entry point <see cref="IConfiguredView.ConfigureCore"/> can be expressed
/// without the view's <c>TQuery</c> type parameter. Authoritative shape: docs/spec/01-view.md §5.2.
/// </summary>
/// <remarks>
/// In line with the central template style (Gaya A, §5.5) there is intentionally <b>no</b>
/// <c>Route()</c> or <c>RequireAuthorization()</c> here: routing is global (§5.6) and authorization is
/// centralized via the authorizer (§5.6, Decision Log D43/D44). The strongly-typed
/// <see cref="IViewBuilder{TQuery}"/> re-declares these members with a <see langword="new"/> return
/// type so fluent chains stay strongly typed.
/// </remarks>
public interface IViewBuilderCore
{
    /// <summary>
    /// Sets the unique view name used for registration and routing (<c>{root}/{viewName}</c>, §5.6).
    /// </summary>
    /// <param name="viewName">The view name. Must be non-empty.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilderCore Named(string viewName);

    /// <summary>
    /// Overrides the maximum page size the List facet may return for this view (§7, §11.2).
    /// </summary>
    /// <param name="rows">The maximum number of rows per page.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilderCore MaxPageSize(int rows);

    /// <summary>
    /// Overrides the maximum number of rows an export may produce for this view (§11.2).
    /// </summary>
    /// <param name="rows">The maximum number of export rows.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    IViewBuilderCore MaxExportRows(int rows);
}
