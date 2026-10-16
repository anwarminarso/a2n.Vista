namespace a2n.Vista.Diagnostics;

/// <summary>
/// A failure that carries a stable, low-cardinality wire code suitable for traces and metrics.
/// Telemetry copies <see cref="ErrorCode"/> and never the exception message, so the value must be a
/// fixed token (lowercase letters, digits, and single hyphens) — never row values, SQL, field
/// contents, or caller-supplied text.
/// </summary>
/// <remarks>
/// Write and filter exceptions in Core implement this. AspNetCore request failures do too, so the
/// HTTP layer and the EF executor share one classification path without either package referencing
/// the other's exception types beyond the interface. A later write-error mapping that introduces
/// new wire codes (for example <c>write-constraint-violation</c> or <c>write-persistence-failed</c>)
/// is picked up automatically as long as the exception reports that code here.
/// </remarks>
public interface IVistaErrorCode
{
    /// <summary>
    /// The stable wire code for this failure (for example <c>write-conflict</c> or
    /// <c>filter-field-not-allowed</c>).
    /// </summary>
    string ErrorCode { get; }
}
