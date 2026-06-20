# Spec 03 — Source Generator (Pillar 3)

> Status: **DESIGN INTENT (frozen; not a final contract until Pillar 3 is built)**
> Date: 2026-06-20 (rev: reconciliation notes against Pillar 1)
> Scope: Roslyn incremental source generator `a2n.Vista.SourceGenerators` that removes reflection from the hot path and makes Vista Native-AOT-clean. It produces: an `IConfiguredView` implementation, compile-time `ViewMetadata`, the member-access & accessor map, `CompiledView` (projection/`MapWritable`), `JsonSerializerContext`, auto-registration, and the OpenAPI document. **Not** included: query semantics (Spec 02), the adapter contract (Spec 04), HTTP endpoints (Spec 05). This document defines **what is generated & its contract**, not the line-by-line Roslyn implementation details.
>
> **Reconciliation note (2026-06-20).** Pillar 3 is **not yet implemented**. Pillar 1 uses the
> **reflection** path with `[RequiresUnreferencedCode]` (see `FilterCompiler`, `EfViewExecutor`,
> `IViewRegistry.Register<TView>`, `RegisterTemplate`). When the generator is built, align the contract names
> with the actual code (Spec 01 §13.1): the List result is `ViewListResult<TRow>` (not `ViewQueryResult`);
> there is no separate `IViewWriter` (writes live in `IViewExecutor`); `FilterOrigin` is a 3-value enum used as a
> parameter; registration is via `IViewRegistry.Add(ViewMetadata)` + (for executable Style B)
> `IViewExecutionPlan` through `IVistaBuilder.Register<TView>(plan)` (DR5/DR8).

---

## 1. Goal

Pillar 3 = "AOT-First, not AOT-as-afterthought" (ROADMAP). The generator must satisfy the AOT constraints of Spec 01 §9 for **all hot paths**:

1. **No `Activator.CreateInstance`** — construct `TQuery`/`TCrud` through generated code.
2. **No `PropertyInfo.GetValue/SetValue`** — read/write fields through compile-time accessor delegates.
3. **No non-typed `JsonSerializer.(De)serialize(.., Type)`** — every DTO has a generated `JsonTypeInfo`.
4. **No runtime `Expression.Property(..)`** — member-access for filter/sort is generated from the `TQuery` shape.
5. **No reflection scan** in registration — `Register<TView>()` is called from generated code (not `RegisterAssembly`, Spec 01 §5.3/§9).

The generator is an **artifact provider** consumed by the engine (Spec 02), the exporter (Spec 01 §11), and the adapter (Spec 04 §9).

## 2. Position in the Architecture

```text
            COMPILE TIME (Roslyn)                         RUNTIME
┌──────────────────────────────────────┐     ┌───────────────────────────────┐
│ a2n.Vista.SourceGenerators           │     │ engine (Spec 02) ─┐           │
│  input:  View<>/ViewTemplate<> syntax │     │ exporter (§11) ───┼─ consume  │
│  output: partial IConfiguredView      │ ──► │ adapter (Spec 04)─┘ CompiledView│
│          ViewMetadata builder         │     │ AspNetCore (Spec 05) endpoints │
│          member-access + accessors     │     └───────────────────────────────┘
│          CompiledView (proj/MapWritable)│
│          JsonSerializerContext          │
│          module initializer (register)  │
│          OpenAPI document model          │
└──────────────────────────────────────┘
```

Package constraint (ROADMAP D48): the generator is a **Roslyn analyzer** (`netstandard2.0`, `IIncrementalGenerator`), **without referencing other Vista projects**. It recognizes Vista types by **fully-qualified name** (symbol string matching), not by assembly reference. The generated code is placed in the consumer assembly (the one that defines the View), not in the generator.

| Document | Relationship |
|---|---|
| `01-view.md` | Input: `View<TQuery>`, `View<TQuery,TCrud>`, `ViewTemplate<TDbContext>`, fluent DSL. Output: `IConfiguredView`, `ViewMetadata`, `ExportColumnAccessors`. |
| `02-filter-and-query.md` | Consumer: member-access (§14 Spec 02), `CompiledView`, two-count engine. |
| `04-adapter-contract.md` | Consumer: `JsonTypeInfo` for `ToResponse` (§9 Spec 04). |
| `05-aspnetcore-mapping.md` | Consumer: auto-registration + OpenAPI document model. |

## 3. Terminology

