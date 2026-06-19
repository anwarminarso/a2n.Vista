using a2n.Vista.Results;

namespace a2n.Vista.Ports;

/// <summary>
/// Result of a List facet execution. Wraps the paged, filtered result together with the
/// total row count <b>before</b> any client filter/search was applied, so a grid can report
/// both totals without re-querying. Authoritative behavior: docs/spec/01-view.md §10
/// and Requirement R10.4 (DataTables <c>recordsTotal</c>/<c>recordsFiltered</c>).
/// </summary>
/// <typeparam name="TRow">The projected (read) row type of the view.</typeparam>
/// <param name="Page">
/// The filtered, paged result. <see cref="PagedResult{T}.TotalRows"/> is the count
/// <b>after</b> filtering (maps to DataTables <c>recordsFiltered</c>).
/// </param>
/// <param name="TotalRowsUnfiltered">
/// Total number of rows the view returns with server-trusted scope applied but
/// <b>without</b> the client filter/search (maps to DataTables <c>recordsTotal</c>).
/// A <see langword="long"/> for consistency with <see cref="PagedResult{T}.TotalRows"/> and to
/// avoid overflow on very large tables.
/// </param>
/// <remarks>
/// <para>
/// This wrapper exists so the already-finalized <see cref="PagedResult{T}"/> (Decision Log D21,
/// §10.1) is not modified: <see cref="PagedResult{T}"/> keeps carrying only the filtered total,
/// while the unfiltered total a grid needs lives alongside it here.
/// </para>
/// <para>
/// "Unfiltered" means the row-level security scope from <c>IViewAuthorizer.ShapeQuery</c>
/// (server-trusted, AND-ed into the query) is still applied; only the client-supplied
/// filter and global search are excluded. A user must never see a total that counts rows
/// outside their authorized scope.
/// </para>
/// </remarks>
public sealed record ViewListResult<TRow>(
    PagedResult<TRow> Page,
    long TotalRowsUnfiltered);
