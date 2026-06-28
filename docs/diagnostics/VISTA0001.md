# VISTA0001 — Style B view must be `partial`

| | |
|---|---|
| **ID** | `VISTA0001` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Error |
| **Introduced** | M9 — Source Generator, Phase 1 (Decision Log D117) |

## Cause

A typed **Style B** view — a class deriving from `a2n.Vista.Authoring.View<TQuery>` or
`a2n.Vista.Authoring.View<TQuery, TCrud>` — was found that is **not** declared `partial`.

The Vista source generator emits a companion type per view to hold the generated field accessors and a
module initializer that registers them. It requires the view to be `partial` so the generated code can
sit alongside your declaration. A non-partial view is **skipped** (no accessors are generated for it), and
this error is reported.

## Example that triggers VISTA0001

```csharp
using a2n.Vista.Authoring;

public class CustomerListView : View<CustomerListItem> // ❌ not partial → VISTA0001
{
    protected override void Configure(IViewBuilder<CustomerListItem> builder)
        => builder.Named("CustomerList")
                  .From<Customer>(c => new CustomerListItem { Id = c.Id, Name = c.Name })
                  .Field(x => x.Id, f => f.PrimaryKey());
}
```

## How to fix

Add the `partial` modifier to the view class:

```csharp
public partial class CustomerListView : View<CustomerListItem> // ✅
{
    protected override void Configure(IViewBuilder<CustomerListItem> builder)
        => builder.Named("CustomerList")
                  .From<Customer>(c => new CustomerListItem { Id = c.Id, Name = c.Name })
                  .Field(x => x.Id, f => f.PrimaryKey());
}
```

## When you can ignore it

You generally should not. If you intentionally do not want generated accessors for a view, the view keeps
working through the reflection fallback (coexistence) — but it will not get the AOT-clean export path.
Making the class `partial` is the recommended resolution.

## Related

- [VISTA0002](VISTA0002.md) — Style B view needs a public parameterless constructor for accessor registration.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81).