| Term | Meaning |
|---|---|
| **Shape-driven** | Generation from the **type symbol** (`TQuery`/`TCrud`/`TEntity`) — only needs the property list. Robust, always succeeds. |
| **DSL-recognized** | Generation from **body analysis** of `Configure`/`AddView` (fluent chain). Best-effort; a diagnostic is emitted when not recognized. |
| **`CompiledView`** | Bundle of generated delegates for one view: source query, projection, member-access, accessors, `MapWritable` assignment. |
| **Member-access** | `Expression<Func<TQuery,TProp>>` / `Func<TQuery,object?>` per field — the replacement for runtime `Expression.Property`. |
| **Accessor** | `Func<object,object?>` (read) & `Action<object,object?>` (write/mask) per field — the replacement for `PropertyInfo`. |
| **RUC** | `[RequiresUnreferencedCode]` — a path that is not AOT-clean (e.g. anonymous serialization in Style A). |

## 4. Non-Goals

- `IIncrementalGenerator` implementation details (pipeline nodes, caching) — touched on in §12, not normative line-by-line.
- Generation of HTTP write-endpoint code (routing) → Spec 05; the generator only provides the `MapWritable` assignment & metadata.
- TypeScript client generation → Spec 06 (a separate tool that consumes `ViewMetadata`/OpenAPI).
- Full analysis of arbitrarily complex projections → §17 (the depth of DSL analysis is an open question).

## 5. Input — What the Generator Scans

The generator collects **View candidates** from syntax (fast, incremental):

1. **Style B (class-per-view):** a non-abstract class that inherits `a2n.Vista.View<TQuery>` or `View<TQuery,TCrud>` (Spec 01 §5.1). Must be `partial` (§7, VISTA0001).
2. **Style A (central template):** a class that inherits `ViewTemplate<TDbContext>` (Spec 01 §5.5); each `views.AddView("name", query)` call inside `Configure` = one view.

For each candidate, the generator extracts (a combination of shape-driven + DSL-recognized, §6):

- `TQuery` (typed or anonymous), `TCrud`, `TEntity`/`TSource`.
- The projection lambda (`From<TSource>(x => new TQuery{..})` / `AddView` body).
- Field configuration (`.Field(x => x.F, f => f.PrimaryKey().Hidden()...)`).
- `MapWritable(w => w.P, e => e.P)` (Write facet).
- `Named(...)`/`AddView` name, `MaxPageSize`, `MaxExportRows`.

## 6. Generation Model — Shape-driven + DSL-recognized

### 6.1 Shape-driven (always, from the type symbol)

Only needs the type's property list → **always** succeeds, **always** AOT-clean:

| Artifact | From | Used by |
|---|---|---|
| Member-access per `TQuery` property | `TQuery` properties | filter/sort engine (Spec 02 §9, §11) |
| Accessor `Func<object,object?>` per `TQuery` property | `TQuery` properties | export (Spec 01 §11), mask (§13) |
| `JsonTypeInfo`/`JsonSerializerContext` for `TQuery`,`TCrud` | typed types | response/request serialization (Spec 04 §9) |
| Constructor for `TQuery`/`TCrud` (no `Activator`) | typed types | materialization, model-bind |

> Anonymous `TQuery` (Style A): member-access & accessors **can still** be generated (the anonymous shape is visible in the compilation), but `JsonSerializerContext` **cannot** (an anonymous type has no name to reference) → serialization falls back to STJ reflection (RUC). Consistent with Spec 01 §4.5/§9.

### 6.2 DSL-recognized (from body analysis)

Requires understanding the fluent chain → best-effort, with a diagnostic when the pattern is not recognized:

| Artifact | Recognized pattern | Fallback when not recognized |
|---|---|---|
| Projection delegate `TSource→TQuery` | member-init `new TQuery { A = s.A, B = s.B }` / anonymous `new { s.A }` | VISTA0003 (warning) + interpreted projection (RUC) |
| `MapWritable` assignment `TEntity.P = TCrud.P` | `MapWritable(w => w.P, e => e.P)` with simple member selectors | VISTA0012 (warning) + interpreted assignment (RUC) |
| Default field config | a literal chain `.PrimaryKey()/.Hidden()/.Operators(..)/.Searchable(false)` | evaluated at runtime startup (cold path; still AOT-safe) |

Principle: **shape is always compile-time; configuration may be runtime-startup (cold); only the hot path must use generated delegates.** Reading a `MemberExpression` `x => x.F` at startup to build `ViewMetadata` is AOT-safe (it is not reflection-emit) — what is forbidden is `Compile()`-ing an expression at runtime & `PropertyInfo` on the hot path.

## 7. Output — Partial `IConfiguredView` & Registration

A Style B view must be `partial`; the generator completes `IConfiguredView` (Spec 01 §5.1):

