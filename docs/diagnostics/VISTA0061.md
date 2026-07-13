# VISTA0061 — Style A anonymous read row stays on the reflection path (RUC by design)

| | |
|---|---|
| **ID** | `VISTA0061` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Info |
| **Introduced** | M9 — Source Generator Style A coverage (Decision Log D129 / D130) |

## Cause

A **Style A** view (`a2n.Vista.Authoring.ViewTemplate<TDbContext>` calling `AddView<TRow>(name, projection)`)
has an **anonymous** or `object` read row type (`TRow`). Its **read** serialization and **export** therefore
stay on the reflection path — permanently `[RequiresUnreferencedCode]` **by design** (D96 / D130).

This is the anonymous-type wall, and it does not move. A C# anonymous type has **no source-writable name**:
its metadata name (for example `<>f__AnonymousType0`) is not valid C# and is not stable across assemblies.
A source generator therefore **cannot** write any of the following for an anonymous row:

- `((AnonymousRow)row).Field` — no export accessor,
- `Expression<Func<AnonymousRow, T>>` — no member access,
- `JsonTypeInfo<AnonymousRow>` / `new AnonymousRow()` — no serialization metadata.

Because naming the type is impossible, the read-side artifacts cannot be generated for this view, and its
read path stays on the reflection fallback. **This is not a fixable warning** — it is the deliberate,
permanent AOT asymmetry of Style A (Style A anonymous read = RUC; typed Style B = AOT-clean).

Note this diagnostic concerns the **read** row only. If the view is **writable** via
`.WithCrud<TCrud, TEntity>()`, its write model (`TCrud`, always a **named** type) is unaffected and can
still be covered — the write body binds AOT-clean even though the read row is anonymous. That coverage is
reported by [VISTA0060](VISTA0060.md).

## Example that triggers VISTA0061

```csharp
using a2n.Vista.Authoring;

public sealed class CatalogTemplate : ViewTemplate<ShopDbContext>
{
    protected override void Configure(IViewTemplateBuilder<ShopDbContext> views)
        => views.AddView(
                    "orders",
                    // ❌ anonymous read row — unnameable in generated source
                    (db, sp) => db.Orders.Select(o => new { o.Id, o.Total }));
}
```

## How to get read-side coverage (optional)

If — and only if — you want the read-side artifacts (export accessors + read-DTO `JsonTypeInfo`) generated,
project into a **named** row type (a DTO or record). The view keeps its central-template authoring style:

```csharp
public sealed record OrderRow(int Id, decimal Total);

views.AddView<OrderRow>(                                     // ✅ named row → read-side coverage
        "orders",
        (db, sp) => db.Orders.Select(o => new OrderRow(o.Id, o.Total)));
```

This is a **choice**, not a fix: an anonymous read row is a valid, working Style A view. The anonymous
projection is exactly what makes Style A ergonomic, and keeping it is fully supported — it simply stays on
the reflection (RUC) path for read serialization and export.

## When you can ignore it

Almost always. The view is fully functional; only the AOT-clean **read** auto-generation is unavailable, by
design. Severity is `Info` for this reason. If your app is not trim/AOT-published, there is nothing to
consider here at all.

## Related

- [VISTA0060](VISTA0060.md) — Style A view covered by generated shape-driven artifacts.
- [VISTA0062](VISTA0062.md) — Style A AddView name is not a compile-time constant.
- [VISTA0063](VISTA0063.md) — Style A DTO member cannot be emitted reflection-free (falls back).
- [VISTA0040](VISTA0040.md) — Style B view cannot receive a generated HTTP dispatch invoker.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D129/D130); D96 (permanent
  AOT asymmetry of Style A).
