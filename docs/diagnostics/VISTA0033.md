# VISTA0033 — Writable view MapWritable chain cannot be analyzed statically

| | |
|---|---|
| **ID** | `VISTA0033` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Warning |
| **Introduced** | M9 — Source Generator write mapper, Phase 2 (Decision Log D121) |

## Cause

A typed **Style B** writable view (deriving from `a2n.Vista.Authoring.View<TQuery, TCrud>`) declares a
`MapWritable` chain the source generator **cannot reproduce statically**.

To emit a reflection-free write mapper, the generator reproduces a view's `MapWritable` chain as
compile-time source (so the consumer compiles the member assignments, with no runtime reflection). It
supports chains of **simple member-selection** mappings. A chain it cannot reproduce — a selector that is
not a simple member selection, or an unrecognized chain shape — is **skipped**: no write mapper is
generated, the view stays **functional**, and its write path uses the runtime **reflection-based**
mapper. **No compilation error is raised** and the generator continues with the remaining views.

When the offending expression can be determined, it is included in the warning message.

## Example that triggers VISTA0033

```csharp
using a2n.Vista.Authoring;

public partial class ProductEditView : View<ProductRow, ProductWrite>
{
    public ProductEditView() { }

    protected override void Configure(IViewBuilder<ProductRow, ProductWrite> builder)
        => builder.Named("ProductEdit")
                  .From<Product>(p => new ProductRow { Id = p.Id, Name = p.Name })
                  .Field(x => x.Id, f => f.PrimaryKey())
                  // ❌ target selector is not a simple member selection
                  .Crud(c => c.MapWritable(w => w.Name, e => Normalize(e.Name)));
}
```

## How to fix

Reduce each `MapWritable` selector to a simple member selection so the generator can reproduce the chain
and emit the reflection-free write mapper. Perform any normalization in the executor / write model rather
than in the mapping selector:

```csharp
protected override void Configure(IViewBuilder<ProductRow, ProductWrite> builder)
    => builder.Named("ProductEdit")
              .From<Product>(p => new ProductRow { Id = p.Id, Name = p.Name })
              .Field(x => x.Id, f => f.PrimaryKey())
              .Crud(c => c.MapWritable(w => w.Name, e => e.Name));   // ✅ simple member selection
```

## When you can ignore it

It is safe to leave as-is: the view remains fully functional and its write path runs through the runtime
(reflection-based) mapper. You lose only the reflection-free generated write mapper for that view.
Severity is `Warning` for this reason.

## Related

- [VISTA0030](VISTA0030.md) — Writable view declares no MapWritable mappings.
- [VISTA0031](VISTA0031.md) — MapWritable target is not a scalar member.
- [VISTA0032](VISTA0032.md) — MapWritable target is a key or the concurrency token.
- [VISTA0003](VISTA0003.md) — Style B view projection cannot be analyzed statically.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D121).
