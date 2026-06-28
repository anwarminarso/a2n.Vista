using System.Collections.Generic;

namespace a2n.Vista.Adapters;

/// <summary>
/// A neutral, type-erased List result handed to <see cref="IViewAdapter.ToResponse"/>. The host converts
/// the engine's generic <c>ViewListResult&lt;TRow&gt;</c> (whose row type is only known at runtime) into
/// this shape so the adapter can format a grid response without knowing the row type (Decision Log D111,
/// DR6).
/// </summary>
/// <param name="Rows">The materialized rows for the requested page, already projected by the engine.</param>
/// <param name="RecordsFiltered">
/// Row count after the client filter/search (maps to DataTables <c>recordsFiltered</c>); the engine's
/// <c>ViewListResult.Page.TotalRows</c>.
/// </param>
/// <param name="RecordsTotal">
/// Row count within the current context — server-trusted scope plus the client <c>Scope</c> sub-tree,
/// excluding the client filter/search (maps to DataTables <c>recordsTotal</c>); the engine's
/// <c>ViewListResult.TotalRowsUnfiltered</c>.
/// </param>
public sealed record AdapterListResult(
    IReadOnlyList<object?> Rows,
    long RecordsFiltered,
    long RecordsTotal);
