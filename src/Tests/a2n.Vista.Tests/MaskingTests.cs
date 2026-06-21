using System.Diagnostics.CodeAnalysis;
using System.Linq;
using a2n.Vista.Authoring;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Requirement R2 (Decision Log D95) — a <c>MaskField</c>'d field defaults to non-probeable, so a
/// client cannot reconstruct the masked value by probing filter/search responses. Exercised through the
/// public Gaya B registration path (<c>AddVista(v =&gt; v.Register&lt;TView&gt;())</c>), then the
/// produced <see cref="ViewMetadata.Fields"/> are asserted:
/// <list type="bullet">
/// <item>R2.1 — a masked field defaults to <c>IsFilterable == false</c>.</item>
/// <item>R2.2 — an explicit <c>Filterable(true)</c> opt-in overrides the masked default.</item>
/// <item>R2.3 — a masked string field defaults to <c>IsSearchable == false</c> (the same Contains
/// probing vector), independently of the filterable axis.</item>
/// </list>
/// </summary>
public sealed class MaskingTests
{
    private const string Il2026 = "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming";
    private const string Why = "Test exercises the runtime reflection authoring path by design; trimming is not used for tests.";

    /// <summary>R2.1 + R2.3: a masked field with no explicit opt-in is neither filterable nor searchable.</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Masked_Field_Defaults_NonFilterable_And_NonSearchable()
    {
        var email = Field(nameof(MaskedRow.Email));

        await Assert.That(email.IsMaskable).IsTrue();
        await Assert.That(email.IsFilterable).IsFalse();
        await Assert.That(email.IsSearchable).IsFalse();
    }

    /// <summary>R2.2: an explicit <c>Filterable(true)</c> on a masked field wins over the masked default.
    /// The (unset) search axis still falls back to the masked default (non-searchable).</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Masked_Field_With_Explicit_Filterable_Stays_Filterable()
    {
        var name = Field(nameof(MaskedRow.Name));

        await Assert.That(name.IsMaskable).IsTrue();
        await Assert.That(name.IsFilterable).IsTrue();   // explicit opt-in wins (R2.2)
        await Assert.That(name.IsSearchable).IsFalse();  // not opted into search → masked default (R2.3)
    }

    /// <summary>An unmasked field keeps the default-allow posture (filterable).</summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task Unmasked_Field_Stays_Filterable()
    {
        var id = Field(nameof(MaskedRow.Id));

        await Assert.That(id.IsMaskable).IsFalse();
        await Assert.That(id.IsFilterable).IsTrue();
    }

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static FieldMetadata Field(string name)
    {
        var services = new ServiceCollection();
        services.AddVista(v => v.Register<MaskedView>());
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IViewRegistry>();
        var view = registry.Get("masked")
            ?? throw new System.InvalidOperationException("View 'masked' was not registered.");
        return view.Fields.Single(f => f.Name == name);
    }
}

/// <summary>The EF source entity for <see cref="MaskedView"/> (POCO; not materialized in a metadata-only test).</summary>
internal sealed class MaskedSource
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>The read projection for <see cref="MaskedView"/>.</summary>
internal sealed class MaskedRow
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// A Gaya B read-only view used to exercise D95: <c>Email</c> is masked with no override (→ non-probeable),
/// <c>Name</c> is masked but explicitly <c>Filterable()</c> (→ filterable, still non-searchable), and
/// <c>Id</c> is the unmasked primary key.
/// </summary>
internal sealed class MaskedView : View<MaskedRow>
{
    protected override void Configure(IViewBuilder<MaskedRow> b) =>
        b.Named("masked")
         .From<MaskedSource>(s => new MaskedRow { Id = s.Id, Email = s.Email, Name = s.Name })
         .Field(x => x.Id, f => f.PrimaryKey())
         .MaskField(x => x.Email, _ => true, _ => "***")
         .MaskField(x => x.Name, _ => true, _ => "***")
         .Field(x => x.Name, f => f.Filterable());
}