```csharp
// written by the developer
public partial class CustomerListView : View<CustomerListItem, CustomerWriteDto>
{
    protected internal override void Configure(IViewBuilder<CustomerListItem, CustomerWriteDto> b) { /* ... */ }
}

// generated (illustrative)
partial class CustomerListView : IConfiguredView
{
    public string Name => "customers";
    public Type QueryType => typeof(CustomerListItem);
    public Type? CrudType => typeof(CustomerWriteDto);
    public void ConfigureCore(IViewBuilderCore builder) { /* metadata bootstrap */ }
}
```

**Auto-registration** via a generated module initializer (Spec 01 §5.3 "source-gen adds this automatically"):

```csharp
// generated per consumer assembly
internal static class VistaGeneratedRegistration
{
    [ModuleInitializer]
    internal static void Register() => VistaRegistry.AddGenerated(
        typeof(CustomerListView), /* CompiledView bundle */ CompiledViews.Customers, ...);
}
```

`AddVista(...)` consumes this generated registry; an explicit `Register<TView>()` remains valid (idempotent, deduplicated by Name). `RegisterAssembly` remains `[RequiresUnreferencedCode]` (the non-AOT path, Spec 01 §9).

## 8. Output — `CompiledView` Bundle

One `CompiledView` per view, stored into the Core contract consumed by the engine/exporter/adapter:

```csharp
namespace a2n.Vista;

// Compile-time delegate bundle. No reflection in its members.
public sealed class CompiledView
{
    public string Name { get; init; }
    public ViewMetadata Metadata { get; init; }

    // Source query factory (Spec 02 §5 step 6). object = erased IQueryable<TSource>.
    public Func<IServiceProvider, object> SourceQuery { get; init; }

    // Projection TSource→TQuery as an Expression (for EF translate, Spec 02 §5 step 8).
    public LambdaExpression Projection { get; init; }

    // Member-access per field: name → Expression<Func<TQuery,object?>> (filter/sort, Spec 02 §9/§11).
    public IReadOnlyDictionary<string, LambdaExpression> MemberAccess { get; init; }

    // Read accessor per field (export §11, mask §13).
    public ExportColumnAccessors Accessors { get; init; }

    // Mask mutator per field (Spec 01 §5.2/§13). null when there is no mask.
    public IReadOnlyDictionary<string, Action<object, IServiceProvider>>? Maskers { get; init; }

    // Write: assign TCrud→TEntity from MapWritable (Spec 01 §5.2). null when read-only.
    public Action<object /*crud*/, object /*entity*/>? ApplyWritable { get; init; }

    // Primary key accessor (Detail by-key & tiebreaker paging, Spec 02 §11).
    public Func<object, object>? KeySelector { get; init; }
}

public interface ICompiledViewStore
{
    bool TryGet(string viewName, out CompiledView view);
    IReadOnlyCollection<CompiledView> All { get; }
}
```

Notes:

- `Projection`/`MemberAccess` remain `LambdaExpression` (not pure delegates) because **EF Core needs the expression tree** to translate to SQL. The key point: these expressions are **built compile-time by the generator** (static node construction), **not** `Expression.Property(p, propertyInfo)` via runtime reflection. There is no `Compile()` on the hot path.
- `Accessors`/`Maskers`/`ApplyWritable`/`KeySelector` are **pure delegates** (in-memory, post-materialization) — no expression, no reflection.
- `SourceQuery` is erased to `object` at the Core boundary (in line with `IViewExecutor`, Spec 02 §6.3); the EF layer casts it to `IQueryable<TSource>`.

## 9. Output — JSON (System.Text.Json source-gen)

- Each typed `TQuery`/`TCrud` → `[JsonSerializable]` in a generated `JsonSerializerContext` per assembly → `JsonTypeInfo` is available for response serialization (Spec 04 §9) & `TCrud` deserialization (Spec 05 write path).
- STJ native (Spec 01 D5); Newtonsoft only in the separate `a2n.Vista.Newtonsoft` package (outside the AOT path).
- **Anonymous (Style A):** no `JsonTypeInfo` → STJ reflection serialization, marked RUC (Spec 01 §4.5). Diagnostic VISTA0030 (info) when building targets AOT (`PublishAot=true`).

## 10. Output — OpenAPI Document Model

- The generator produces a **neutral document model** (not `Microsoft.AspNetCore.OpenApi` — a `netstandard2.0` generator may not reference ASP.NET) from `ViewMetadata`: path per facet (Spec 01 §12.3), `TQuery`/`TCrud` schema, filter/sort parameters, error responses (Spec 02 §15).
- `a2n.Vista.AspNetCore` (Spec 05) consumes this model → registers it into the ASP.NET OpenAPI pipeline at compile time (no runtime scan).
- Anonymous view: the schema is derived from the anonymous shape (best-effort); RUC is not relevant for the document (build-time artifact).

