namespace a2n.Vista.Metadata;

/// <summary>
/// Per-view hard limits that the executor and endpoints enforce to bound query cost.
/// Authoritative shape: docs/spec/01-view.md §5.4 (referenced as <c>Limits</c>) and §11.2.
/// </summary>
/// <param name="MaxPageSize">
/// Maximum number of rows a single page may return. Requests exceeding this are clamped or
/// rejected; <c>length=-1</c> (no paging) is never honoured (§7, §11.2).
/// </param>
/// <param name="MaxExportRows">
/// Maximum number of rows an export may produce, enforced before the export pipeline runs.
/// The global default is 100,000 rows; an absolute cap of 1,000,000 cannot be bypassed (§11.2).
/// </param>
public sealed record HardLimits(int MaxPageSize, int MaxExportRows)
{
    /// <summary>Default maximum page size applied when a view does not override it.</summary>
    public const int DefaultMaxPageSize = 100;

    /// <summary>Default maximum export row count applied when a view does not override it (§11.2).</summary>
    public const int DefaultMaxExportRows = 100_000;

    /// <summary>Absolute export cap that cannot be bypassed by per-view configuration (§11.2).</summary>
    public const int AbsoluteMaxExportRows = 1_000_000;

    /// <summary>Default maximum filter-tree nesting depth (Decision Log D108, §8.3, D61).</summary>
    public const int DefaultMaxFilterDepth = 16;

    /// <summary>Default maximum number of filter leaves in one request (Decision Log D108, §8.3).</summary>
    public const int DefaultMaxFilterLeaves = 128;

    /// <summary>Default maximum length of a single client-supplied filter string value (Decision Log D108, §8.3).</summary>
    public const int DefaultMaxFilterStringLength = 4096;

    /// <summary>Default maximum number of values an <c>In</c> operator may carry (Decision Log D108, §8.2).</summary>
    public const int DefaultMaxInValues = 1000;

    /// <summary>
    /// Maximum filter-tree nesting depth a client request may carry; exceeding it is a 400
    /// (Decision Log D108). Defaults to <see cref="DefaultMaxFilterDepth"/>.
    /// </summary>
    public int MaxFilterDepth { get; init; } = DefaultMaxFilterDepth;

    /// <summary>
    /// Maximum number of filter leaves a client request may carry; exceeding it is a 400
    /// (Decision Log D108). Defaults to <see cref="DefaultMaxFilterLeaves"/>.
    /// </summary>
    public int MaxFilterLeaves { get; init; } = DefaultMaxFilterLeaves;

    /// <summary>
    /// Maximum length of a single client-supplied filter string value; exceeding it is a 400
    /// (Decision Log D108). Defaults to <see cref="DefaultMaxFilterStringLength"/>.
    /// </summary>
    public int MaxFilterStringLength { get; init; } = DefaultMaxFilterStringLength;

    /// <summary>
    /// Maximum number of values an <c>In</c> operator may carry; exceeding it is a 400
    /// (Decision Log D108). Defaults to <see cref="DefaultMaxInValues"/>.
    /// </summary>
    public int MaxInValues { get; init; } = DefaultMaxInValues;

    /// <summary>The default hard limits applied to a view that does not customize them.</summary>
    public static HardLimits Default { get; } = new(DefaultMaxPageSize, DefaultMaxExportRows);
}
