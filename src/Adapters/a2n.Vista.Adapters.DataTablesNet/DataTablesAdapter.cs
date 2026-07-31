using System;
using System.Collections.Generic;
using System.Globalization;
using a2n.Vista.Adapters;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Adapters.DataTablesNet;

/// <summary>
/// The jQuery DataTables.NET reference adapter (Spec 04 §7). Translates a DataTables server-side request
/// into the neutral <see cref="ViewQueryRequest"/> — populating the three channels (global search →
/// <c>Search</c>, structured <c>jsonQB</c>/per-column → <c>Filter</c>, <c>externalFilter</c> → <c>Scope</c>,
/// Decision Log D111) — and the neutral result back into a <see cref="DataTablesResponse{T}"/>. The three
/// steps are pure; the adapter references <c>a2n.Vista.Core</c> only (Decision Log D48/D66).
/// </summary>
public sealed class DataTablesAdapter : ViewAdapter<DataTablesQuery, DataTablesResponse<object>>
{
    /// <inheritdoc />
    public override string Id => "datatables";

    /// <inheritdoc />
    public override string? RouteSuffix => "datatable";

    /// <inheritdoc />
    public override DataTablesQuery BindRequest(AdapterRequest raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var values = raw.Values;

        var query = new DataTablesQuery
        {
            Draw = GetInt(values, "draw", 0),
            Start = GetInt(values, "start", 0),
            Length = GetInt(values, "length", 0),
            Search = new DtSearch
            {
                Value = GetString(values, "search[value]") ?? string.Empty,
                Regex = GetBool(values, "search[regex]", defaultValue: false),
            },
            JsonQB = GetString(values, "jsonQB") ?? GetString(values, "jsonqb"),
            ExternalFilter = GetString(values, "externalFilter") ?? GetString(values, "externalfilter"),
        };
        // usePGSQL / case-sensitivity flags are read and discarded (provider-detected, D70).

        // Validate the row offset at bind time, mirroring the AG Grid adapter's range check: a negative
        // `start` is a malformed request, not something to silently normalize to the first row.
        if (query.Start < 0)
        {
            throw new AdapterBindException(
                $"The DataTables row offset is invalid: start={query.Start} (require start >= 0).");
        }

        // `regex=true` asks for regular-expression matching, which is not part of the neutral filter
        // contract. Executing it as a literal Contains would silently answer a different question, so it is
        // rejected loudly instead (the same posture as AG Grid's Advanced Filter).
        if (query.Search.Regex)
        {
            throw new AdapterBindException(
                "DataTables regex search (search[regex]=true) is not supported: regular-expression filtering "
                + "is not part of the neutral filter contract.");
        }

        BindColumns(values, query);
        BindOrder(values, query);

        foreach (var column in query.Columns)
        {
            if (column.Search is { Regex: true })
            {
                throw new AdapterBindException(
                    $"DataTables per-column regex search is not supported (column '{column.Data}').");
            }
        }

        return query;
    }

    /// <inheritdoc />
    public override ViewQueryRequest ToQuery(DataTablesQuery request, ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(view);

        var fields = BuildFieldLookup(view);

        // Paging (D144): DataTables is offset-based, so `start` is carried verbatim as the absolute Offset
        // instead of being divided into a page index. Dividing lost rows twice — integer division snapped an
        // unaligned `start`, and the engine's later page-size clamp shifted the window. A non-positive
        // Length is still passed through unchanged so the engine rejects "all rows" (R3.1).
        var pageSize = request.Length;
        var offset = request.Start;

        var sort = BuildSort(request);
        var search = BuildGlobalSearch(request, view);
        var filter = BuildStructuredFilter(request, fields);
        var scope = ExternalFilterParser.Parse(request.ExternalFilter);

        return new ViewQueryRequest(
            Filter: filter,
            Sort: sort,
            Page: 0,
            PageSize: pageSize,
            SelectFields: null,
            Search: search,
            Scope: scope,
            Offset: offset);
    }

    /// <inheritdoc />
    public override DataTablesResponse<object> ToResponse(
        AdapterListResult result,
        DataTablesQuery request,
        ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);

