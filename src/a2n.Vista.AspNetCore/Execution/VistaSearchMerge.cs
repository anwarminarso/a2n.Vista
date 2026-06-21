using System.Collections.Generic;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// Folds a global <c>search</c> string from a List/Export request body into the neutral
/// <see cref="ViewQueryRequest.Filter"/> tree (Decision Log D110): a disjunction of <c>Contains</c> over
/// the view's searchable string fields, AND-ed with any structured filter. The executor compiles the
/// merged tree under the filter whitelist (searchable string fields are filterable by default), so a
/// field opted out of search (for example a masked field, D95) is never probed.
/// </summary>
public static class VistaSearchMerge
{
    /// <summary>Returns <paramref name="request"/> with the global search folded into its filter tree.</summary>
    /// <param name="view">The target view metadata (supplies the searchable fields).</param>
    /// <param name="request">The base neutral request.</param>
    /// <param name="search">The global search text, or <see langword="null"/>/empty for none.</param>
    /// <returns>The request, with search merged when applicable.</returns>
    public static ViewQueryRequest Apply(ViewMetadata view, ViewQueryRequest request, string? search)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(search))
        {
            return request;
        }

        var leaves = new List<FilterNode>();
        foreach (var field in view.Fields)
        {
            if (field.IsSearchable && field.ClrType == typeof(string))
            {
                leaves.Add(new FilterLeaf(field.Name, FilterOperator.Contains, search));
            }
        }

        if (leaves.Count == 0)
        {
            return request;
        }

        FilterNode searchTree = leaves.Count == 1 ? leaves[0] : new FilterOr(leaves);
        FilterNode merged = request.Filter is null
            ? searchTree
            : new FilterAnd(new[] { request.Filter, searchTree });

        return request with { Filter = merged };
    }
}
