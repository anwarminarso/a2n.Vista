// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Tracking names for the incremental generator pipeline (tasks.md §4.2, requirement R1.3).
//
// Roslyn's incremental generator host can record per-step run information when a driver is created with
// GeneratorDriverOptions.TrackIncrementalGeneratorSteps == true. Steps are looked up in
// GeneratorRunResult.TrackedSteps by NAME, so the pipeline stages we want to assert on must be tagged
// via IncrementalValuesProvider.WithTrackingName(...). These names are the single source of truth shared
// by ViewAccessorGenerator (which tags the stages) and the generator tests (which read them back).
//
// This is purely diagnostic/observability metadata: tagging a stage does not change WHAT the generator
// emits, only that the host remembers the stage's inputs/outputs for cache-reuse assertions. It lets the
// tests prove that an unrelated edit, which leaves a view's equatable model unchanged, serves the
// downstream model step from cache (IncrementalStepRunReason.Cached/Unchanged) rather than recomputing
// it (R1.3, Spec 03 §12).

namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// Well-known <c>WithTrackingName</c> identifiers for the incremental pipeline stages, shared between
    /// the generator and its tests so cache-reuse can be asserted (R1.3).
    /// </summary>
    public static class TrackingNames
    {
        /// <summary>
        /// The stage that yields the equatable <c>ViewModel</c> per discovered view (the semantic
        /// transform output after filtering). When an unrelated edit leaves a view's model unchanged,
        /// this stage's outputs are served from cache, proving the equatable model (R1.3).
        /// </summary>
        public const string ViewModel = "ViewModel";
    }
}
