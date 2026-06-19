namespace a2n.Vista.Results;

/// <summary>
/// Immutable, adapter-agnostic page of results returned by the view executor.
/// Carries both the materialized <paramref name="Items"/> and the paging totals a grid
/// needs (for example DataTables' <c>recordsTotal</c>/<c>recordsFiltered</c>).
/// Totals are <see langword="long"/> to avoid overflow on tables larger than ~2.1B rows.
/// Authoritative shape: docs/spec/01-view.md §10.1 (Decision Log D21).
/// </summary>
/// <typeparam name="T">The projected row type of the view.</typeparam>
/// <param name="Items">The materialized rows for the requested page, in result order.</param>
/// <param name="TotalRows">
/// Total number of matching rows after filtering, as a <see langword="long"/> to avoid overflow.
/// </param>
/// <param name="PageIndex">The zero-based index of the returned page.</param>
/// <param name="PageSize">The page size used to compute this page.</param>
/// <param name="TotalPages">
/// Total number of pages for <paramref name="TotalRows"/> at the given <paramref name="PageSize"/>,
/// as a <see langword="long"/> for consistency with <paramref name="TotalRows"/>.
/// </param>
/// <remarks>
/// There is intentionally no public materializer. Materialization stays behind
/// <c>IViewExecutor</c> (async-only, with a <c>CancellationToken</c>) so callers cannot bypass
/// Vista's validation, authorization, and paging limits. See docs/spec/01-view.md §10.2.
/// </remarks>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalRows,
    int PageIndex,
    int PageSize,
    long TotalPages);
