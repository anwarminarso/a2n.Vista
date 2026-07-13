# VISTA0060 — Style A view covered by generated shape-driven artifacts

| | |
|---|---|
| **ID** | `VISTA0060` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Info |
| **Introduced** | M9 — Source Generator Style A coverage (Decision Log D129 / D130) |

## Cause

A **Style A** view — an `a2n.Vista.Authoring.ViewTemplate<TDbContext>` calling
`AddView<TRow>(name, projection)` (optionally continued by `.WithCrud<TCrud, TEntity>()`) — is **covered**
for shape-driven generation. The Vista source generator emits one or more reflection-free artifacts for the
view and registers them into the existing Core stores, so the covered artifacts resolve from generated code
instead of the reflection fallback.

This informational diagnostic names the view and the **exact set** of artifacts generated for it:

- **Export accessors** — when `TRow` is a **named** type (registered into `ViewAccessorRegistry`).
- **Read-DTO `JsonTypeInfo`** for `TRow`, `ViewListResult<TRow>`, and `PagedResult<TRow>` — when `TRow` is
  named and all its DTO members are emittable (registered into `GeneratedJsonContextStore`).
- **Write-model `TCrud` `JsonTypeInfo`** — when the view is writable via `WithCrud<TCrud, TEntity>()` and
  `TCrud` is emittable (registered into `GeneratedJsonContextStore`). `TCrud` is always a named type, so
  this artifact is generatable **even when the read row is anonymous**.

Anything not listed stays on the reflection path by design (see [VISTA0061](VISTA0061.md),
[VISTA0062](VISTA0062.md), and [VISTA0063](VISTA0063.md)).

## Example

For a covered read-only view with a named row, VISTA0060 names the export accessors and the read-DTO set:

```csharp
using a2n.Vista.Authoring;

public sealed record CustomerRow(int Id, string Name, string Country);

public sealed class CatalogTemplate : ViewTemplate<ShopDbContext>
{
    protected override void Configure(IViewTemplateBuilder<ShopDbContext> views)
        => views.AddView<CustomerRow>(                       // ✅ named row + constant name
                    "customers",
                    (db, sp) => db.Customers.Select(c => new CustomerRow(c.Id, c.Name, c.Country)));
}
```

For a **writable** view, VISTA0060 also names the `TCrud` `JsonTypeInfo`. When the read row is **anonymous**
but the view is writable, VISTA0060 names **only** the `TCrud` `JsonTypeInfo`, and the read row is reported
separately by [VISTA0061](VISTA0061.md):

```csharp
views.AddView(
        "orders",
        (db, sp) => db.Orders.Select(o => new { o.Id, o.Total }))   // anonymous read row → VISTA0061
    .WithCrud<OrderWrite, Order>();                                 // named TCrud → VISTA0060 (write only)
```

## What it means for you

- The named artifacts are resolved from generated, reflection-free code, so those paths are AOT-clean.
- The generated output is guaranteed **byte-for-byte identical** to the reflection serializer (and
  value-for-value identical to the reflection export read) under the same `JsonSerializerOptions`, so there
  is no wire or export drift.
- Any developer `App_Json_Context` entry for a DTO now served by the generated context is optional; it
  remains valid (redundant, not forbidden) and produces identical JSON.

## When you can ignore it

This is confirmation, not a problem. Severity is `Info` for this reason.

## Related

- [VISTA0061](VISTA0061.md) — Style A anonymous read row stays on the reflection path (RUC by design).
- [VISTA0062](VISTA0062.md) — Style A AddView name is not a compile-time constant.
- [VISTA0063](VISTA0063.md) — Style A DTO member cannot be emitted reflection-free (falls back).
- [VISTA0050](VISTA0050.md) — Per-view JsonTypeInfo generated for a covered Style B view.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D129/D130).
