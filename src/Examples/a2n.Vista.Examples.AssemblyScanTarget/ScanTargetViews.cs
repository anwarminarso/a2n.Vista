// Licensed to the a2n.Vista project. Published artifact — English only.

using a2n.Vista.Authoring;

namespace a2n.Vista.Examples.AssemblyScanTarget;

/// <summary>The EF source entity both scan-target views project from.</summary>
public sealed class ScanTargetSource
{
    /// <summary>The primary key.</summary>
    public int Id { get; set; }

    /// <summary>A plain display field.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A field the widget view masks.</summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>The projected row of <see cref="ScanTargetWidgetView"/>.</summary>
public sealed class ScanTargetWidgetRow
{
    /// <summary>The primary key.</summary>
    public int Id { get; set; }

    /// <summary>A plain display field.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A masked field, so a scan can be observed to publish mask specs.</summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>The projected row of <see cref="ScanTargetGadgetView"/>.</summary>
public sealed class ScanTargetGadgetRow
{
    /// <summary>The primary key.</summary>
    public int Id { get; set; }

    /// <summary>A plain display field.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A discoverable read-only view carrying a masked field. Registration must publish its mask specs, which
/// the former metadata-only assembly scan did not do.
/// </summary>
public sealed class ScanTargetWidgetView : View<ScanTargetWidgetRow>
{
    /// <inheritdoc />
    protected override void Configure(IViewBuilder<ScanTargetWidgetRow> builder) =>
        builder.Named("scan-target-widget")
               .From<ScanTargetSource>(s => new ScanTargetWidgetRow
               {
                   Id = s.Id,
                   Name = s.Name,
                   Secret = s.Secret,
               })
               .Field(x => x.Id, f => f.PrimaryKey())
               .MaskField(x => x.Secret, _ => true, _ => "***");
}

/// <summary>A second discoverable read-only view, so the scan is proven to register more than one view.</summary>
public sealed class ScanTargetGadgetView : View<ScanTargetGadgetRow>
{
    /// <inheritdoc />
    protected override void Configure(IViewBuilder<ScanTargetGadgetRow> builder) =>
        builder.Named("scan-target-gadget")
               .From<ScanTargetSource>(s => new ScanTargetGadgetRow
               {
                   Id = s.Id,
                   Name = s.Name,
               })
               .Field(x => x.Id, f => f.PrimaryKey());
}
