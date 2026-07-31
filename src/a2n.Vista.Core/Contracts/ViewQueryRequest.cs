namespace a2n.Vista.Contracts;

/// <summary>
/// Neutral, adapter-agnostic query request consumed by the view executor. Whatever shape
/// a specific grid sends (DataTables, jQuery-QueryBuilder, AG Grid, OData, ...), the
/// adapter (Pilar 2) translates it into this single structure before it reaches Core.
/// Authoritative shape: docs/spec/01-view.md §8.
/// </summary>
/// <param name="Filter">
/// The merged filter tree (structured filter combined with global search by the adapter),
/// or <see langword="null"/> when no filtering is requested.
/// </param>
/// <param name="Sort">Ordering instructions, applied in list order.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Requested page size (clamped to the view's hard limit by the executor).</param>
/// <param name="SelectFields">
/// Optional subset of field names to project; <see langword="null"/> returns all projected fields.
/// </param>
/// <param name="Search">
/// Optional global-search sub-tree, validated under <see cref="FilterOrigin.Search"/> (Contains over
/// searchable string fields only). Kept separate from <paramref name="Filter"/> so each channel is
/// validated against its own whitelist (Decision Log D111). <see langword="null"/> when absent.
/// </param>
/// <param name="Scope">
/// Optional contextual/lookup scoping sub-tree from the client (DynData <c>externalFilter</c>
/// equivalent), validated under <see cref="FilterOrigin.Scope"/> (scopable fields only). It defines the
/// working context, so it counts toward the unfiltered total (Decision Log D111). <see langword="null"/>
/// when absent.
/// </param>
/// <param name="Offset">
/// Optional <b>absolute</b> zero-based row offset (Decision Log D144). When set it is authoritative and
/// <paramref name="Page"/> is ignored; when <see langword="null"/> the executor skips
/// <paramref name="Page"/> × resolved page size as before.
/// </param>
/// <remarks>
/// <para>
/// <b>Why an absolute offset (D144).</b> Grids are offset-based, not page-based: DataTables sends
/// <c>start</c>/<c>length</c> and AG Grid sends <c>startRow</c>/<c>endRow</c>. Deriving a page index by
/// dividing the offset by the <em>client's requested</em> page size loses information twice — integer
/// division snaps an unaligned offset (<c>start=250,length=100</c> → skip 200), and the executor's later
/// clamp of the page size to <see cref="a2n.Vista.Metadata.HardLimits.MaxPageSize"/> then moves the window
/// (<c>start=200,length=200</c> with a cap of 100 → skip 100). Both returned wrong rows with no error.
/// Carrying the offset verbatim keeps clamping a pure size concern: the window start never moves, and a
/// clamped request simply returns fewer rows from the right position.
/// </para>
/// <para>
/// The parameter is optional and defaults to <see langword="null"/>, so the page-based contract and every
/// existing caller are unchanged.
/// </para>
/// </remarks>
public sealed record ViewQueryRequest(
    FilterNode? Filter,
    IReadOnlyList<SortSpec> Sort,
    int Page,
    int PageSize,
    IReadOnlyList<string>? SelectFields = null,
    FilterNode? Search = null,
    FilterNode? Scope = null,
    int? Offset = null);
