namespace a2n.Vista.Client.TypeScript.Pipeline;

/// <summary>
/// Accumulates non-fatal <see cref="GenerationNotice"/>s during a generation run and hands them
/// back in a deterministic, total order (design "Non-fatal notices"). Ordering the notices means
/// they never perturb byte-for-byte determinism (Requirement 9) and produce a stable success
/// report (Requirement 10.6).
/// </summary>
public sealed class NoticeCollector
{
    private readonly List<GenerationNotice> _notices = new();

    /// <summary>Gets the number of notices collected so far.</summary>
    public int Count => _notices.Count;

    /// <summary>Records a notice.</summary>
    public void Add(GenerationNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _notices.Add(notice);
    }

    /// <summary>Records that a permissive/unconstrained object member degraded to <c>unknown</c> (Requirement 3.6).</summary>
    public void AddPermissiveObjectMember(string view, string property) =>
        Add(GenerationNotice.PermissiveObjectMember(view, property));

    /// <summary>Records that an unrecognized scalar degraded to <c>unknown</c> (Requirement 3.7).</summary>
    public void AddUnrecognizedScalar(string view, string property, string? type, string? format) =>
        Add(GenerationNotice.UnrecognizedScalar(view, property, type, format));

    /// <summary>Records a <c>ViewListResult_*</c> re-lifting fallback (Requirement 2.6, robustness).</summary>
    public void AddEnvelopeShapeFallback(string componentName) =>
        Add(GenerationNotice.EnvelopeShapeFallback(componentName));

    /// <summary>
    /// Returns the collected notices in a deterministic, total order. The collector is not mutated,
    /// so it may be called more than once.
    /// </summary>
    public IReadOnlyList<GenerationNotice> ToSortedList()
    {
        var sorted = new List<GenerationNotice>(_notices);
        sorted.Sort();
        return sorted;
    }
}
