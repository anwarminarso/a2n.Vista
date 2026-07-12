# VISTA0041 — Serialization guidance for a covered Style B view

| | |
|---|---|
| **ID** | `VISTA0041` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Info |
| **Introduced** | M9 — Source Generator HTTP surface (Decision Log D124) |

## Cause

A typed **Style B** view (deriving from `a2n.Vista.Authoring.View<TQuery>` or `View<TQuery, TCrud>`) is
**covered** by the generated HTTP dispatch surface. This informational diagnostic tells you exactly which
types to include in a developer-authored `JsonSerializerContext` so the view's HTTP (de)serialization is
**AOT-clean**.

A Roslyn source generator **cannot consume the output of another source generator**: all generators run
against the same input compilation, so the built-in System.Text.Json generator never sees a
`[JsonSerializable]` context that Vista's generator emits. Vista therefore cannot auto-generate a working
per-view serialization context. Instead, you author a `JsonSerializerContext` and register it with
`AddVistaJsonContext(...)`; Vista chains it into the serialization seam.

This diagnostic names the exact `[JsonSerializable]` types for the view so authoring the context is
mechanical:

- `TRow` (the projected row type)
- `ViewListResult<TRow>`
- `PagedResult<TRow>`
- `TCrud` (only when the view is writable, with a named write model)

The **build succeeds** whether or not a context is supplied. Until you register one, the view
(de)serializes through the reflection fallback resolver (which is trim/AOT-unsafe). Registering the
context makes the view's serialization AOT-clean.

## Example

For a covered view `OrderView : View<OrderRow, OrderCrud>`, VISTA0041 names `OrderRow`,
`ViewListResult<OrderRow>`, `PagedResult<OrderRow>`, and `OrderCrud`. Author and register a context:

```csharp
using System.Text.Json.Serialization;
using a2n.Vista.Contracts;   // ViewListResult<T>, PagedResult<T>

[JsonSerializable(typeof(OrderRow))]
[JsonSerializable(typeof(ViewListResult<OrderRow>))]
[JsonSerializable(typeof(PagedResult<OrderRow>))]
[JsonSerializable(typeof(OrderCrud))]
public partial class AppJsonContext : JsonSerializerContext { }
```

```csharp
// during endpoint registration
app.MapVistaViews()
   .AddVistaJsonContext(AppJsonContext.Default);   // ✅ chains the context into the serialization seam
```

For a **read-only** view (`View<TQuery>`), omit the `TCrud` line — the diagnostic will not list it.

## When you can ignore it

It is safe to leave as-is: the view remains fully functional and (de)serializes through the reflection
fallback resolver. You lose only the AOT-clean serialization path for that view until a context is
registered. Severity is `Info` because this is guidance, not an error.

## Related

- [VISTA0040](VISTA0040.md) — Style B view cannot receive a generated HTTP dispatch invoker.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D123/D124).