        return new DataTablesResponse<object>
        {
            Draw = request.Draw,
            RecordsTotal = result.RecordsTotal,
            RecordsFiltered = result.RecordsFiltered,
            Data = result.Rows,
        };
    }

    private static List<SortSpec> BuildSort(DataTablesQuery request)
    {
        var sort = new List<SortSpec>(request.Order.Count);
        foreach (var order in request.Order)
        {
            if (order.Column < 0 || order.Column >= request.Columns.Count)
            {
                continue;
            }

            var column = request.Columns[order.Column];
            if (string.IsNullOrEmpty(column.Data))
            {
                // Non-field UI column (e.g. an action column): not sortable.
                continue;
            }

            if (!column.Orderable)
            {
                // The request itself declares the column non-orderable; honor that rather than sorting by it
                // anyway. (The engine's own IsSortable whitelist still applies on top of this.)
                continue;
            }

            var descending = string.Equals(order.Dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort.Add(new SortSpec(column.Data, descending));
        }

        return sort;
    }

    /// <summary>Builds the global-search sub-tree (Contains over the view's searchable string fields).</summary>
    private static FilterNode? BuildGlobalSearch(DataTablesQuery request, ViewMetadata view)
    {
        var value = request.Search.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var leaves = new List<FilterNode>();
        foreach (var field in view.Fields)
        {
            if (field.IsSearchable && field.ClrType == typeof(string))
            {
                leaves.Add(new FilterLeaf(field.Name, FilterOperator.Contains, value));
            }
        }

        return leaves.Count switch
        {
            0 => null,
            1 => leaves[0],
            _ => new FilterOr(leaves),
        };
    }

    /// <summary>
    /// Builds the structured-filter sub-tree: per-column <c>Contains</c> leaves AND the parsed
    /// <c>jsonQB</c> tree, AND-ed together.
    /// </summary>
    private static FilterNode? BuildStructuredFilter(
        DataTablesQuery request,
        IReadOnlyDictionary<string, FieldMetadata> fields)
    {
        var children = new List<FilterNode>();

        foreach (var column in request.Columns)
        {
            // A column the request declares non-searchable contributes no leaf: binding `searchable:false`
            // and then filtering on it anyway answered a different question than the client asked.
            if (!column.Searchable)
            {
                continue;
            }

            var value = column.Search.Value;
            if (!string.IsNullOrEmpty(column.Data) && !string.IsNullOrWhiteSpace(value))
            {
                children.Add(new FilterLeaf(column.Data, FilterOperator.Contains, value));
            }
        }

        var qb = QueryBuilderParser.Parse(request.JsonQB, fields);
        if (qb is not null)
        {
            children.Add(qb);
        }

        return children.Count switch
        {
            0 => null,
            1 => children[0],
            _ => new FilterAnd(children),
        };
    }

    private static Dictionary<string, FieldMetadata> BuildFieldLookup(ViewMetadata view)
    {
        var lookup = new Dictionary<string, FieldMetadata>(view.Fields.Count, StringComparer.Ordinal);
        foreach (var field in view.Fields)
        {
            lookup[field.Name] = field;
        }

        return lookup;
    }

    private static void BindColumns(IReadOnlyDictionary<string, IReadOnlyList<string>> values, DataTablesQuery query)
    {
        for (var i = 0; ; i++)
        {
            var dataKey = $"columns[{i}][data]";
            if (!values.ContainsKey(dataKey) && !values.ContainsKey($"columns[{i}][name]"))
            {
                break;
            }

            query.Columns.Add(new DtColumn
            {
                Data = GetString(values, dataKey) ?? string.Empty,
                Name = GetString(values, $"columns[{i}][name]") ?? string.Empty,
                // Absent → allowed: DataTables always sends both flags, and a hand-built request that omits
                // them means "no per-column restriction". The engine's own whitelist still governs, so
                // default-allow here cannot widen what the view exposes; it only stops an omitted transport
                // flag from silently disabling search/sort.
                Searchable = GetBool(values, $"columns[{i}][searchable]", defaultValue: true),
                Orderable = GetBool(values, $"columns[{i}][orderable]", defaultValue: true),
                Search = new DtSearch
                {
                    Value = GetString(values, $"columns[{i}][search][value]") ?? string.Empty,
                    Regex = GetBool(values, $"columns[{i}][search][regex]", defaultValue: false),
                },
            });
        }
    }

    private static void BindOrder(IReadOnlyDictionary<string, IReadOnlyList<string>> values, DataTablesQuery query)
    {
        for (var k = 0; ; k++)
        {
            var columnKey = $"order[{k}][column]";
            if (!values.ContainsKey(columnKey))
            {
                break;
            }

            query.Order.Add(new DtOrder
            {
                Column = GetInt(values, columnKey, 0),
                Dir = GetString(values, $"order[{k}][dir]") ?? "asc",
            });
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, IReadOnlyList<string>> values, string key) =>
        values.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;

    private static int GetInt(IReadOnlyDictionary<string, IReadOnlyList<string>> values, string key, int fallback)
    {
        var text = GetString(values, key);
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new AdapterBindException($"DataTables parameter '{key}' must be an integer, but was '{text}'.");
        }

        return value;
    }

    private static bool GetBool(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        string key,
        bool defaultValue)
    {
        var text = GetString(values, key);
        return string.IsNullOrEmpty(text)
            ? defaultValue
            : string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
    }
}
