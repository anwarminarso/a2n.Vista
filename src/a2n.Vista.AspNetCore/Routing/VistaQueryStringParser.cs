using a2n.Vista.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace a2n.Vista.AspNetCore.Routing;

/// <summary>
/// Builds a neutral <see cref="ViewQueryRequest"/> from an HTTP query string for the List facet
/// (Task 10.3). This is a deliberately small, pragmatic parser: Pilar 1 only needs paging and sorting
/// from the URL so the example and integration tests exercise <see cref="ViewQueryRequest"/> end to end.
/// Rich request shapes (DataTables, jQuery-QueryBuilder, AG Grid, OData) and the structured
/// filter/global-search trees they carry are an adapter concern handled in Pilar 2 (§8.1), which posts
/// the full request body; until then <see cref="ViewQueryRequest.Filter"/> stays <see langword="null"/>.
/// Authoritative behavior: docs/spec/01-view.md §8 (neutral request) and §10 (paging).
/// </summary>
/// <remarks>
/// <para>
/// <b>Supported parameters.</b>
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Page</b> (zero-based): <c>page</c>, falling back to <c>pageIndex</c> then <c>pageNumber</c>.
///   Defaults to <c>0</c> when absent.
///   </description></item>
///   <item><description>
///   <b>Page size</b>: <c>pageSize</c>, falling back to <c>size</c>. Defaults to
///   <see cref="DefaultPageSize"/> when absent. The value is passed through <b>unclamped</b> — the
///   executor clamps it to the view's <c>MaxPageSize</c> and rejects non-positive values such as
///   <c>length=-1</c> (R10.3), so this parser must not silently "fix" a hostile value.
///   </description></item>
///   <item><description>
///   <b>Sort</b>: <c>sort</c> (repeatable and/or comma-separated). Each token names a field; a leading
///   <c>-</c> means descending and a leading <c>+</c> (or no prefix) means ascending — for example
///   <c>sort=Name,-CreatedOn</c>. As a simpler alternative, <c>orderBy=Field</c> combined with the
///   boolean <c>desc</c> is accepted when <c>sort</c> is absent.
///   </description></item>
/// </list>
/// <para>
/// <b>Not parsed in Pilar 1 (Pilar 2 adapters).</b> Structured filters, global search, and explicit
/// field selection (<see cref="ViewQueryRequest.SelectFields"/>) are left unset. The Pilar 1 executor
/// does not honor <see cref="ViewQueryRequest.SelectFields"/>, so parsing it here would imply behavior
/// that does not exist yet.
/// </para>
/// </remarks>
public static class VistaQueryStringParser
{
    /// <summary>
    /// The page size applied when the request supplies none. Kept modest so an unparameterized List
    /// request returns a bounded page; the executor still clamps any larger explicit value to the
    /// view's <c>MaxPageSize</c>.
    /// </summary>
    public const int DefaultPageSize = 20;

    private static readonly string[] PageKeys = ["page", "pageIndex", "pageNumber"];
    private static readonly string[] PageSizeKeys = ["pageSize", "size"];

    /// <summary>
    /// Parses the query string of <paramref name="request"/> into a <see cref="ViewQueryRequest"/>.
    /// </summary>
    /// <param name="request">The current HTTP request whose query string carries the paging/sort parameters.</param>
    /// <returns>
    /// A <see cref="ViewQueryRequest"/> with <see cref="ViewQueryRequest.Page"/>,
    /// <see cref="ViewQueryRequest.PageSize"/>, and <see cref="ViewQueryRequest.Sort"/> populated from the
    /// query string. <see cref="ViewQueryRequest.Filter"/> and <see cref="ViewQueryRequest.SelectFields"/>
    /// are <see langword="null"/> (Pilar 2).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static ViewQueryRequest Parse(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = request.Query;
        var page = ReadInt(query, PageKeys, defaultValue: 0);
        var pageSize = ReadInt(query, PageSizeKeys, defaultValue: DefaultPageSize);
        var sort = ReadSort(query);

        return new ViewQueryRequest(
            Filter: null,
            Sort: sort,
            Page: page,
            PageSize: pageSize,
            SelectFields: null);
    }

    /// <summary>
    /// Returns the first parseable integer found under <paramref name="keys"/> (in order), or
    /// <paramref name="defaultValue"/> when none is present or parseable. Values are read with the
    /// invariant culture so they are not affected by server locale.
    /// </summary>
    private static int ReadInt(IQueryCollection query, string[] keys, int defaultValue)
    {
        foreach (var key in keys)
        {
            if (query.TryGetValue(key, out var values)
                && values.Count > 0
                && int.TryParse(values[^1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// Reads ordering instructions. Prefers the prefix-encoded <c>sort</c> parameter (repeatable and/or
    /// comma-separated, <c>-</c> for descending); falls back to <c>orderBy</c> + <c>desc</c> when
    /// <c>sort</c> is absent. Returns an empty list when neither is supplied.
    /// </summary>
    private static IReadOnlyList<SortSpec> ReadSort(IQueryCollection query)
    {
        if (query.TryGetValue("sort", out var sortValues) && sortValues.Count > 0)
        {
            var specs = new List<SortSpec>();
            foreach (var raw in sortValues)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                foreach (var token in raw!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var spec = ParseSortToken(token);
                    if (spec is not null)
                    {
                        specs.Add(spec);
                    }
                }
            }

            if (specs.Count > 0)
            {
                return specs;
            }
        }

        if (query.TryGetValue("orderBy", out var orderBy) && orderBy.Count > 0 && !string.IsNullOrWhiteSpace(orderBy[^1]))
        {
            var descending = ReadBool(query, "desc");
            return [new SortSpec(orderBy[^1]!.Trim(), descending)];
        }

        return [];
    }

    /// <summary>
    /// Parses a single <c>sort</c> token into a <see cref="SortSpec"/>. A leading <c>-</c> marks
    /// descending order; a leading <c>+</c> (or no prefix) marks ascending. Returns <see langword="null"/>
    /// when the token has no field name after the optional prefix.
    /// </summary>
    private static SortSpec? ParseSortToken(string token)
    {
        var descending = false;
        var field = token;

        if (field.StartsWith('-'))
        {
            descending = true;
            field = field[1..];
        }
        else if (field.StartsWith('+'))
        {
            field = field[1..];
        }

        field = field.Trim();
        return field.Length == 0 ? null : new SortSpec(field, descending);
    }

    /// <summary>
    /// Reads a boolean flag from the query string. Accepts <c>true</c>/<c>false</c> and the common
    /// shorthands <c>1</c>/<c>0</c> (case-insensitive); any other value is treated as <see langword="false"/>.
    /// </summary>
    private static bool ReadBool(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var values) || values.Count == 0)
        {
            return false;
        }

        var value = values[^1];
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal);
    }
}
