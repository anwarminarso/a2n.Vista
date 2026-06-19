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

    /// <summary>The default hard limits applied to a view that does not customize them.</summary>
    public static HardLimits Default { get; } = new(DefaultMaxPageSize, DefaultMaxExportRows);
}
