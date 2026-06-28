# VISTA0002 — Style B view needs a public parameterless constructor for accessor registration

| | |
|---|---|
| **ID** | `VISTA0002` |
| **Category** | `a2n.Vista.SourceGenerators` |
| **Severity** | Info |
| **Introduced** | M9 — Source Generator, Phase 1 (Decision Log D117) |

## Cause

A `partial` typed **Style B** view (deriving from `a2n.Vista.Authoring.View<TQuery>` or
`View<TQuery, TCrud>`) has **no public parameterless constructor**.

The generated module initializer registers a view's accessors keyed by the view's runtime `Name`. To read
`Name`, the initializer instantiates the view via its public parameterless constructor at module load. A
view without such a constructor cannot be instantiated this way in Phase 1, so it is **skipped** (no
accessor map or registration is emitted) and this informational diagnostic is reported.

This is a Phase 1 limitation, not a defect in your code — the view still works through the reflection
fallback on the export path (coexistence).

## Example that triggers VISTA0002

```csharp
using a2n.Vista.Authoring;

public partial class CustomerListView : View<CustomerListItem>
{
    // ❌ only a parameterized constructor → no public parameterless ctor → VISTA0002
    public CustomerListView(ICustomerPolicy policy) => _policy = policy;

    private readonly ICustomerPolicy _policy;

    protected override void Configure(IViewBuilder<CustomerListItem> builder)
        => builder.Named("CustomerList")
                  .From<Customer>(c => new CustomerListItem { Id = c.Id, Name = c.Name })
                  .Field(x => x.Id, f => f.PrimaryKey());
}
```

## How to fix

Add (or keep) a public parameterless constructor so the generator can register the view's accessors:

```csharp
public partial class CustomerListView : View<CustomerListItem>
{
    public CustomerListView() { }                 // ✅ enables generated accessor registration
    public CustomerListView(ICustomerPolicy policy) : this() => _policy = policy;

    private readonly ICustomerPolicy _policy;

    protected override void Configure(IViewBuilder<CustomerListItem> builder)
        => builder.Named("CustomerList")
                  .From<Customer>(c => new CustomerListItem { Id = c.Id, Name = c.Name })
                  .Field(x => x.Id, f => f.PrimaryKey());
}
```

A class with no declared constructors at all also satisfies this, because the C# compiler supplies an
implicit public parameterless constructor.

## When you can ignore it

It is safe to leave as-is: the view continues to export correctly via the reflection fallback. You lose
only the AOT-clean generated accessor path for that view. Severity is `Info` for this reason.

## Related

- [VISTA0001](VISTA0001.md) — Style B view must be declared `partial`.
- `docs/spec/03-source-generator.md` — source generator design intent (D71–D81).
