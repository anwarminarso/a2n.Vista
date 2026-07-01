# VISTA0020 — Style B executable view has no derivable key

| | |
|---|---|
| **ID** | `VISTA0020` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Error |
| **Introduced** | M10 — Style B Executable, Source Generator Phase 2 (Decision Log D118) |

## Cause

A typed **Style B** view (deriving from `a2n.Vista.Authoring.View<TQuery>` or `View<TQuery, TCrud>`) is
**executable** (its `From<TSource>(...)` projection can be reproduced statically, so a generated execution
plan would be emitted) but it is **provably keyless** at compile time:

- it declares **no key** — neither an explicit view-level `.Key(...)` nor a projected field's
  `.PrimaryKey()`; **and**
- it projects from **more than one source entity**, so single-source primary-key auto-derivation (D105)
  cannot apply.

An executable view needs a key for the deterministic paging tiebreaker (D106) and for Detail-by-key. A
multi-source view with no declared key can never obtain one, so this is reported as an **error**.

A **single-source** view with no declared key is **not** reported here — its key can only be decided at
runtime against `DbContext.Model`. That case is validated by the startup model hook (D105), which derives
the key from the source entity's primary key, or fails closed if the entity has no model primary key.

## Example that triggers VISTA0020

```csharp
using a2n.Vista.Authoring;

public partial class OrderCustomerView : View<OrderCustomerRow>
{
    public OrderCustomerView() { }

    protected override void Configure(IViewBuilder<OrderCustomerRow> builder)
        => builder.Named("OrderCustomer")
                  // ❌ projects across two source entities and declares no key → provably keyless
                  .FromQuery<Order>(
                      db => db.Set<Order>().Join(db.Set<Customer>(), o => o.CustomerId, c => c.Id, (o, c) => new { o, c }),
                      x => new OrderCustomerRow { OrderId = x.o.Id, CustomerName = x.c.Name });
}
```

## How to fix

Declare a key explicitly — an unambiguous, stable column set that identifies a row of the multi-source
projection — via `.Key(...)` or by marking a projected field with `.PrimaryKey()`:

```csharp
protected override void Configure(IViewBuilder<OrderCustomerRow> builder)
    => builder.Named("OrderCustomer")
              .FromQuery<Order>(/* ... */)
              .Field(x => x.OrderId, f => f.PrimaryKey());   // ✅ explicit key for the executable view
```

## When you can ignore it

You should not ignore this: it is an `Error` because an executable view without a key cannot satisfy
deterministic paging or Detail-by-key. Either declare a key, or keep the view single-source so the
startup model hook can derive its key from the EF model.

## Related

- [VISTA0003](VISTA0003.md) — Style B view projection cannot be analyzed statically.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D80).
