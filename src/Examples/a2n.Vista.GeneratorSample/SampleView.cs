// Licensed to the a2n.Vista project. Published artifact — English only.
//
// End-to-end generator consumer (Spec source-generator, Task 6.1, R6.4; validates R2.1/R3.2/R3.3).
//
// SampleView is a minimal, VALID, partial typed "Style B" read-only view. Because it is partial and
// derives from a2n.Vista.Authoring.View<TQuery>, the source generator (referenced as an analyzer in
// this project) discovers it and emits — into THIS assembly — a `file static` accessor map plus a
// [ModuleInitializer] that registers those accessors into a2n.Vista.Metadata.ViewAccessorRegistry,
// keyed by the view's runtime Name ("GeneratorSampleView"). Compiling this file proves the generated
// code (the accessor map + the module initializer) is legal in a real consumer assembly; the
// accompanying test proves the registry is populated and the accessors work at runtime.

using a2n.Vista.Authoring;

namespace a2n.Vista.GeneratorSample;

/// <summary>The EF source entity the sample view projects from.</summary>
public sealed class SampleSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>The projected (read) row type — the generator emits one accessor per public property.</summary>
public sealed class SampleRow
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// A minimal partial Style B read-only view. The <c>partial</c> modifier satisfies the generator's
/// VISTA0001 requirement, and the implicit public parameterless constructor lets the generated
/// <c>[ModuleInitializer]</c> instantiate it to read <see cref="View{TQuery}.Name"/> at module load
/// (so VISTA0002 is not triggered).
/// </summary>
public partial class SampleView : View<SampleRow>
{
    /// <inheritdoc />
    protected override void Configure(IViewBuilder<SampleRow> builder)
        => builder.Named("GeneratorSampleView")
                  .From<SampleSource>(s => new SampleRow { Id = s.Id, Name = s.Name })
                  .Field(x => x.Id, f => f.PrimaryKey());
}
