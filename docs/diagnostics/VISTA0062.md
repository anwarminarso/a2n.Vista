# VISTA0062 — Style A AddView name is not a compile-time constant

| | |
|---|---|
| **ID** | `VISTA0062` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Info |
| **Introduced** | M9 — Source Generator Style A coverage (Decision Log D129 / D130) |

## Cause

A **Style A** `AddView<TRow>(name, projection)` call site (inside an
`a2n.Vista.Authoring.ViewTemplate<TDbContext>`) supplies a `name` argument that is **not a compile-time
constant** string.

The Vista source generator keys every Style A artifact **statically** by the view name — the constant is
lifted directly from the `AddView` call and used as the registration key in the `[ModuleInitializer]`:

```csharp
[ModuleInitializer]
static void Register() => ViewAccessorRegistry.Register("customers", Map);   // key lifted from AddView
```

When the `name` argument is not a compile-time constant, there is no stable key to register the artifact
under. The runtime name is unknowable at compile time, and registering under a guessed/wrong key would
silently miss at runtime. So the generator emits **nothing** for that call site, and the view stays on the
reflection path.

No artifact is emitted for the call site. The view stays **fully functional** on the reflection fallback —
only the AOT-clean auto-generation is missed. **No compilation error is raised.**

## Example that triggers VISTA0062

```csharp
using a2n.Vista.Authoring;

public sealed class CatalogTemplate : ViewTemplate<ShopDbContext>
{
    private string _name = ResolveName();   // not a compile-time constant

    protected override void Configure(IViewTemplateBuilder<ShopDbContext> views)
        => views.AddView<CustomerRow>(
                    _name,                                  // ❌ non-constant view name
                    (db, sp) => db.Customers.Select(c => new CustomerRow(c.Id, c.Name)));
}
```

## How to fix

Use a compile-time **constant string literal** (or a `const`) for the `AddView` name so the generator can
key the artifacts statically:

```csharp
private const string ViewName = "customers";   // ✅ compile-time constant

protected override void Configure(IViewTemplateBuilder<ShopDbContext> views)
    => views.AddView<CustomerRow>(
                ViewName,                                   // ✅ or the literal "customers"
                (db, sp) => db.Customers.Select(c => new CustomerRow(c.Id, c.Name)));
```

Once the name is constant, the view becomes eligible for generated artifacts and reports
[VISTA0060](VISTA0060.md) instead (subject to the row/DTO shape being nameable and emittable).

## When you can ignore it

It is safe to leave as-is: the view remains fully functional and resolves through the reflection fallback.
You lose only the AOT-clean auto-generation for that view. Severity is `Info` for this reason.

## Related

- [VISTA0060](VISTA0060.md) — Style A view covered by generated shape-driven artifacts.
- [VISTA0061](VISTA0061.md) — Style A anonymous read row stays on the reflection path (RUC by design).
- [VISTA0063](VISTA0063.md) — Style A DTO member cannot be emitted reflection-free (falls back).
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81, D129/D130).
