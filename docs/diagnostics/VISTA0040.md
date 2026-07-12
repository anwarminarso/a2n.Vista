# VISTA0040 — Style B view cannot receive a generated HTTP dispatch invoker

| | |
|---|---|
| **ID** | `VISTA0040` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Info |
| **Introduced** | M9 — Source Generator HTTP surface (Decision Log D123) |

## Cause

A typed **Style B** view (deriving from `a2n.Vista.Authoring.View<TQuery>` or `View<TQuery, TCrud>`) is
recognized as an HTTP-surface **base candidate**, but it cannot receive a generated **dispatch invoker**
because its projected row type (`TQuery`) — or, for a writable view, its write model (`TCrud`) — is
**anonymous or `object`** rather than a **named** type.

The Vista source generator emits a reflection-free HTTP dispatch invoker only for a view whose `TQuery` is
a named type (and, when writable, whose `TCrud` is a named type). The invoker closes the generic executor
facets (`ListAsync<TRow>` / `DetailAsync<TRow>` / `CreateAsync<TCrud>` / `UpdateAsync<TCrud>`) at compile
time, so a named type is required. An anonymous/`object` type argument cannot be closed statically.

No invoker is emitted for the view. The view stays **fully functional** on the reflection dispatch
fallback — only the **AOT-clean** HTTP surface is missed. **No compilation error is raised.**

## Example that triggers VISTA0040

```csharp
using a2n.Vista.Authoring;

// ❌ anonymous/object row type — the generator cannot close ListAsync<TRow> at compile time
public partial class OrderView : View<object>
{
    public OrderView() { }

    protected override void Configure(IViewBuilder<object> builder)
        => builder.Named("Order")
                  .FromQuery<Order>(
                      db => db.Set<Order>(),
                      o => new { o.Id, o.CustomerName });   // anonymous projection
}
```

## How to fix

Give the view a **named** projected row type (and, for a writable view, a **named** write model) so the
generator can emit the reflection-free invoker:

```csharp
using a2n.Vista.Authoring;

public sealed record OrderRow(int Id, string CustomerName);

public partial class OrderView : View<OrderRow>   // ✅ named row type
{
    public OrderView() { }

    protected override void Configure(IViewBuilder<OrderRow> builder)
        => builder.Named("Order")
                  .From<Order>(o => new OrderRow(o.Id, o.CustomerName))
                  .Field(x => x.Id, f => f.PrimaryKey());
}
```

## When you can ignore it

It is safe to leave as-is: the view remains fully functional and its HTTP dispatch runs through the
runtime (reflection-based) path. You lose only the AOT-clean HTTP surface for that view. This is the
permanent, expected behavior for Style A (anonymous) views (D96) — those stay on reflection by design.
Severity is `Info` for this reason.

## Related

- [VISTA0041](VISTA0041.md) — Serialization guidance for a covered Style B view.
- [VISTA0003](VISTA0003.md) — Style B view projection cannot be analyzed statically.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D123/D124).
