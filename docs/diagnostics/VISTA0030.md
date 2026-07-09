# VISTA0030 — Writable view declares no MapWritable mappings

| | |
|---|---|
| **ID** | `VISTA0030` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Error |
| **Introduced** | M9 — Source Generator write mapper, Phase 2 (Decision Log D121/D122) |

## Cause

A typed **Style B** writable view (deriving from `a2n.Vista.Authoring.View<TQuery, TCrud>`) exposes a
**CRUD facet** but declares **zero `MapWritable` mappings**. The generated write mapper would therefore
assign nothing.

Mass assignment in Vista is **default-deny** (D25/D95): a writable view only assigns members that are
explicitly whitelisted through `MapWritable`. An empty whitelist produces a mapper that writes no member,
which is almost always an authoring mistake rather than an intentional no-op. This is reported as an
**error**: the compilation fails and no write mapper is emitted for the view.

This build-time diagnostic replaces the interim startup fail-fast guard in
`ViewBuilderOfTCrud.ValidateWriteFacet` (D122), so the unsafe condition is reported exactly once and only
at build time.

## Example that triggers VISTA0030

```csharp
using a2n.Vista.Authoring;

public partial class ProductEditView : View<ProductRow, ProductWrite>
{
    public ProductEditView() { }

    protected override void Configure(IViewBuilder<ProductRow, ProductWrite> builder)
        => builder.Named("ProductEdit")
                  .From<Product>(p => new ProductRow { Id = p.Id, Name = p.Name })
                  .Field(x => x.Id, f => f.PrimaryKey())
                  .Crud(c => { /* ❌ no MapWritable mappings declared */ });
}
```

## How to fix

Declare the members the write model is allowed to assign, or remove the CRUD facet if the view is not
writable:

```csharp
protected override void Configure(IViewBuilder<ProductRow, ProductWrite> builder)
    => builder.Named("ProductEdit")
              .From<Product>(p => new ProductRow { Id = p.Id, Name = p.Name })
              .Field(x => x.Id, f => f.PrimaryKey())
              .Crud(c => c.MapWritable(w => w.Name, e => e.Name));   // ✅ explicit whitelist
```

## When you can ignore it

You should not ignore this: it is an `Error` because a writable view whose whitelist is empty cannot
perform any write. Either declare the intended `MapWritable` mappings or drop the CRUD facet.

## Related

- [VISTA0031](VISTA0031.md) — MapWritable target is not a scalar member.
- [VISTA0032](VISTA0032.md) — MapWritable target is a key or the concurrency token.
- [VISTA0033](VISTA0033.md) — MapWritable chain cannot be analyzed statically.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D121/D122).
