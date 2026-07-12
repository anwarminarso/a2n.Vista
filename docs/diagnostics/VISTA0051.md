# VISTA0051 — Style B view DTO member cannot be emitted reflection-free

| | |
|---|---|
| **ID** | `VISTA0051` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Warning |
| **Introduced** | M9 — Source Generator per-view JsonTypeInfo (Decision Log D125 / D126) |

## Cause

A typed **Style B** view (deriving from `a2n.Vista.Authoring.View<TQuery>` or `View<TQuery, TCrud>`) is a
serialization **candidate**, but one of its DTO members has a shape the generator **cannot emit
reflection-free** via `System.Text.Json.Serialization.Metadata.JsonMetadataServices`. Examples include a
member requiring a bespoke/custom converter, an unsupported polymorphic shape, or an unresolved generic.

Because correctness (byte-for-byte parity with the reflection serializer) beats coverage, the generator
does **not** emit a best-effort context that could drift from the wire. The view is classified **not
covered**: no per-view `JsonTypeInfo` is generated, and the view falls back to the developer
`App_Json_Context` / reflection resolver — exactly as before this phase.

The view stays **fully functional**. Only the AOT-clean per-view serialization auto-generation is missed.
**No compilation error is raised.**

## Example that triggers VISTA0051

```csharp
using a2n.Vista.Authoring;

// A DTO member whose shape needs a custom converter the generator will not synthesize.
public sealed record OrderRow(int Id, string CustomerName, IShippingAddress Address);
//                                                         ^^^^^^^^^^^^^^^^^^^^^^^^^^^
// ❌ interface/polymorphic member — not emittable reflection-free

public partial class OrderView : View<OrderRow>
{
    public OrderView() { }

    protected override void Configure(IViewBuilder<OrderRow> builder)
        => builder.Named("Order")
                  .From<Order>(o => new OrderRow(o.Id, o.CustomerName, o.ShippingAddress))
                  .Field(x => x.Id, f => f.PrimaryKey());
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

Once every member is emittable, the view becomes covered and reports [VISTA0050](VISTA0050.md) instead.

## When you can ignore it

It is safe to leave as-is: the view remains fully functional and (de)serializes through the developer
`App_Json_Context` (see [VISTA0041](VISTA0041.md)) or the reflection fallback resolver. You lose only the
AOT-clean per-view serialization for that view. Severity is `Warning` because the view still works via the
fallback — it is never an error.

## Related

- [VISTA0050](VISTA0050.md) — Per-view JsonTypeInfo generated for a covered Style B view.
- [VISTA0041](VISTA0041.md) — Serialization guidance for authoring a developer context.
- [VISTA0033](VISTA0033.md) — Writable view MapWritable chain cannot be analyzed statically (fallback).
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D123–D126).
