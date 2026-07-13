# VISTA0063 — Style A DTO member cannot be emitted reflection-free

| | |
|---|---|
| **ID** | `VISTA0063` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Warning |
| **Introduced** | M9 — Source Generator Style A coverage (Decision Log D129 / D130) |

## Cause

A **Style A** view (`a2n.Vista.Authoring.ViewTemplate<TDbContext>` calling `AddView<TRow>(name, projection)`,
optionally continued by `.WithCrud<TCrud, TEntity>()`) has a candidate DTO — a named `TRow` on the read
side, or a `TCrud` on the write side — with a member whose shape the generator **cannot emit
reflection-free** via `System.Text.Json.Serialization.Metadata.JsonMetadataServices`. Examples include a
member requiring a bespoke/custom converter, an unsupported polymorphic shape, or an unresolved generic.

Because correctness (byte-for-byte parity with the reflection serializer) beats coverage, the generator does
**not** emit a best-effort context that could drift from the wire. The offending DTO is classified **not
covered** for serialization: no `JsonTypeInfo` is generated for it, and the view falls back to the developer
`App_Json_Context` / reflection resolver for that DTO — exactly as before this phase.

The view stays **fully functional**. A named-`TRow` view still receives its **export accessor map** (only
the serialization `JsonTypeInfo` for the offending DTO is skipped). **No compilation error is raised.**

This is the Style A counterpart of [VISTA0051](VISTA0051.md) (the typed Style B "non-emittable member"
diagnostic); both share the same severity and semantics.

## Example that triggers VISTA0063

```csharp
using a2n.Vista.Authoring;

// A DTO member whose shape needs a custom converter the generator will not synthesize.
public sealed record OrderRow(int Id, string CustomerName, IShippingAddress Address);
//                                                         ^^^^^^^^^^^^^^^^^^^^^^^^^^^
// ❌ interface/polymorphic member — not emittable reflection-free

public sealed class CatalogTemplate : ViewTemplate<ShopDbContext>
{
    protected override void Configure(IViewTemplateBuilder<ShopDbContext> views)
        => views.AddView<OrderRow>(
                    "orders",
                    (db, sp) => db.Orders.Select(o => new OrderRow(o.Id, o.CustomerName, o.ShippingAddress)));
}
```

The diagnostic message names the offending type/member so you can find it quickly.

## How to fix (to get the generated context)

Simplify the offending DTO member to an **emittable shape** so the generator can build its `JsonTypeInfo`:

- Use a **named POCO/record** with emittable members instead of an interface or abstract/polymorphic type.
- Replace a member that needs a custom converter with a shape System.Text.Json supports natively (BCL
  scalars, `string`, nullable value types, enums, `byte[]`, collections of an emittable element, the Vista
  `ViewListResult<TRow>` / `PagedResult<TRow>` envelopes, and single-level nested emittable POCOs).

```csharp
public sealed record ShippingAddress(string Line1, string City, string PostalCode);   // ✅ named, emittable
public sealed record OrderRow(int Id, string CustomerName, ShippingAddress Address);
```

Once every member is emittable, the DTO becomes covered and the view reports [VISTA0060](VISTA0060.md)
instead.

## When you can ignore it

It is safe to leave as-is: the view remains fully functional and (de)serializes the offending DTO through
the developer `App_Json_Context` or the reflection fallback resolver. You lose only the AOT-clean per-DTO
serialization. Severity is `Warning` because the view still works via the fallback — it is never an error.

## Related

- [VISTA0060](VISTA0060.md) — Style A view covered by generated shape-driven artifacts.
- [VISTA0061](VISTA0061.md) — Style A anonymous read row stays on the reflection path (RUC by design).
- [VISTA0062](VISTA0062.md) — Style A AddView name is not a compile-time constant.
- [VISTA0051](VISTA0051.md) — Style B view DTO member cannot be emitted reflection-free (the counterpart).
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D129/D130).
