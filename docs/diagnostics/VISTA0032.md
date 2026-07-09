# VISTA0032 — MapWritable target is a key or the concurrency token

| | |
|---|---|
| **ID** | `VISTA0032` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Error |
| **Introduced** | M9 — Source Generator write mapper, Phase 2 (Decision Log D121/D122) |

## Cause

A typed **Style B** writable view (deriving from `a2n.Vista.Authoring.View<TQuery, TCrud>`) declares a
`MapWritable` mapping whose **target is a declared key member** (from `.Key(...)` / `.PrimaryKey()`) or the
**concurrency token** (from `WithConcurrencyToken`).

Key members and the concurrency token are managed by the executor and EF, not by client-supplied write
models. Letting a `MapWritable` mapping overwrite them would break row identity or optimistic concurrency
(D25/D95). This is reported as an **error**, once **per offending member**: the compilation fails and no
write mapper is emitted for the view.

This build-time diagnostic replaces the interim startup fail-fast guard in
`ViewBuilderOfTCrud.ValidateWriteFacet` (D122), so the unsafe condition is reported exactly once and only
at build time.

## Example that triggers VISTA0032

```csharp
using a2n.Vista.Authoring;

public partial class ProductEditView : View<ProductRow, ProductWrite>
{
    public ProductEditView() { }

    protected override void Configure(IViewBuilder<ProductRow, ProductWrite> builder)
        => builder.Named("ProductEdit")
                  .From<Product>(p => new ProductRow { Id = p.Id, Name = p.Name, RowVersion = p.RowVersion })
                  .Field(x => x.Id, f => f.PrimaryKey())
                  .WithConcurrencyToken(x => x.RowVersion)
                  // ❌ Id is the declared key; RowVersion is the concurrency token
                  .Crud(c => c.MapWritable(w => w.Id, e => e.Id)
                              .MapWritable(w => w.RowVersion, e => e.RowVersion));
}
```

## How to fix

Remove the mappings that target the key or concurrency-token members; map only the mutable business
members:

```csharp
protected override void Configure(IViewBuilder<ProductRow, ProductWrite> builder)
    => builder.Named("ProductEdit")
              .From<Product>(p => new ProductRow { Id = p.Id, Name = p.Name, RowVersion = p.RowVersion })
              .Field(x => x.Id, f => f.PrimaryKey())
              .WithConcurrencyToken(x => x.RowVersion)
              .Crud(c => c.MapWritable(w => w.Name, e => e.Name));   // ✅ no key/token targets
```

## When you can ignore it

You should not ignore this: it is an `Error` because assigning a key or the concurrency token breaks
identity or optimistic concurrency. Remove the offending mapping.

## Related

- [VISTA0030](VISTA0030.md) — Writable view declares no MapWritable mappings.
- [VISTA0031](VISTA0031.md) — MapWritable target is not a scalar member.
- [VISTA0033](VISTA0033.md) — MapWritable chain cannot be analyzed statically.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D121/D122).