## 11. Invariant Enforcement (compile-time)

The generator/analyzer enforces the Spec 01 invariants that can be checked statically:

1. **Typing invariant (Spec 01 §4.5/D38):** the Write facet needs a typed `TCrud`. Because `WithCrud<TCrud,TEntity>()`/`View<TQuery,TCrud>` requires a class type (not anonymous), this is **already enforced by the type system**. The generator adds a diagnostic if there is an attempt to write from an anonymous-only view (VISTA0031, error) — defense in depth.
2. **PrimaryKey for stable paging (Spec 02 §11/§17 #2):** a view without a `PrimaryKey()` field → VISTA0020 (warning in v1.0; candidate for error). Without a PK, `KeySelector`/tiebreaker is null → engine fallback + a runtime warning.
3. **`MapWritable` exhaustiveness (Spec 01 D25):** a `TCrud` field not covered by `MapWritable` → VISTA0010 (info, default ignore). `[VistaWritable(strict: true)]` on `TCrud` → an unmapped field becomes VISTA0011 (error).
4. **Unique view name:** a duplicate `Named`/`AddView` across consumer assemblies → VISTA0040 (error).
5. **Valid field selector:** `.Field(x => expr)` where `expr` is not a single property access on `TQuery` → VISTA0050 (error).

## 12. Incremental Pipeline (informative)

- `IIncrementalGenerator` with an **equatable** data model (value-based record) so Roslyn caching is effective — changing the body of one View does not regenerate the whole assembly.
- Stages: `ForAttributeWithMetadataName`/fast syntax predicate → symbol extraction → immutable model → emit. Avoid the global `Compilation` in hot nodes.
- Multi-target `net8.0;net9.0;net10.0` (Spec 01 D8): the generated code uses features available in the lowest TFM (e.g. `[ModuleInitializer]` has existed since net5). `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` are applied per path.
- Test: snapshot/golden-file with TUnit (Spec 01 D7) in a generator-test project (separate, referencing the generator as an analyzer).

## 13. Diagnostics Catalog

| ID | Severity | Condition | Developer action |
|---|---|---|---|
| `VISTA0001` | Error | Style B view is not `partial` | add `partial` |
| `VISTA0002` | Error | `Configure`/`AddView` not found / wrong signature | fix the override |
| `VISTA0003` | Warning | Projection cannot be analyzed statically | simplify to member-init, or accept RUC |
| `VISTA0010` | Info | `TCrud` field not covered by `MapWritable` (default ignore) | ignore or map |
| `VISTA0011` | Error | Strict mode: `TCrud` field not mapped | map or remove `strict` |
| `VISTA0012` | Warning | `MapWritable` selector is not a simple member | simplify; otherwise → interpreted assignment (RUC) |
| `VISTA0020` | Warning | View without `PrimaryKey()` (non-deterministic paging) | mark a PK |
| `VISTA0030` | Info | Anonymous view in an AOT build (`PublishAot`) | use a typed DTO for full AOT |
| `VISTA0031` | Error | Attempt at a Write facet on an anonymous projection | use a typed `WithCrud<TCrud,TEntity>` |
| `VISTA0040` | Error | Duplicate view name | rename |
| `VISTA0050` | Error | `.Field` selector is not a single property access | fix the selector |

Prefix `VISTA` + 4 digits; category `a2n.Vista.SourceGenerators`; all have a help-link to the docs.

## 14. AOT Constraints Guaranteed by the Generator

A summary of what the generator **guarantees is removed** from the hot path (satisfying Spec 01 §9):

| Anti-pattern | Replaced by the generator with |
|---|---|
| `Activator.CreateInstance(TQuery)` | a direct constructor in generated code |
| `PropertyInfo.GetValue/SetValue` | generated `Func/Action` accessors (`CompiledView.Accessors`) |
| runtime `Expression.Property(p, PropertyInfo)` | a member-access expression built at compile time |
| runtime `Expression.Lambda(..).Compile()` | projection/member-access as a static expression (EF translate, no compile) |
| `JsonSerializer.Serialize(obj, Type)` | a generated `JsonTypeInfo` |
| `Assembly.GetTypes()` registration scan | a generated module initializer + `Register<TView>()` |

What **remains** RUC (deliberately, Spec 01 §4.5): serialization & schema of an anonymous projection (Style A). Filter/sort/paging in Style A stays AOT-clean (shape-driven member-access, §6.1).

## 15. Decision Log (continued from Spec 04 D70)

| # | Decision | Status | Notes |
|---|---|---|---|
| D71 | `IIncrementalGenerator`, `netstandard2.0`, without a Vista project reference; recognize types via FQN. | **Decided** | ROADMAP D48. |
| D72 | Generation model is **shape-driven + DSL-recognized**. Shape is always compile-time; field configuration may be runtime-startup (cold); the hot path must use generated delegates. | **Decided** | §6. |
| D73 | Style B view must be `partial`; the generator completes `IConfiguredView` + `ConfigureCore`. | **Decided** | §7, VISTA0001. Spec 01 §5.1. |
| D74 | The `CompiledView` bundle (`SourceQuery`/`Projection`/`MemberAccess`/`Accessors`/`Maskers`/`ApplyWritable`/`KeySelector`) is stored via `ICompiledViewStore`, consumed by the engine/exporter/adapter. | **Decided** | §8. |
| D75 | `Projection`/`MemberAccess` remain `LambdaExpression` (EF needs the tree) but are **built at compile time** — no runtime `Compile()`/`PropertyInfo`. In-memory accessors = pure delegates. | **Decided** | §8. The core of AOT + EF-translatable. |
| D76 | A `JsonSerializerContext` per typed DTO is generated; anonymous → STJ reflection + RUC (VISTA0030). | **Decided** | §9. Spec 01 D5/§4.5. |
| D77 | Auto-registration via a generated `[ModuleInitializer]`; an explicit `Register<TView>()` remains valid (deduplicated by Name). `RegisterAssembly` remains RUC. | **Decided** | §7. Spec 01 §5.3. |
| D78 | OpenAPI = a generated neutral document model (the generator does not reference ASP.NET); consumed by `a2n.Vista.AspNetCore`. | **Decided** | §10. |
| D79 | `MapWritable` → a generated assignment via member-selector analysis; if not extractable → VISTA0012 + interpreted fallback (RUC). | **Decided** | §6.2, §13. Spec 01 D25. |
| D80 | A view without `PrimaryKey()` → VISTA0020 (warning in v1.0). Candidate to be promoted to error. | **Decided (warning)** | §11, Spec 02 §17 #2. |
| D81 | Diagnostics prefix `VISTA####`, category `a2n.Vista.SourceGenerators`, with a help-link. | **Decided** | §13. |

## 16. Relationship with Previous Open Questions

- **Spec 02 §17 #2 (is PK mandatory?)** → addressed by D80: warning VISTA0020 in v1.0, candidate for error. The generator is where it is enforced.
- **Spec 01 §15 #4 (`MapWritable` exhaustiveness)** → D79 + VISTA0010/0011 (already Decided as D25; here is the mechanism).
- **Spec 01 §15 #2 (sparse `SelectFields`)** → §17 #2 below (accessor combinatorics).

## 17. Open Questions

1. **Depth of DSL projection analysis.** How far does the generator analyze a non-trivial projection (method call, conditional, nested `new`)? v1.0 candidate: support member-init & flat anonymous; the rest is VISTA0003 + RUC. A projection with navigation (`s.Category.Name`) needs to be supported (common in joins) — a clear rule is needed.
2. **Sparse `SelectFields` (Spec 01 §15 #2).** A per-field-combination accessor is combinatorial. Candidate: generate one full accessor map + a runtime projection that selects the subset (without SQL re-projection), or defer sparse-select to v1.x.
3. **Composite-PK `KeySelector`.** The shape of the `object` key for a PK > 1 column (tuple? array?) — aligned with the Detail endpoint (Spec 05) & tiebreaker (Spec 02 §11).
4. **Cross-assembly view discovery.** Per-assembly module initializer: how does `AddVista` in the main app discover views from a library assembly that also has an initializer? (A module initializer runs when the assembly is loaded — must ensure the referenced assembly is not trimmed before init.) Candidate: the main app generator emits an explicit reference, or document a manual `Register<TView>()` for cross-assembly views.
5. **OpenAPI for an anonymous view.** A schema from the anonymous shape is sufficient, but component naming (`#/components/schemas/...`) needs a stable name — derive it from the view name? (e.g. `vProductCategoryRow`).

## 18. Next / Forward References

- `05-aspnetcore-mapping.md` — consumes auto-registration (§7), the OpenAPI model (§10), `CompiledView.ApplyWritable` for the write endpoint, `KeySelector` for Detail by-key.
- `06-typescript-client.md` — consumes `ViewMetadata`/OpenAPI (§10) for DTO codegen + the TS filter API.
- `07-export.md` — consumes `ExportColumnAccessors` (§8) for streaming export (Spec 01 §11).
