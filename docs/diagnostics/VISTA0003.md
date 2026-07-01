# VISTA0003 — Style B view projection cannot be analyzed statically

| | |
|---|---|
| **ID** | `VISTA0003` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Warning |
| **Introduced** | M10 — Style B Executable, Source Generator Phase 2 (Decision Log D118) |

## Cause

A typed **Style B** view (deriving from `a2n.Vista.Authoring.View<TQuery>` or `View<TQuery, TCrud>`)
declares a `From<TSource>(...)` source projection — so the author intends the view to be executable — but
that projection **cannot be reproduced statically** by the source generator.

To make a Style B view executable on an AOT-clean path, the generator reproduces its `From<TSource>(...)`
projection as compile-time source (so the consumer compiles the projection expression, with no runtime
reflection). It supports two projection shapes, mirroring `ViewBuilder.ExtractProjectedFields`:

- **member-initialization** — `s => new TRow { Member = s.X, ... }`
- **named-constructor** — `s => new TRow(s.X, ...)` (for example a positional record)

with simple member selections. A projection it cannot reproduce (a non-member-initialization /
non-named-constructor shape, a nested or collection initializer, or a binding that is not a simple member
selection) is **skipped**: no execution plan is generated for the view, the view stays **metadata-only**,
and the generator **continues** generating plans for the remaining views. **No compilation error is
raised.**

When the offending member can be determined, its name is included in the warning message.

## Example that triggers VISTA0003

```csharp
using a2n.Vista.Authoring;

public partial class OrderSummaryView : View<OrderSummary>
{
    public OrderSummaryView() { }

    protected override void Configure(IViewBuilder<OrderSummary> builder)
        => builder.Named("OrderSummary")
                  // ❌ a nested initializer / non-simple binding the generator cannot reproduce
                  .From<Order>(o => new OrderSummary
                  {
                      Id = o.Id,
                      Lines = new LineSummary { Count = o.Lines.Count }, // not a simple member selection
                  });
}
```

## How to fix

Reduce the projection to a member-initialization or named-constructor shape with simple member
selections, so the generator can reproduce it and emit the AOT-clean execution plan:

```csharp
protected override void Configure(IViewBuilder<OrderSummary> builder)
    => builder.Named("OrderSummary")
              .From<Order>(o => new OrderSummary       // ✅ simple member-init bindings
              {
                  Id = o.Id,
                  LineCount = o.Lines.Count,
              })
              .Field(x => x.Id, f => f.PrimaryKey());
```

## When you can ignore it

It is safe to leave as-is: the view remains discoverable as metadata-only and runs through the runtime
(reflection-expression) fallback. You lose only the AOT-clean generated execution plan for that view.
Severity is `Warning` for this reason.

## Related

- [VISTA0020](VISTA0020.md) — Style B executable view has no derivable key.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81).
