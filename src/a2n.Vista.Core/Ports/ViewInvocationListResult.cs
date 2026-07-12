using System.Collections.Generic;

namespace a2n.Vista.Ports;

/// <summary>
/// The type-erased result of a List facet invocation produced by a generated <see cref="IViewInvoker"/>,
/// carrying everything the HTTP layer consumes today without reflecting over
/// <see cref="ViewListResult{TRow}"/> or <c>PagedResult&lt;TRow&gt;</c> (Decision Log D123, Requirement
/// R2.2). It replaces the reflection-based <c>ViewRequestExecutor.ToAdapterResult</c>.
/// </summary>
/// <param name="BoxedResult">
/// The closed <see cref="ViewListResult{TRow}"/> instance, boxed as <see cref="object"/>, for
/// List JSON serialization through the serialization seam.
/// </param>
/// <param name="Rows">
/// The materialized rows of the page (the closed <c>PagedResult&lt;TRow&gt;.Items</c>), boxed as
/// <see cref="object"/> elements, for the adapter and export paths.
/// </param>
/// <param name="TotalRowsFiltered">
/// The count after the client filter/search was applied (the closed
/// <c>PagedResult&lt;TRow&gt;.TotalRows</c>; DataTables <c>recordsFiltered</c>).
/// </param>
/// <param name="TotalRowsUnfiltered">
/// The count with only the server-trusted scope applied (the closed
/// <see cref="ViewListResult{TRow}.TotalRowsUnfiltered"/>; DataTables <c>recordsTotal</c>).
/// </param>
/// <remarks>
/// The generated invoker fills all four members from the compile-time closed
/// <see cref="ViewListResult{TRow}"/> via direct member access (no reflection), keeping the read
/// dispatch path AOT-clean. This type depends only on Core and BCL types and introduces no
/// System.Text.Json, EF, or ASP.NET Core dependency into <c>a2n.Vista.Core</c>.
/// </remarks>
public sealed record ViewInvocationListResult(
    object BoxedResult,
    IReadOnlyList<object?> Rows,
    long TotalRowsFiltered,
    long TotalRowsUnfiltered);
