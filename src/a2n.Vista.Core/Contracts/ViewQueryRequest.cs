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
public sealed record ViewQueryRequest(
    FilterNode? Filter,
    IReadOnlyList<SortSpec> Sort,
    int Page,
    int PageSize,
    IReadOnlyList<string>? SelectFields = null,
    FilterNode? Search = null,
    FilterNode? Scope = null);
