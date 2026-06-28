using System;
using System.Collections.Generic;

namespace a2n.Vista.Adapters.DataTablesNet;

/// <summary>
/// The jQuery DataTables server-side request (the result of <see cref="DataTablesAdapter.BindRequest"/>).
/// Mirrors the DataTables wire shape plus the DynData extras (<c>jsonQB</c>/<c>externalFilter</c>),
/// preserved for migration parity (Spec 04 §7.1).
/// </summary>
public sealed class DataTablesQuery
{
    /// <summary>The DataTables draw counter, echoed back unchanged for request/response correlation.</summary>
    public int Draw { get; set; }

    /// <summary>The zero-based row offset of the requested page.</summary>
    public int Start { get; set; }

    /// <summary>The requested page size; a non-positive value (DataTables <c>-1</c> = "all") is passed through so the engine rejects it.</summary>
    public int Length { get; set; }

    /// <summary>The global search box value.</summary>
    public DtSearch Search { get; set; } = new();

    /// <summary>The column definitions, in display order.</summary>
    public List<DtColumn> Columns { get; set; } = new();

    /// <summary>The ordering instructions, in priority order.</summary>
    public List<DtOrder> Order { get; set; } = new();

    /// <summary>jQuery-QueryBuilder JSON (structured-filter channel); <see langword="null"/> when absent.</summary>
    public string? JsonQB { get; set; }

    /// <summary>Contextual/scoping JSON (scope channel, DynData <c>externalFilter</c>); <see langword="null"/> when absent.</summary>
    public string? ExternalFilter { get; set; }
}

/// <summary>A DataTables search value (global or per-column).</summary>
public sealed class DtSearch
{
    /// <summary>The search text.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Whether the value is a regex (Vista ignores this; search is always Contains).</summary>
    public bool Regex { get; set; }
}

/// <summary>A DataTables column definition.</summary>
public sealed class DtColumn
{
    /// <summary>The bound field name (<c>data</c>); empty for a non-field UI column (for example an action column).</summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>The optional column name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the column participates in search.</summary>
    public bool Searchable { get; set; }

    /// <summary>Whether the column can be ordered.</summary>
    public bool Orderable { get; set; }

    /// <summary>The per-column search value.</summary>
    public DtSearch Search { get; set; } = new();
}

/// <summary>A DataTables ordering instruction.</summary>
public sealed class DtOrder
{
    /// <summary>The index into <see cref="DataTablesQuery.Columns"/> being ordered.</summary>
    public int Column { get; set; }

    /// <summary>The direction (<c>asc</c>/<c>desc</c>).</summary>
    public string Dir { get; set; } = "asc";
}

/// <summary>
/// The jQuery DataTables server-side response (the result of <see cref="DataTablesAdapter.ToResponse"/>).
/// </summary>
/// <typeparam name="T">The row type (here <see cref="object"/>; rows are already projected by the engine).</typeparam>
public sealed class DataTablesResponse<T>
{
    /// <summary>The echoed draw counter.</summary>
    public int Draw { get; set; }

    /// <summary>Total rows in the current context (server-trusted scope + client scope), before the client filter/search.</summary>
    public long RecordsTotal { get; set; }

    /// <summary>Total rows after the client filter/search.</summary>
    public long RecordsFiltered { get; set; }

    /// <summary>The page of rows.</summary>
    public IReadOnlyList<T> Data { get; set; } = Array.Empty<T>();

    /// <summary>An optional native DataTables error message (unused by default; Vista uses Problem Details).</summary>
    public string? Error { get; set; }
}
