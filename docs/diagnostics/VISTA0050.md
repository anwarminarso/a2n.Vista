# VISTA0050 — Per-view JsonTypeInfo generated for a covered Style B view

| | |
|---|---|
| **ID** | `VISTA0050` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Info |
| **Introduced** | M9 — Source Generator per-view JsonTypeInfo (Decision Log D125 / D126) |

## Cause

A typed **Style B** view (deriving from `a2n.Vista.Authoring.View<TQuery>` or `View<TQuery, TCrud>`) is
**covered** for per-view serialization: every member of its DTOs has an emittable shape, so the Vista
source generator emits a reflection-free per-view serialization context for it.

Unlike [VISTA0041](VISTA0041.md) — which asks you to author a `JsonSerializerContext` by hand — this phase
generates the context **for you**. Because a Roslyn source generator cannot consume the output of another
source generator, Vista does not use the `[JsonSerializable]` attribute route; instead it builds each
`JsonTypeInfo` **by hand via** `System.Text.Json.Serialization.Metadata.JsonMetadataServices` (the same
metadata factory the built-in System.Text.Json generator emits into under the hood). The generated context
is a `file sealed` class implementing `IJsonTypeInfoResolver`, auto-chained into the serialization seam
**ahead of the reflection fallback**.

This informational diagnostic names the view and the exact **Serializable_DTO_Set** now served by the
generated context:

- `TRow` (the projected row type)
- `ViewListResult<TRow>`
- `PagedResult<TRow>`
- `TCrud` (only when the view is writable, with a named write model)

Because the generated context resolves these types AOT-clean, the corresponding developer
`App_Json_Context` entry for the view is **optional** — a still-registered context keeps working (redundant,
not forbidden), and removing it changes nothing on the wire.

## Example

For a covered view `OrderView : View<OrderRow, OrderCrud>`, VISTA0050 names `OrderRow`,
`ViewListResult<OrderRow>`, `PagedResult<OrderRow>`, and `OrderCrud`. No developer context is required:

```csharp
using a2n.Vista.Authoring;

public sealed record OrderRow(int Id, string CustomerName, OrderStatus Status);
public sealed record OrderCrud(string CustomerName, OrderStatus Status);

public partial class OrderView : View<OrderRow, OrderCrud>   // ✅ named row + write types
{
    public OrderView() { }

    protected override void Configure(IViewBuilder<OrderRow, OrderCrud> builder)
        => builder.Named("Order")
                  .From<Order>(o => new OrderRow(o.Id, o.CustomerName, o.Status))
                  .Field(x => x.Id, f => f.PrimaryKey());
}
```

```csharp
// during endpoint registration — no AddVistaJsonContext(...) needed for OrderView's DTOs
app.MapVistaViews();
```

For a **read-only** view (`View<TQuery>`), the diagnostic omits the `TCrud` line.

## What it means for you

- The view's HTTP (de)serialization is AOT-clean **without** a developer `App_Json_Context`.
- Any `App_Json_Context` entry you previously authored for this view's DTOs (see
  [VISTA0041](VISTA0041.md)) can be removed; it becomes redundant.
- The generated path is guaranteed **byte-for-byte identical** to the reflection serializer under the same
  `JsonSerializerOptions`, so removing the developer context introduces no wire drift.

## When you can ignore it

This is confirmation, not a problem. It is safe to leave any redundant `App_Json_Context` entry in place —
the generated context takes precedence in the resolver chain and both produce identical JSON. Severity is
`Info` for this reason.

## Related

- [VISTA0051](VISTA0051.md) — Style B view DTO member cannot be emitted reflection-free (falls back).
- [VISTA0041](VISTA0041.md) — Serialization guidance for authoring a developer context (now optional).
- [VISTA0040](VISTA0040.md) — Style B view cannot receive a generated HTTP dispatch invoker.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D123–D126).
