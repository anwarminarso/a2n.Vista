# VISTA0031 — MapWritable target is not a scalar member

| | |
|---|---|
| **ID** | `VISTA0031` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Error |
| **Introduced** | M9 — Source Generator write mapper, Phase 2 (Decision Log D121/D122) |

## Cause

A typed **Style B** writable view (deriving from `a2n.Vista.Authoring.View<TQuery, TCrud>`) declares a
`MapWritable` mapping whose **target member is not a scalar** — it is a navigation (a reference or
collection property) on the source entity rather than a simple scalar member.

The generated write mapper performs only direct **scalar** member assignments. Assigning a navigation
would reopen the mass-assignment surface the whitelist exists to close (D25/D95). This is reported as an
**error**, once **per offending mapping**: the compilation fails and no write mapper is emitted for the
view.

This build-time diagnostic replaces the interim startup fail-fast guard in
`ViewBuilderOfTCrud.ValidateWriteFacet` (D122), so the unsafe condition is reported exactly once and only
at build time.

## Example that triggers VISTA0031

```csharp
using a2n.Vista.Authoring;

public partial class OrderEditView : View<OrderRow, OrderWrite>
{
    public OrderEditView() { }

    protected override void Configure(IViewBuilder<OrderRow, OrderWrite> builder)
        => builder.Named("OrderEdit")
                  .From<Order>(o => new OrderRow { Id = o.Id, CustomerId = o.CustomerId })
                  .Field(x => x.Id, f => f.PrimaryKey())
                  // ❌ Customer is a navigation, not a scalar member
                  .Crud(c => c.MapWritable(w => w.Customer, e => e.Customer));
}
```

## How to fix

Map the navigation's scalar foreign-key member instead, or remove the mapping:

```csharp
protected override void Configure(IViewBuilder<OrderRow, OrderWrite> builder)
    => builder.Named("OrderEdit")
              .From<Order>(o => new OrderRow { Id = o.Id, CustomerId = o.CustomerId })
              .Field(x => x.Id, f => f.PrimaryKey())
              .Crud(c => c.MapWritable(w => w.CustomerId, e => e.CustomerId));   // ✅ scalar FK
```

## When you can ignore it

You should not ignore this: it is an `Error` because the generator cannot safely assign a navigation.
Map the scalar foreign-key member or remove the mapping.

## Related

- [VISTA0030](VISTA0030.md) — Writable view declares no MapWritable mappings.
- [VISTA0032](VISTA0032.md) — MapWritable target is a key or the concurrency token.
- [VISTA0033](VISTA0033.md) — MapWritable chain cannot be analyzed statically.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D121/D122).
