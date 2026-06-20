# Spec 01 — View (Pillar 1)

> Status: **IMPLEMENTED (Pillar 1)** — reconciled with the code
> Date: 2026-06-20 (rev: synchronized to the `pilar-1-core` implementation; source of truth = code)
> Scope: the `View` concept in `a2n.Vista.Core`. Not included: UI adapters (Pillar 2), source generator (Pillar 3), full EF Core integration. This spec focuses on the **public contract** that forms the foundation for all other projects.
>
> **Reconciliation note (2026-06-20).** Pillar 1 has been implemented (`.kiro/specs/pilar-1-core`,
> Tasks 1–13 complete). Where this document and the code differ, **the code prevails**.
> Adjusted sections are marked inline; important differences that surfaced during implementation
> are recorded in Decision Log §13 (DR1–DR10). Some API members sketched here are still
> *forward-looking* (write path, validator/interceptor, adapter `Accept`-negotiation) and are marked
> as the next milestone.

---

## 1. Purpose

Define the **View** as the core declarative unit of `a2n.Vista`. A View must be:

1. **Explicit** — no auto-expose. The developer must declare each View.
2. **Type-safe on the write path** — write operations (create/edit) are always strongly-typed `TCrud` + whitelist, not `IQueryable<dynamic>`. The read path (grid/detail) may use anonymous projection for ergonomics (see 4.5).
3. **Single source** for: metadata, endpoint contract, filter contract, UI binding, TS client codegen.
4. **Secure-by-default** — declarative field whitelist, CRUD opt-in, mandatory authorization.
5. **AOT-clean** — the public contract must not force runtime reflection; all "hot" paths are served by the source generator (Pillar 3).

## 2. Terminology

| Term | Meaning |
|---------|------|
| **View** | Declarative unit: LINQ projection + metadata + (optional) CRUD target. Replaces `QueryTemplate` from DynData. |
| **TQuery** | The DTO type produced by the projection (sent to the client as a response item). |
| **TCrud** | The DTO type for write operations (create/update). Distinct from `TQuery` to separate the read & write contracts. |
| **Source** | The EF Core entity (or another `IQueryable<T>`) that the data originates from. |
| **CrudTarget** | The target entity of write operations. May be the same as `Source`, a subset, or absent (read-only View). |
| **ViewBuilder** | Fluent API for configuring a View at registration time. |
| **IViewRegistry** | The container holding all registered Views; resolved by the endpoint mapper, adapters, and codegen. |
| **ViewMetadata** | A declarative snapshot of the View after the builder completes — the input for the source generator & TS client. |
| **Adapter** | A per-grid component that translates client requests into a neutral `ViewQueryRequest` and responses into a grid format. |
| **ViewTemplate** | A centralized authoring class (DynData style): registers many Views via `AddView(...)` in one place. See 4.5. |
| **Facet** | One of the three capabilities of a View: **List** (read many), **Detail** (read one by-key), **Write** (create/edit/delete). See 4.6. |
| **Anonymous view** | A View with an anonymous projection (`select new { ... }`) — read-only unless attached to a typed Write facet. |

## 3. Non-Goals (for this spec)

- Implementation of any concrete adapter.
- Implementation of EF Core query translation.
- Implementation of the source generator.
- Definition of the response format (JSON shape) sent to the client — that is Pillar 2's domain.
- Authentication (who the user is) — Vista only delegates to ASP.NET Core identity.

## 4. Core Concepts

### 4.1 View = projection + contract

A View **always** has four things:

1. **Source query**: `Func<TServices, IQueryable<TSource>>` — how to obtain the base `IQueryable`.
2. **Projection**: `Expression<Func<TSource, TQuery>>` — the final shape sent to the client.
3. **Filter contract**: the list of `TQuery` fields the client may filter on + the allowed operators.
4. **Metadata**: view name, route, description, hard limits, auth requirement.

CRUD is optional:

- If `CrudTarget<TEntity>` is set: this View can create/update/delete.
- `TCrud` is the write DTO whose fields are an **explicit whitelist** onto `TEntity`.
- Without `CrudTarget`, the View is **read-only** and write endpoints will not be generated.

### 4.2 Raw DbSet = View without projection

There is no "expose every DbSet" API. But if a developer needs direct access to an entity without a projection, they still declare a View — with an identity projection (`x => x`). This preserves *one path, one rule*: all data leaving Vista goes through a View.

### 4.3 Read DTO vs Write DTO

`TQuery` and `TCrud` **must be separated** at the type level. The reasons:

- The fields safe to display ≠ the fields safe for the client to change.
- DynData mixed the two → mass assignment leaked.
- This separation is also clean for the TS client (`MyViewQueryDto` vs `MyViewCrudDto`).

A read-only View uses the base class `View<TQuery>` (without `TCrud`) — not `Unit`/`NoCrud` (see 5.1). For writes: `View<TQuery, TCrud>` (Style B) or `WithCrud<TCrud, TEntity>` (Style A).

### 4.4 Searchable vs Filterable

DynData mixed two distinct concepts into a single request. Vista still **separates** them conceptually, but (revision D42) **defaults to allow** — not opt-in as in the early spec version. The key point: in Vista the security boundary is the **projection**, not the table. Fields present in the projection have already been curated by the developer (no password hashes, etc.), so the default of "every projection field can be filtered/sorted/searched" is far safer than DynData, which exposed all entity columns.

| Concept | Vista default | Override |
|--------|---------------|----------|
| **Filter** (per-field, explicit operator) | All projection fields filterable, default operators per type | `.Field(x => x.F, f => f.Operators(...))` or `f.Filterable(false)` |
| **Sort** | All projection fields sortable | `.Field(x => x.F, f => f.Sortable(false))` |
| **Search** (global, OR-`Contains` over string fields) | All **string** projection fields participate | `.Field(x => x.F, f => f.Searchable(false))` |

Separating Filter vs Search still matters:

1. Filter operators (`Equals`, `In`, `Between`, etc.) cannot be "reached through the search box" — global search is only `Contains` over string fields.
2. A field can opt out of search but remain filterable (e.g. a PII field shown masked: `.Field(x => x.Email, f => f.Searchable(false))`).
3. Only **string** fields participate in global search; numeric/date never do (except via explicit filter).

### 4.5 Two Authoring Styles

Vista is an **evolution** of DynData: DynData's *view-first* ergonomics are retained as the **first authoring style**, alongside the strongly-typed class-per-view style. Both produce the same `ViewMetadata` (5.4) and go through the same validation/auth/limit pipeline.

**Style A — Central Template (anonymous, DynData-like).** A single `ViewTemplate<TDbContext>` class registers many Views via `AddView(...)` with an inline **anonymous** projection. No DTO class is needed; view columns are easy to adjust. This is the style DynData users miss.

**Style B — Class-per-View (typed).** A single `View<TQuery>` / `View<TQuery, TCrud>` class per view with an explicit DTO. More verbose, but fully AOT-clean.

#### Typing rules (security invariant)

> **An anonymous projection may only serve read facets (List/Detail). The Write facet REQUIRES a typed `TCrud` + whitelist.**

Consequence: a View whose only facets are anonymous reads is **read-only**. There is no path from an anonymous projection to a write operation — that closes mass-assignment by design. To add writes, attach a typed Write facet (see 4.6 + 5.5 `WithCrud`). This formulation refines the original idea "anonymous ⇒ the whole view is read-only" into a **per-facet** rule: reads may be anonymous, writes must be typed.

| | Style A (central template) | Style B (class-per-view) |
|---|---|---|
| Read projection | anonymous | typed `TQuery` |
| Create a DTO class? | no (read); yes (if there are writes) | yes |
| Write facet (CRUD) | only via `WithCrud<TCrud, TEntity>` (typed) | `View<TQuery, TCrud>` |
| AOT-clean | no (RUC, reflection serialization) | yes |
| Suited for | back-office, many views, fast iteration, DynData migration | complex views, Native AOT target |

#### Commitment: both styles are permanent (D96)

Style A **and** Style B are **permanent** first-class features — there is no plan to deprecate Style A.
Selection guidance (use-case):

| Application topology | Recommendation | Reason |
|---|---|---|
| Monolith (many views) | **Style A** | Views centralized in one template; easy to find & maintain without hunting through separate files. |
| Modular monolith (views in sub-projects) | **Style B** | Views live in sub-projects as classes; the main project attaches their assemblies. Requires **cross-assembly view discovery** (D97). |
| Microservices | A or B | Free choice — follow each team's preference per service. |

> **The AOT asymmetry is permanent & explicit (D96).** Because Style A projections are anonymous,
> their serialization is **never** fully Native-AOT-clean (no by-name `JsonSerializerContext`) →
> it stays `[RequiresUnreferencedCode]` forever (D40). **Style A filter/sort/paging stays AOT-clean**
> (member-access shape-driven); only *serialization* is not. Developers targeting full Native AOT
> must use Style B. This is not a temporary shortcoming — it is a guaranteed design consequence.

### 4.6 Facet Model

A single **View** is one named *resource* (e.g. `"vProductCategory"`) that has up to three **facets**:

| Facet | Cardinality | Typing | Endpoint (see 12.3) |
|-------|-------------|--------|------------------------|
| **List** | many, paged | anonymous / typed | `POST /api/views/{name}/query` |
| **Detail** | one, by-key | anonymous / typed | `GET /api/views/{name}/{key}` |
| **Write** | one (create/update/delete) | **typed only** | `POST/PUT/DELETE /api/views/{name}` |

Rules:

1. **List is mandatory.** Every View has at least a List facet (its read projection).
2. **Detail is optional.** If not declared, Detail uses the List projection filtered by primary key. The PK is determined via field metadata (`PrimaryKey()`). A Detail facet with its own projection (more columns than the grid) is available in Style B; in Style A v0.x, Detail = List by-key.
3. **Write is optional & typed.** Without a Write facet → the resource is read-only. Write never uses an anonymous projection.
4. **PK is the bridge between facets.** List row → button → Detail/Write all use the same PK. Therefore the PK must be present in the List projection (it may be `Hidden()`, like `ProductId` in DynData).
5. **Auth per-facet.** Defaults to View-level auth; can be overridden per facet (e.g. read `CanReadProducts`, write `CanEditProducts`).

Mapping to the UI "surfaces": **List = grid**, **Detail = display form**, **Write = create/edit form**.

## 5. API Surface (Public Contract)

> Note: the signatures below are the **spec target**, not yet implemented. Type names are normative, bodies are illustrative.

### 5.1 Main types

```csharp
namespace a2n.Vista;

// Non-generic marker for registry & polymorphism (does not use View<object>).
public interface IConfiguredView
{
    string Name { get; }
    Type QueryType { get; }
    Type? CrudType { get; }
    void ConfigureCore(IViewBuilderCore builder);
}

// Read-only View. The builder used does NOT have CrudOn / MapWritable.
public abstract class View<TQuery> : IConfiguredView
    where TQuery : class
{
    // Called by the registry at startup.
    protected internal abstract void Configure(IViewBuilder<TQuery> builder);
    // The IConfiguredView implementation is generated by the source generator (Pillar 3).
}

// View with CRUD. The builder has CrudOn and must be used for the write path.
public abstract class View<TQuery, TCrud> : IConfiguredView
    where TQuery : class
    where TCrud : class
{
    protected internal abstract void Configure(IViewBuilder<TQuery, TCrud> builder);
}
```

Notes:

- `View<TQuery>` is **not** a subclass of `View<TQuery, TCrud>`. Separating the builder types prevents a developer from calling `CrudOn(...)` on a read-only view.
- The `NoCrud` marker (previous version) is removed. Read-only is handled through a separate base class, not a generic dummy parameter.
- Registration & polymorphism go through the non-generic `IConfiguredView` — there is no `View<object>` (see 5.3).

### 5.2 ViewBuilder (Style B)

Two interfaces are explicitly separated. The read-only view (`IViewBuilder<TQuery>`) does not have `CrudOn`, so a compile error appears if used incorrectly. The non-generic `IViewBuilderCore` part exists so that `IConfiguredView.ConfigureCore(...)` (see 5.1) can be codegen'd.

In line with Style A (§5.5): there is **no** `Route()`/`RequireAuthorization()` (global route §5.6, centralized auth §5.6), and filter/sort/search are **default-allow** for all projection fields — customize via `.Field(...)` (§4.4). `IFieldBuilder<TProp>` is shared with §5.5.

```csharp
// Non-generic part for source-gen interop (see 5.1 IConfiguredView).
public interface IViewBuilderCore
{
    IViewBuilderCore Named(string viewName);
    IViewBuilderCore MaxPageSize(int rows);
    IViewBuilderCore MaxExportRows(int rows);
}

// Read-only view builder.
public interface IViewBuilder<TQuery> : IViewBuilderCore
    where TQuery : class
{
    new IViewBuilder<TQuery> Named(string viewName);

    // Source query — REQUIRED, one of these.
    IViewBuilder<TQuery> From<TSource>(
        Expression<Func<TSource, TQuery>> projection)
        where TSource : class;

    IViewBuilder<TQuery> FromQuery<TSource>(
        Func<IServiceProvider, IQueryable<TSource>> source,
        Expression<Func<TSource, TQuery>> projection)
        where TSource : class;

    // Per-field configuration (optional). Default: filterable + sortable +
    // (string) searchable, auto label. Override/opt-out via IFieldBuilder<TProp>
    // (see 5.5) — including .Scopable(...) for client contextual filters (§5.6).
    IViewBuilder<TQuery> Field<TProp>(
        Expression<Func<TQuery, TProp>> field,
        Action<IFieldBuilder<TProp>> configure);

    new IViewBuilder<TQuery> MaxPageSize(int rows);
    new IViewBuilder<TQuery> MaxExportRows(int rows);

    // Row-level security — pre-projection (recommended). TSource = source entity.
    // Soft-delete & tenant-filter generally live on TSource.
    IViewBuilder<TQuery> WithRowFilter<TSource>(
        Func<IServiceProvider, Expression<Func<TSource, bool>>> filterFactory)
        where TSource : class;

    // Row-level security — post-projection (special case, e.g. computed field).
    IViewBuilder<TQuery> WithProjectedRowFilter(
        Func<IServiceProvider, Expression<Func<TQuery, bool>>> filterFactory);

    // Field masking — predicate (bool) + transformer (TProp -> TProp).
    IViewBuilder<TQuery> MaskField<TProp>(
        Expression<Func<TQuery, TProp>> field,
        Func<IServiceProvider, bool> shouldMask,
        Func<TProp, TProp> masker);
}

// View with CRUD. Inherits the read-side knobs from the read-only builder + write path.
public interface IViewBuilder<TQuery, TCrud> : IViewBuilder<TQuery>
    where TQuery : class
    where TCrud : class
{
    // CRUD — must be called at least once on View<TQuery, TCrud>.
    ICrudBuilder<TQuery, TCrud, TEntity> CrudOn<TEntity>(
        Expression<Func<TEntity, TQuery>>? projectionForRead = null)
        where TEntity : class;
}

public interface ICrudBuilder<TQuery, TCrud, TEntity>
    where TQuery : class
    where TCrud : class
    where TEntity : class
{
    // Write whitelist — REQUIRED at least once. No field is default-mapped.
    ICrudBuilder<TQuery, TCrud, TEntity> MapWritable<TProp>(
        Expression<Func<TCrud, TProp>> from,
        Expression<Func<TEntity, TProp>> to);

    ICrudBuilder<TQuery, TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField);

    // FORWARD-LOOKING (not in Pillar 1 code yet; see DR4): validator & interceptor.
    // ICrudBuilder<TQuery, TCrud, TEntity> WithValidator<TValidator>()
    //     where TValidator : IViewCrudValidator<TCrud>;
    // ICrudBuilder<TQuery, TCrud, TEntity> WithInterceptor<TInterceptor>()
    //     where TInterceptor : IViewCrudInterceptor<TCrud, TEntity>;

    ICrudBuilder<TQuery, TCrud, TEntity> AllowBulk(bool allow = true);
}
```

Deliberate consequences:

- **Default-allow**: filter/sort/search are active for all projection fields; restrict via `.Field(x => x.F, f => f.Filterable(false)/.Searchable(false)/.Operators(...))` (§4.4, D42).
- **No auth/route in the builder**: global route (§5.6), auth via `IViewAuthorizer` (§5.6) — D43/D44.
- `WithRowFilter<TSource>` is the **primary** path (pre-projection). Pushes down to SQL naturally; soft-delete/tenant on the entity, not the DTO.
- `WithProjectedRowFilter` is for special cases only.
- `MaskField` needs three arguments: field selector, predicate, transformer (without a transformer, masking has no semantics). **A field that is `MaskField`'d defaults to `Filterable(false)` (D95)** — preventing *probing* of the original value (e.g. binary-search `StartsWith`) that would expose the masked value via filter responses. Explicit opt-in `Filterable(true)` if genuinely needed (ideally restrict to `Equals` only).
- `WithConcurrencyToken` (HTTP detail in Spec 05) & `WithInterceptor` (audit forecast v1.x).

### 5.3 Registry

`IViewRegistry` (in `a2n.Vista.Core`) stores `ViewMetadata` keyed by `Name`. Implementation (DR1):

```csharp
namespace a2n.Vista.Ports;

public interface IViewRegistry
{
    // Primary metadata sink. Called by authoring (Style A/B) & the source generator (Pillar 3)
    // after ViewMetadata is built. Duplicate name → throw at startup (R1.3).
    void Add(ViewMetadata view);

    // Reflection registration path (Style B by type). AOT-unsafe → RequiresUnreferencedCode.
    // The source generator emits an equivalent Add(...) on the AOT-clean path.
    [RequiresUnreferencedCode("View registration introspects the view type at runtime.")]
    void Register<TView>() where TView : class;

    // Miss → null (not throw). The endpoint maps null to 404 (R1.1).
    // Note: refines the early sketch that used a non-null Get.
    ViewMetadata? Get(string name);

    IReadOnlyCollection<ViewMetadata> All { get; }
}
```

Differences from the early sketch (already applied in the code): `Add(ViewMetadata)` is the primary sink;
`Get` returns **nullable**; there is **no** `Register(Type)` nor `RegisterAssembly` on the Core
surface — the registration sugar (`RegisterTemplate`, `Register<TView>`, `Register<TView>(plan)`)
lives on the DI builder (`IVistaBuilder`, see below).

DI is split into **two doors** (DR2): view/execution (EF) wiring in `a2n.Vista.EntityFrameworkCore`,
HTTP auth/route in `a2n.Vista.AspNetCore`. The two meet through Core ports (`IViewRegistry`,
`IViewExecutor`, `IViewScope`) and do **not** reference each other (R11.3, D48).

```csharp
// 1) EF door (view registration + execution plan). IVistaBuilder.
builder.Services.AddVista(vista =>
{
    vista.RouteRoot("/api/views");                       // route root embedded into ViewMetadata.Route
    vista.RegisterTemplate<AppViews, AppDbContext>();    // Style A — explicit TDbContext (two type params)
    vista.Register<CustomerListView>();                  // Style B — metadata-only (see note)
    // vista.Register<CustomerListView>(plan);           // Style B + execution plan (executable)
});

// 2) AspNetCore door (HTTP route root + single auth door). IVistaEndpointBuilder.
builder.Services.AddVistaEndpoints(v =>
{
    v.RouteRoot("/api/views");                           // live endpoint route root
    v.UseAuthorizer<AppViewAuthorizer>();                // single auth door (§5.6). Without it → allow + warning.
});

app.MapVistaViews();              // generic map of all views (resolve by name at request time)
app.MapView("customers");         // map a single view by NAME (not MapView<TView>(); see §5.6)
```

> **Pillar 1 implementation note.** `RegisterTemplate<TTemplate, TDbContext>` (Style A) produces
> metadata **and** an EF execution plan (executable). `Register<TView>()` (Style B) is currently
> **metadata-only** — the view is discoverable but its execution throws until a plan is supplied via
> `Register<TView>(IViewExecutionPlan)` or the source generator (Pillar 3). See DR5.

### 5.4 ViewMetadata (output)

```csharp
public sealed record ViewMetadata(
    string Name,
    string Route,
    Type QueryType,
    Type? CrudType,
    Type? CrudEntityType,
    IReadOnlyList<FieldMetadata> Fields,
    AuthorizationRequirement? Authorization,  // null = use the central authorizer (§5.6); per-view override is rare
    HardLimits Limits,
    bool IsReadOnly);

public sealed record FieldMetadata(
    string Name,
    string Label,            // auto from Name ("ProductName" → "Product Name"); override via .Field(..., f => f.Label(...))
    Type ClrType,
    bool IsFilterable,       // default true (all projection fields)
    bool IsSortable,         // default true
    bool IsSearchable,       // default true for string fields
    bool IsScopable,         // default false; contextual/lookup key from the client (§5.6)
    bool IsHidden,           // default false; hidden = not sent/displayed (e.g. technical PK)
    bool IsWritable,
    bool IsMaskable,
    FilterOperator AllowedOperators);
```

`ViewMetadata` is the **primary input** for:

- The source generator (Pillar 3) — codegen of endpoints, expression builders, OpenAPI doc.
- `IViewAdapter<TRequest, TResponse>` (Pillar 2) — translate client requests.
- `a2n.Vista.Client.TypeScript` — codegen of DTOs + filter contract in TS.

### 5.5 Central Template API (Style A)

```csharp
namespace a2n.Vista;

// DynData-like centralized authoring. TDbContext is the IQueryable source.
public abstract class ViewTemplate<TDbContext>
    where TDbContext : DbContext
{
    protected internal abstract void Configure(IViewTemplateBuilder<TDbContext> views);
}

public interface IViewTemplateBuilder<TDbContext>
    where TDbContext : DbContext
{
    // Read-only anonymous view. TRow is inferred by the compiler from the lambda body
    // (may be an anonymous type) — no explicit DTO needed.
    IReadViewBuilder<TRow> AddView<TRow>(
        string name,
        Func<TDbContext, IServiceProvider, IQueryable<TRow>> query)
        where TRow : class;
}

// Read facet builder. The field selector remains strongly-typed even when TRow is anonymous,
// because the lambda is evaluated in the same scope as AddView.
//
// Note: there is NO Route()/RequireAuthorization() here — routes are global (§5.6)
// and auth is centralized (§5.6). Filter/sort/search are active for ALL fields
// by default; customize via .Field(...).
public interface IReadViewBuilder<TRow>
    where TRow : class
{
    IReadViewBuilder<TRow> MaxPageSize(int rows);
    IReadViewBuilder<TRow> MaxExportRows(int rows);

    // Per-field configuration (optional). Each field defaults to: filterable + sortable
    // + (if string) searchable, label auto from the name. Use .Field(...) to override.
    IReadViewBuilder<TRow> Field<TProp>(
        Expression<Func<TRow, TProp>> field,
        Action<IFieldBuilder<TProp>> configure);

    // Pre-projection row-level security (see 5.2). For server-trusted scope
    // across views, use IViewAuthorizer.ShapeQuery (§5.6).
    IReadViewBuilder<TRow> WithRowFilter<TSource>(
        Func<IServiceProvider, Expression<Func<TSource, bool>>> filterFactory)
        where TSource : class;

    // Bridge to the Write facet — REQUIRED typed. Turns the resource into read+write.
    // The only CRUD door from the central-template style; does not accept
    // anonymous types (invariant 4.5).
    ICrudFacetBuilder<TCrud, TEntity> WithCrud<TCrud, TEntity>()
        where TCrud : class
        where TEntity : class;
}

// Per-field configuration. All optional; the defaults are already safe/correct.
public interface IFieldBuilder<TProp>
{
    IFieldBuilder<TProp> PrimaryKey();
    IFieldBuilder<TProp> Hidden();                        // not sent/displayed
    IFieldBuilder<TProp> Label(string label);             // override the auto label
    IFieldBuilder<TProp> Format(string formatString);

    // Opt-out / customize defaults (everything defaults to true):
    IFieldBuilder<TProp> Filterable(bool allowed = true);
    IFieldBuilder<TProp> Sortable(bool allowed = true);
    IFieldBuilder<TProp> Searchable(bool allowed = true);   // only affects string fields
    IFieldBuilder<TProp> Operators(FilterOperator allowed); // restrict operators (implies Filterable)

    // Allow the field to be a contextual/lookup key from the CLIENT (default false, opt-in).
    // Client filter scoping (the DynData externalFilter equivalent) may only target
    // Scopable fields — separate from UI Filterable (§5.6, D47).
    IFieldBuilder<TProp> Scopable(bool allowed = true);
}

// Same semantics as ICrudBuilder<TQuery, TCrud, TEntity> (5.2), without TQuery
// because the read facet in Style A is served by an anonymous TRow.
public interface ICrudFacetBuilder<TCrud, TEntity>
    where TCrud : class
    where TEntity : class
{
    ICrudFacetBuilder<TCrud, TEntity> MapWritable<TProp>(
        Expression<Func<TCrud, TProp>> from,
        Expression<Func<TEntity, TProp>> to);
    ICrudFacetBuilder<TCrud, TEntity> WithConcurrencyToken<TToken>(
        Expression<Func<TEntity, TToken>> tokenField);
    // FORWARD-LOOKING (not in Pillar 1 code yet; see DR4):
    // ICrudFacetBuilder<TCrud, TEntity> WithValidator<TValidator>()
    //     where TValidator : IViewCrudValidator<TCrud>;
    ICrudFacetBuilder<TCrud, TEntity> AllowBulk(bool allow = true);
}
```

AOT note: Style A triggers `[RequiresUnreferencedCode]` on the registration & anonymous-type serialization path (see ROADMAP Pillar 3). For full Native AOT, use Style B. The Write facet in both styles stays AOT-clean because the `TCrud → TEntity` mapping is source-gen'd from `MapWritable`.

`ViewTemplate` registration in DI (see also 5.3 & 5.6) — two doors:

```csharp
// EF: view registration + execution plan
services.AddVista(vista =>
{
    vista.RouteRoot("/api/views");                           // global route (§5.6)
    vista.RegisterTemplate<NorthwindViews, NorthwindDbContext>(); // Style A (explicit TDbContext)
    // vista.Register<CustomerListView>(plan);               // Style B + plan (executable)
});

// AspNetCore: HTTP route root + single auth door
services.AddVistaEndpoints(v =>
{
    v.RouteRoot("/api/views");
    v.UseAuthorizer<NorthwindViewAuthorizer>();              // without it → allow + warning
});
```

### 5.6 Authorization & Routing (cross-style)

Applies to both Style A and Style B. Replaces per-view `Route()` + `RequireAuthorization()` (Decision Log D43/D44).

#### Global routing

```csharp
services.AddVistaEndpoints(v => v.RouteRoot("/api/views"));  // default "/api/views" (HTTP layer)
```

Each view's route is derived from `{root}/{viewName}`. **Verb-to-facet in Pillar 1** (see 12.3):
`GET {root}/{viewName}` (List, paging/sort from the query string), `GET {root}/{viewName}/{key}` (Detail),
`POST {root}/{viewName}` (Create), `PUT {root}/{viewName}/{key}` (Update),
`DELETE {root}/{viewName}/{key}` (Delete). There is no per-view `Route()`; `viewName` comes from
`AddView("...")` / `Named("...")`.

> **Reconciliation note.** Pillar 1 maps List to **`GET {root}/{viewName}`** (query string),
> not `POST {root}/{viewName}/query`. The `POST .../query` form (body filter + response shape
> via `Accept`) is the **Pillar 2 adapter form** that layers on top of this route without changing it
> (DR3). `MapVistaViews()` uses one generic route per verb that resolves the view by name at
> request time; `MapView(string viewName)` maps a single view (not `MapView<TView>()` — that needs
> compile-time type→name resolution from the Pillar 3 source generator).

#### Authorization — a single door (`IViewAuthorizer`)

Replaces per-view auth. One implementation, registered once, becomes the gate for **all** views & facets — DynData's `IDynDataAPIAuth` style.

```csharp
public enum ViewFacet { List, Detail, Export, Create, Update, Delete }

public sealed record ViewAuthContext(
    ClaimsPrincipal User,
    string ViewName,
    ViewFacet Facet,
    HttpContext Http,
    IServiceProvider Services);

public interface IViewAuthorizer
{
    // Allow/deny gate per (view, facet, user). Called on every request.
    ValueTask<bool> IsAllowedAsync(ViewAuthContext ctx);

    // The IDynDataAPIAuth.ApplyRequest equivalent: inject server-trusted row/scope
    // filters (tenant, ownership) — centralized, not from the client.
    // This is the trusted "contextual filter" path (see externalFilter reference).
    void ShapeQuery(ViewAuthContext ctx, IViewScope scope);
}

public interface IViewScope
{
    // AND-ed into the query, pushed down to SQL. TSource = the view's source entity.
    void AddRowFilter<TSource>(Expression<Func<TSource, bool>> filter) where TSource : class;
}
```

Registration & default semantics:

```csharp
services.AddVistaEndpoints(v => v.UseAuthorizer<AppViewAuthorizer>());
```

> **Location.** `UseAuthorizer<T>` lives on `IVistaEndpointBuilder` (`AddVistaEndpoints`, the
> AspNetCore package), **not** on `AddVista` (the EF package). The authorizer is registered with a
> **scoped** lifetime so it can depend on request-scoped services (current user/tenant, scoped `DbContext`).

| Condition | Behavior |
|---------|----------|
| `UseAuthorizer<T>` registered | `T` is the sole gate. Anything not allowed by `IsAllowedAsync` → rejected (403). |
| No authorizer **in Development** | **Allow-all** + startup warning (frictionless dev). |
| No authorizer **in non-Development** (Production/Staging/UAT/env unset) | **Fail-closed: startup fails** with an actionable message, **unless** the explicit opt-in `AllowAnonymousAccess()`. |

> **Auth posture (revision D43 → D94).** The two-level model is retained: a *switch* (authorizer present/absent)
> + a *policy* (handler). What changed: "no authorizer" **no longer means silent allow-all in
> production**. Organization-neutral rationale: operators are often not the code authors and may lack
> source access, so a security *omission* must fail-safe. "The road being open" is now an **explicit**
> decision (`AllowAnonymousAccess()`) — one reviewed line, not the result of forgetting. Forgetting to
> register in non-Dev → the app fails to start (caught at deploy, not leaked silently). See D94 & the
> Operations/Observability doc (authorizer status exposed via a health check). ASP.NET treats an unset
> env = `Production` → the safe direction.

**Type locations (D48):** `IViewAuthorizer`, `ViewAuthContext`, and `ViewFacet` live in **`a2n.Vista.AspNetCore`** (HTTP-bound — `ViewAuthContext` carries `HttpContext`). `IViewScope` lives in **`a2n.Vista.Core`** (neutral). Flow: AspNetCore calls `IsAllowedAsync`/`ShapeQuery`, builds an `IViewScope`, then hands it to `IViewExecutor` (Core/EF). This keeps Core free of HTTP & EF dependencies.

#### Contextual filter from the client (lookup / drilldown) — `Scopable`

DynData's `externalFilter` (see the DataTables reference) is used for lookup modals & drilldown from the client. In Vista, filter scoping **from the client** may only touch fields declared `Scopable` — **separate** from UI `Filterable`:

```csharp
.Field(x => x.CategoryId, f => f.Hidden().Scopable())  // may become a client lookup key
```

- `Scopable` **defaults to false** (opt-in) — unlike `Filterable`, which is default-allow. Lookup is a sensitive path, so it must be declared explicitly.
- The adapter maps the client's contextual filter → a `FilterLeaf` validated as `field ∈ Scopable` (not `Filterable`). A violation → 400.
- **Server-trusted** scope (tenant, ownership) still goes through `IViewAuthorizer.ShapeQuery` — it does not need `Scopable` and cannot be bypassed by the client.

## 6. Hello World End-to-End

```csharp
// 1. Entity (EF Core, owned by the application)
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

// 2. Query DTO (sent to the client)
public class CustomerListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// 3. Crud DTO (received from the client)
public class CustomerWriteDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

// 4. View definition
public class CustomerListView : View<CustomerListItem, CustomerWriteDto>
{
    protected internal override void Configure(
        IViewBuilder<CustomerListItem, CustomerWriteDto> b)
    {
        b.Named("customers")
         .From<Customer>(c => new CustomerListItem
         {
             Id = c.Id,
             Name = c.Name,
             CreatedAt = c.CreatedAt
         })
         // Filter/sort/search are active for ALL projection fields by default.
         // Just override what you need:
         .Field(x => x.Id,        f => f.Hidden())                  // technical PK, hide from the UI
         .Field(x => x.CreatedAt, f => f.Operators(FilterOperator.Range))
         .MaxPageSize(200)
         .MaxExportRows(10_000)
         // Global route ({root}/customers) & auth via the central authorizer — not set here.
         // Row filter on TSource (Customer), not TQuery — soft-delete lives on the entity.
         .WithRowFilter<Customer>(_ => c => !c.IsDeleted)
         .CrudOn<Customer>()
              .MapWritable(w => w.Name,  e => e.Name)
              .MapWritable(w => w.Email, e => e.Email)
              .WithConcurrencyToken(e => e.RowVersion); // assume this was added
    }
}

// 5. Bootstrap — two DI doors
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(/* ... */);

// EF: view registration + execution plan
builder.Services.AddVista(v =>
{
    v.RouteRoot("/api/views");                 // global route (§5.6)
    v.Register<CustomerListView>(plan);        // Style B needs an execution plan to be executable (DR5)
});                                            // (source-gen emits this automatically in Pillar 3)

// AspNetCore: route + single auth door
builder.Services.AddVistaEndpoints(v =>
{
    v.RouteRoot("/api/views");
    v.UseAuthorizer<AppViewAuthorizer>();      // single auth door (§5.6)
});

var app = builder.Build();
app.UseVistaExceptionHandling();   // RFC 7807 error mapping (Spec 05 §9)
app.MapVistaViews();
app.Run();
```

What does **not** appear in Hello World but is deliberate:

- `CustomerListItem` has no `Email` field → it will never be sent to the client. Mass assignment to `Email` is only possible via `CustomerWriteDto.Email`, and only on the CRUD endpoint that is already authorized.
- The `IsDeleted` field is not in `CustomerListItem` and is not `MapWritable`'d → it cannot be set by the client.
- Explicit `Filterable` per field → the client cannot filter `Email` even though it exists on the entity.

## 6A. Example: `vProductCategory` (central-template style)

A direct equivalent of DynData's `NorthwindQueryTemplate.vProductCategory`, ported to Vista Style A. Read-only (anonymous projection), search/filter/sort opt-in, mandatory auth.

```csharp
public class NorthwindViews : ViewTemplate<NorthwindDbContext>
{
    protected internal override void Configure(IViewTemplateBuilder<NorthwindDbContext> views)
    {
        views.AddView("vProductCategory", (db, sp) =>
                from p in db.Products
                join c in db.Categories on p.CategoryId equals c.CategoryId
                join s in db.Suppliers  on p.SupplierId equals s.SupplierId
                select new
                {
                    p.ProductId,
                    p.CategoryId,
                    c.CategoryName,
                    p.ProductName,
                    p.UnitPrice,
                    p.UnitsInStock,
                    p.SupplierId,
                    SupplierName    = s.CompanyName,
                    SupplierContact = s.ContactName
                })
            // Default: ALL fields filter+sort+search, auto label, route {root}/vProductCategory,
            // auth via the central authorizer. Just mark the special ones:
            .Field(x => x.ProductId,  f => f.PrimaryKey().Hidden())   // PK, hide
            .Field(x => x.CategoryId, f => f.Hidden())
            .Field(x => x.SupplierId, f => f.Hidden());
        // No Write facet → read-only (List + Detail by ProductId).
    }
}
```

Differences from DynData (`AddQuery("vProductCategory", typeof(Product), ...)`):

- **No `typeof(Product)`** → no CRUD here → no mass-assignment. Stays read-only.
- **Filter/sort/search active by default** for all projection fields (like DynData), but limited to the columns that are **actually projected** — not all entity columns. Per-field opt-out is available (`f.Searchable(false)`).
- **Centralized auth** — no auth attribute on the view; the gate lives in `IViewAuthorizer` (§5.6).
- Field metadata via the fluent `.Field(x => x.ProductId, ...)` is strongly-typed (auto label), not DynData's string callback.

### 6A.1 Adding CRUD (rising to a typed Write facet)

If the same resource needs create/edit, attach a typed Write facet via `WithCrud` — the grid projection stays anonymous, writes go through a DTO + whitelist:

```csharp
views.AddView("vProductCategory", (db, sp) => /* ... same anonymous projection ... */)
    .Field(x => x.ProductId, f => f.PrimaryKey().Hidden())
    // filter/sort/search active by default for all fields; override as needed
    .WithCrud<ProductWriteDto, Product>()
        .MapWritable(w => w.ProductName,  e => e.ProductName)
        .MapWritable(w => w.UnitPrice,    e => e.UnitPrice)
        .MapWritable(w => w.UnitsInStock, e => e.UnitsInStock)
        .MapWritable(w => w.CategoryId,   e => e.CategoryId)
        .MapWritable(w => w.SupplierId,   e => e.SupplierId);
// Global route ({root}/vProductCategory). Centralized auth: IViewAuthorizer sees the
// ViewFacet (List/Detail vs Create/Update/Delete) → can apply different read vs write policy (§5.6).

public class ProductWriteDto      // strongly typed — required for writes
{
    public string ProductName { get; set; } = "";
    public decimal? UnitPrice { get; set; }
    public short? UnitsInStock { get; set; }
    public int CategoryId { get; set; }
    public int SupplierId { get; set; }
}
```

These are the three surfaces from the design discussion within one resource: **List (grid, anonymous)**, **Detail (display form, anonymous)**, **Write (create/edit, typed)** — connected via `ProductId`.

## 7. Default Security Rules

| Rule | Default |
|--------|---------|
| Fields in the response | Only those present in `TQuery`. |
| Filterable fields | **All projection fields** (default allow). Opt-out: `.Field(x => x.F, f => f.Filterable(false))`. The safe boundary = the projection contents. **Exception (D95): a `MaskField`'d field defaults to `Filterable(false)`** — see the masking note below. |
| Sortable fields | **All projection fields** (default allow). Opt-out: `f.Sortable(false)`. |
| Fields in global search | **All string projection fields** (default allow). Opt-out: `f.Searchable(false)`. Differs from the early spec version (opt-in); safe because the projection is already curated. |
| Writable fields | None. Must opt-in via `MapWritable(...)`. **(Stays default-deny — write ≠ read.)** |
| Write facet (CRUD) | Requires a typed `TCrud` + `MapWritable`. An anonymous projection is **never** a write contract. An anonymous-only View = read-only. |
| Authorization | **Central authorizer** (`UseAuthorizer<T>`). Registered → it is the sole gate. Not registered: **Development** → allow-all + warning; **non-Development** → **fail-closed startup** unless explicit `AllowAnonymousAccess()` (D94). See §5.6. |
| Bulk operation | Off by default. Opt-in via `AllowBulk(true)`. |
| Export rows | Globally hard-capped, override per view via `MaxExportRows`. **Differs from DynData** which had no cap. |
| Page size | Globally hard-capped, override per view via `MaxPageSize`. **Differs from DynData** which accepted `length=-1` (no paging). |
| Filter/search case-sensitivity | **Provider-detected on the server**, not a client flag. The client only sends intent (`Contains`/`Equals`). See Section 8. |
| Concurrency control (write) | Opt-in via `WithConcurrencyToken(...)`. The write endpoint respects the `If-Match` header; conflict → 412 Precondition Failed. Detail in Spec 05. |
| Error contract | RFC 7807 Problem Details. See Section 14. |

## 8. Filter & Search Contract (Relation to Pillar 2)

Vista defines **one neutral filter tree**, not three parallel paths like DynData (`externalFilter` + `globalSearch` + `jsonQB`). Whatever the request shape from a specific grid (DataTables, jQuery-QueryBuilder, AG Grid, OData), the adapter (Pillar 2) translates it into the following single structure before it reaches Core:

```csharp
public sealed record ViewQueryRequest(
    FilterNode? Filter,                  // single tree, the merged filter + search from the adapter
    IReadOnlyList<SortSpec> Sort,
    int Page,
    int PageSize,
    IReadOnlyList<string>? SelectFields = null);

public abstract record FilterNode;
public sealed record FilterLeaf(string Field, FilterOperator Op, object? Value) : FilterNode;
public sealed record FilterAnd(IReadOnlyList<FilterNode> Children) : FilterNode;
public sealed record FilterOr(IReadOnlyList<FilterNode> Children) : FilterNode;
public sealed record FilterNot(FilterNode Child) : FilterNode;

[Flags]
public enum FilterOperator
{
    None = 0,
    Equals = 1, NotEquals = 2,
    GreaterThan = 4, GreaterThanOrEqual = 8,
    LessThan = 16, LessThanOrEqual = 32,
    Contains = 64, StartsWith = 128, EndsWith = 256,
    In = 512, Between = 1024, IsNull = 2048,
    // Convenience grouping
    Range = GreaterThanOrEqual | LessThanOrEqual | Between,
    Text = Equals | NotEquals | Contains | StartsWith | EndsWith | IsNull,
}
```

### 8.1 Search vs Filter on the adapter side

The adapter's job is to turn the client request (e.g. DataTables `search.value`) into a `FilterOr` sub-tree of `FilterLeaf(Contains)` **only for fields declared `Searchable(...)`**, then AND it with the structured filter (e.g. from a Query Builder). The client does not decide which fields participate in search — that is the View's decision.

```text
Adapter input (DataTables):                Adapter output (ViewQueryRequest.Filter):
{                                          And(
  search: { value: "abc" },                  Or(
  columns: [Name, Status, ...],                Contains(Name, "abc"),
  filter: { Status = "Active" }                Contains(Description, "abc")  // only Searchable fields
}                                            ),
                                             Equals(Status, "Active")
                                           )
```

### 8.2 Case-sensitivity

The client does **not** send a `usePGSQL` / `ignoreCase` flag (in contrast to DynData). Vista decides the strategy on the server based on the EF Core provider:

| Provider | Default `Contains` translation |
|----------|--------------------------------|
| Npgsql (PostgreSQL) | `EF.Functions.ILike("%v%")` |
| SQL Server | `LIKE '%v%'` with the default collation (CI by default in most DBs) |
| SQLite | `LIKE` (ASCII-CI native) |
| MySQL / Pomelo | `LIKE` with the default collation |
| InMemory / test | `string.Contains(StringComparison.OrdinalIgnoreCase)` |

Per-view override is available if needed (e.g. force case-sensitive for a column with a special collation).

### 8.3 Operator whitelist enforcement

Validation is performed by `IViewExecutor` before the expression is built. The explicit rules:

1. **Client filter path** (structured filter from the adapter): each `FilterLeaf(field, op, value)` must satisfy `field` filterable (default true, unless `Filterable(false)`) **and** `op ∈ AllowedOperators[field]`. A violation → HTTP 400 with the rejected `field` & `operator` (see 14 — error model).
2. **Global-search path** (the `FilterOr(Contains, ...)` sub-tree the adapter builds from `search.value`): a searchable string field (default true, unless `Searchable(false)`) permits `Contains` **on this path only**.
3. **Path separation** is done by the adapter: it marks which sub-tree comes from search vs filter (e.g. an internal `FilterOrigin` record, or placing the search sub-tree at a fixed position in the tree). `IViewExecutor` evaluates each path with its own whitelist.
4. Consequence: a field can opt out of one path — search-only (`Filterable(false)`), filter-only (`Searchable(false)`), or both (default).

Example (everything active by default; just opt out the special ones):

```csharp
b.Field(x => x.CreditCardLast4, f => f.Searchable(false))            // filterable, NOT in the search box
 .Field(x => x.Description,     f => f.Filterable(false))            // searchable, cannot be filtered explicitly
 .Field(x => x.Status,          f => f.Operators(FilterOperator.Equals)) // restrict operators
 .Field(x => x.CategoryId,      f => f.Hidden().Scopable());        // client lookup key (§5.6)
```

**The contextual/scope path** (D47): the sub-tree the adapter builds from the client's `externalFilter`/lookup is validated against **`Scopable`** (not `Filterable`). Server-trusted scope from `IViewAuthorizer.ShapeQuery` (§5.6) is not whitelist-validated — it is trusted by definition.

This differs from DynData, which accepted a filter on any field present on the property, and which automatically included all string fields in global search.

## 9. AOT Constraints

This spec mandates that the implementation **must not violate**:

1. No `Activator.CreateInstance(Type)` on the hot path. `TQuery` is constructed via an expression compiled at compile-time by the source generator.
2. No `JsonSerializer.Deserialize(string, Type)` without a `JsonTypeInfo`. Every DTO has a generated `JsonSerializerContext`.
3. No `PropertyInfo.GetValue/SetValue` on the hot path. The `TCrud → TEntity` mapping is compiled at compile-time from `MapWritable(...)`.
4. Any public surface that cannot be AOT-clean is given an explicit `[RequiresUnreferencedCode]` and must have a non-reflection alternative path.
5. `IViewRegistry.Register<TView>()` is marked `[RequiresUnreferencedCode]` (runtime introspection of the view type). The AOT-clean path: the source generator emits an equivalent `Add(ViewMetadata)` (Pillar 3). There is no `RegisterAssembly` on the Core surface (DR1).

## 10. Paging & Response Shape

DynData's `PagingResult<T>` is a shape consumers already use. Vista preserves this **shape** (with deliberate breaking adjustments) so migration is minimal:

### 10.1 `PagedResult<T>`

```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalRows,        // long (DynData: int) — avoid overflow > 2B rows
    int PageIndex,         // 0-based
    int PageSize,
    long TotalPages);      // long (DynData: int) — consistency with TotalRows
```

**`ViewListResult<TRow>` (DR6).** `IViewExecutor.ListAsync<TRow>` returns a wrapper that
carries `PagedResult<TRow>` **plus** the unfiltered total (for DataTables `recordsTotal`, R10.4):

```csharp
namespace a2n.Vista.Ports;

public sealed record ViewListResult<TRow>(
    PagedResult<TRow> Page,        // .TotalRows = recordsFiltered
    long TotalRowsUnfiltered);     // = recordsTotal (scope applied, client filter/search NOT)
```

`PagedResult<T>` remains the neutral read shape; `ViewListResult` layers a second count over it without
duplicating `Items`. (Spec 02 once proposed `ViewQueryResult<T>` — **superseded** by
this `ViewListResult<TRow>`, see the Spec 02 reconciliation.)

Deliberate differences from DynData:

| DynData | Vista | Reason |
|---------|-------|--------|
| `int totalRows` | `long TotalRows` | Tables > 2.1B rows (rare but real) overflow in DynData. |
| `int totalPages` | `long TotalPages` | Consistency. |
| `object context` field | **removed** | Untyped, anti-pattern, never used in a strongly-typed way in DynData. |
| Mutable class (set outside the constructor) | immutable `record` | Thread-safety, defensive copy. |
| Sync `ToPagingResult` present | **Async-only** | Blocking IO in EF Core is an anti-pattern. |
| No `CancellationToken` | **Mandatory in all materializers** | A client cancel must stop the DB query. |
| `pageIndex * pageSize` (`int * int`) | Computed as `long` | DynData's `Skip(pageIndex * pageSize)` could overflow `int`. |

### 10.2 Materialization helper

There is no `IQueryable<T>.ToPagedResultAsync(...)` extension method in the Core public API — that is an internal detail of `IViewExecutor`. Reason: a public extension method tempts developers to call it from anywhere, bypassing Vista's validation/auth/limit path. If a developer needs manual paging outside a View, they use EF Core LINQ directly.

## 11. Export Contract

DynData has a built-in export endpoint (`csv`, `xlsx`) with a custom no-dependency `LiteExcelWriter`. Vista preserves this capability, but:

- **Pluggable exporter**: `IViewExporter` is a separate contract, one instance per format.
- **Default registrations**: `CsvViewExporter` and `LiteXlsxViewExporter` (the `LiteExcelWriter` ported to Core, still no-dependency).
- **Advanced exporter** (ClosedXML / OpenXmlSdk for multi-sheet, styling, formulas) lives in the separate package `a2n.Vista.Exporters.ClosedXml` — not a Core dependency.

### 11.1 Contract

```csharp
// Non-generic main contract. Resolved by the Format string ("csv", "xlsx").
// Not generic so it can be resolved via DI without reflecting into TQuery.
public interface IViewExporter
{
    string Format { get; }            // "csv", "xlsx", ...
    string MimeType { get; }
    string FileExtension { get; }

    // Erased TQuery: rows arrive as object from the streaming pipeline,
    // per-column accessors are taken from `fields` (delegate from source-gen).
    Task ExportAsync(
        IAsyncEnumerable<object> rows,
        IReadOnlyList<FieldMetadata> fields,
        ExportColumnAccessors accessors,
        Stream destination,
        ExportOptions options,
        CancellationToken ct = default);
}

// Compile-time per-view accessor map, generated by the source generator from
// ViewMetadata. No PropertyInfo.GetValue on the hot path.
public sealed class ExportColumnAccessors
{
    public Type RowType { get; }
    public IReadOnlyDictionary<string, Func<object, object?>> ByField { get; }
    // ...
}

public sealed record ExportOptions(
    char Separator = ',',           // default RFC 4180
    Encoding? Encoding = null,      // default UTF-8 with BOM
    string? CultureName = null);    // default invariant; format date/number
```

- The input is `IAsyncEnumerable<object>` + a per-column accessor map — **not** `IQueryable<dynamic>` and **not** a generic `ExportAsync<TQuery>` method. A generic method on a non-generic interface would force the caller to mono-morphize via reflection when resolving `IViewExporter` by string (and that races with pillar #3).
- The source generator produces `ExportColumnAccessors` per `View<TQuery>` as a partial init — the accessors are compile-time delegates, not `PropertyInfo.GetValue`.
- `fields` from `ViewMetadata` is used for headers & formatting (`DisplayAttribute`, type formatting).
- Streaming: the writer must stream rows to `destination` and must not load everything into memory.
- `CancellationToken` must be checked every N rows (default `N = 1024`, override by the implementor).

### 11.2 Hard limits

- `MaxExportRows` is enforced **before** the export pipeline runs: `qry.Take(maxRows + 1)`; if exceeded, return `413 Payload Too Large` with a suggestion to narrow the filter.
- Global default: 100,000 rows. Per-view override via `MaxExportRows(...)`.
- Absolute hard cap (cannot be bypassed): 1,000,000 rows. Beyond that, use a background job (not a synchronous endpoint).

### 11.3 DynData bugs that MUST NOT come along in migration

`LinqExtension.ExportToCSV` (DynData) has concrete bugs. Every `IViewExporter` implementor in Vista **must** satisfy the following properties, validated with tests:

| Property | DynData (LinqExtension.ExportToCSV) | Vista (mandatory) |
|----------|-------------------------------------|-------------------|
| Newline inside a cell value | `txt.Replace("\r", "").Replace("\n", "")` — **drops data** | RFC 4180: newline allowed **inside** a quoted value. |
| Quote inside a value | `Replace("\"", "\"\"")` — correct | Keep: double-quote escape. |
| Separator | `CultureInfo.CurrentCulture.TextInfo.ListSeparator` — on an ID/DE server locale becomes `;`, breaks Excel on other locales | Default `,` (RFC 4180). Explicit override per export call. |
| Encoding | No BOM → Windows Excel corrupts non-ASCII characters on non-UTF | UTF-8 **with BOM** by default. |
| Materializer | `foreach (var item in query)` — loads everything into memory | `IAsyncEnumerable<TQuery>` streaming, await per batch. |
| Per-cell accessor | `PropertyInfo.GetValue(item, null)` per row × per column | Source-gen delegate accessor (compile-time), mutable `ref` struct. |
| CancellationToken | None | Mandatory in the contract; checked every N rows. |

### 11.4 Import — out of v1.0

Import (CSV/Excel → bulk insert) is **not a v1.0 feature**. The reasons:

- DynData does not have this feature, so there is no *parity gap* for user migration.
- A safe design needs: per-record row validation, a field whitelist (stricter than `MapWritable`), transactional batching, per-row error reporting, deduplication, file column → DTO field mapping.
- Better planned after Core stabilizes.

Planned for **v1.x** as a separate package `a2n.Vista.Import` (CSV/Excel → `TCrud[]` validation pipeline → bulk insert via `ExecuteUpdateAsync`/`SaveChangesAsync`). A separate spec at that time.

## 12. Migration Notes from DynData

This spec is partly **breaking** with respect to DynData. Consumers of `a2n.DynData` migrating to Vista will encounter:

### 12.1 Default behavior changes

| DynData (automatic) | Vista |
|---------------------|-------|
| All entity string fields → global search | **Still default-allow**, but only string fields **in the projection** (not all entity columns). Per-field opt-out via `.Field(x => x.F, f => f.Searchable(false))`. |
| All properties → filterable | **Still default-allow**, limited to projection fields. Opt-out `f.Filterable(false)`. |
| All properties → sortable | **Still default-allow**, limited to projection fields. Opt-out `f.Sortable(false)`. |
| Auto-expose all `DbSet`s | **Gone** — every view is explicitly `AddView(...)`/`Register<TView>()`. |
| CRUD endpoint active by default | **Gone** — explicit `WithCrud<TCrud,TEntity>()` / `CrudOn<TEntity>()` + `MapWritable(...)`. |
| `IDynDataAPIAuth` (optional) | `IViewAuthorizer` + `UseAuthorizer<T>` — same style (single door). Without registration → default allow (DynData parity). |

Note: filter/sort/search are **not gone** (unlike the early spec version, which was opt-in). What changed from DynData is only the **scope**: limited to the projected fields, not the entire entity column set.

### 12.2 Request format

| DynData | Vista |
|---------|-------|
| `externalFilter` (flat JSON object) | `FilterNode` tree via the adapter |
| `jsonQB` (jQuery-QueryBuilder format) | `FilterNode` tree via `a2n.Vista.Adapters.QueryBuilder` |
| DataTables shape (`start`, `length`, `columns[]`, `order[]`) | `ViewQueryRequest` via `a2n.Vista.Adapters.DataTablesNet` |
| `usePGSQL=true` flag from the client | None. Provider-detected on the server. |
| `EnableSearchIgnoreCase=true` flag | None. Provider-detected on the server. |
| `length=-1` (return all) | Rejected. Page size hard-capped. |

### 12.3 Endpoint

Vista separates **List** (read many) from **Create** (write one). **Pillar 1** maps List to
`GET {root}/{viewName}` (paging/sort via query string) and Create to `POST {root}/{viewName}` — different
paths, no routing collision. The `POST {root}/{viewName}/query` form (body filter + `Accept`
negotiation) is the **Pillar 2 adapter layer** that layers on top of the Pillar 1 route.

| DynData | Vista (Pillar 1) |
|---------|-------|
| `POST /dyndata/{controller}/{viewName}/datatable` | `POST /api/views/{viewName}/query` — **Pillar 2 adapter form** (response shape via `Accept`/route) |
| `POST /dyndata/{controller}/{viewName}/list` | `GET /api/views/{viewName}` (Pillar 1, paging/sort from the query string; shape `ViewListResult`→JSON) |
| `POST /dyndata/{controller}/{viewName}/export` | `POST /api/views/{viewName}/export?format=csv\|xlsx` (forward-looking) |
| `POST /dyndata/{controller}/{viewName}/read` | `GET /api/views/{viewName}/{key}` |
| `POST /dyndata/{controller}/{viewName}/create` | `POST /api/views/{viewName}` (if the view is writable; Pillar 1 → 501) |
| `POST /dyndata/{controller}/{viewName}/update` | `PUT /api/views/{viewName}/{key}` (Pillar 1 → 501; concurrency: `If-Match`) |
| `POST /dyndata/{controller}/{viewName}/delete` | `DELETE /api/views/{viewName}/{key}` (Pillar 1 → 501; concurrency: `If-Match`) |
| `GET /dyndata/{controller}/{viewName}/metadata` | `GET /api/views/{viewName}/metadata` (forward-looking) |
| `GET /dyndata/{controller}/{viewName}/metadataQB` | Adapter-specific output (`a2n.Vista.Adapters.QueryBuilder` → jQuery-QueryBuilder schema). |
| `GET /dyndata/{controller}/{viewName}/dropdown` | Out of v1.0. Contract stub: `GET /api/views/{viewName}/distinct/{field}` is reserved (see Section 14). |

> **Pillar 1 write status.** The Create/Update/Delete routes are already mapped, but the EF write
> execution is not yet implemented → it returns **501 Not Implemented** (writable view) or **404**
> (read-only view, R3.3). The surface was deliberately stabilized first; write wiring follows (DR7).

### 12.4 The `LinqExtension.cs` & `AnonymousType.cs` functions — **NOT** ported

DynData has `Extensions/LinqExtension.cs` (1461 lines) containing many `IQueryable` extensions. Audit result:

| DynData function | Verdict | Replacement in Vista |
|----------------|---------|---------------------|
| `ToPagingResult` / `ToPagingResultAsync` (paging) | **Port the concept** (rewrite) | See Section 10. Internal to `IViewExecutor`, not a public extension. |
| `OrderBy(IQueryable, string key, bool asc)` + variants | **No** | Sort defaults to all projection fields (opt-out `.Field(x => x.F, f => f.Sortable(false))`); fields outside the projection → HTTP 400. Expression via a source-gen delegate. |
| `ThenBy(IQueryable, string key, ...)` variants | **No** | Part of the sort whitelist above. |
| `Where(IQueryable, object whereExp, Type)` | **No** | Strongly-typed `IQueryable<TSource>`, expression built from the `FilterNode` tree. |
| `AsNoTrackingDynamic(IQueryable<dynamic>)` | **No** | Standard EF Core `.AsNoTracking()`. |
| `Select(IQueryable, params string[] fieldNames)` + variants | **No** | Compile-time projection in `From<TSource>(...)` + a source-gen accessor for sparse `SelectFields`. |
| `InnerJoin` / `LeftJoin` / `RightJoin` / `FullJoin` (~750 lines) | **No** | The developer uses standard EF Core LINQ in the `FromQuery<TSource>(...)` delegate. |
| `SelectRecursive<T>(IEnumerable<T>, Func<T, IEnumerable<T>>)` | Optional | A general utility, can be dropped or placed in `Vista.Core/Utilities` if widely used. |
| `ExportToCSV` / `ExportToExcel` | **Port the concept** (rewrite) | See Section 11. The RFC 4180 bug and per-cell reflection must be fixed. |
| `GroupByDateTimeInterval` (commented-out incomplete) | **No** | Time-bucketing is a v1.x candidate. |
| `AnonymousType.cs` (Reflection.Emit, ~29 KB) | **No** | Vista uses static types in developer-defined DTOs. Source-gen produces partial classes, not runtime emitted types. Fundamentally AOT-incompatible. |

### 12.5 Compatibility layer — **Decided: none (D98)**

Vista does **not** provide a DynData compatibility layer. **Migration is manual**: there is no
seamless drop-in, no `/dyndata/*` route aliases, no committed wire shim for `externalFilter`/`jsonQB`.
Reason: a wire shim = a permanent maintenance burden for the very format we want to leave behind, and
it holds back the evolution of the Vista contract.

What replaces the compat shim:

- **DynData ergonomics are preserved via Style A**, not via a shim. Style A is the "spiritual
  successor" to DynData's `QueryTemplate` — the same concept & coding style, without the reflection/mass-assignment weaknesses.
- **The migration guide (`08-migration-from-dyndata.md`) is the primary migration tool** (not optional):
  concrete before/after examples for `QueryTemplate.AddQuery(...)` → `AddView(...)` Style A, the mapping
  of `externalFilter`/`jsonQB` → `FilterNode`, and the mapping of old endpoints → Vista routes. Without a shim,
  the quality of this guide = the quality of the migration experience.

## 13. Decision Log

| # | Decision | Status | Note |
|---|-----------|--------|---------|
| D1 | Separate `TQuery` and `TCrud` at the type level | **Decided** | Prevents mass-assignment like DynData. |
| D2 | No auto-expose of `DbSet` | **Decided** | Views must be explicit. |
| D3 | `Filterable` & `Sortable` opt-in per field | **Superseded by D42** | Inverted to default-allow + opt-out. |
| D4 | Authorization must be set at build time | **Superseded by D43** | Replaced by the central authorizer; default-allow if not registered. |
| D5 | `System.Text.Json` native in Core, Newtonsoft in a separate package | **Decided** | Per ROADMAP. |
| D6 | CPM (Central Package Management) | **Decided** | `Directory.Packages.props` at the repo root. |
| D7 | Test framework | **Decided: TUnit** | Modern, AOT-friendly — aligned with Pillar 3. Set up when the first test project is created. |
| D8 | Multi-target `net8.0;net9.0;net10.0` | **Decided** | Already in `Directory.Build.props`. |
| D9 | `<Nullable>disable</Nullable>` global | **Superseded by D9-revised** | See D9-revised below. |
| D10 | View identifier: string `Named("customers")` or type-only | **Decided: string + startup dedup** | Identity via the `Named`/`AddView` string; duplicate name → startup error (R1.3). Compile-time validation via the source generator (Pillar 3). |
| D11 | How `From<TSource>(projection)` obtains `IQueryable<TSource>` | **Decided** | Convention `DbContext.Set<TSource>()`; an explicit factory (`FromQuery`/`AddView` delegate) wins. Applied in `SplitViewExecutionPlan` (EF). |
| D12 | Does `View` need generation from source-gen (besides metadata)? | **Decided: deferred to Pillar 3** | Pillar 1 uses a reflection path with `[RequiresUnreferencedCode]`; source-gen partial + auto-register in Spec 03. |
| D13 | `ViewMetadata` location: runtime vs compile-time | **Decided: runtime (Pillar 1)** | Built at runtime via reflection (RUC); the compile-time variant = the source generator (Pillar 3, Spec 03 §6). |
| D14 | `Searchable` separate from `Filterable` (global search does not auto-attack all string fields like DynData) | **Superseded by D42** | The Filter vs Search separation concept is retained (§4.4), but the searchable default is inverted to allow + opt-out. |
| D15 | Import (CSV/Excel → bulk insert) | **Decided: defer to v1.x** | Section 11.4. Not a DynData parity gap; needs a mature validation design. Planned as a separate package `a2n.Vista.Import`. |
| D16 | Pluggable exporter, default port of `LiteExcelWriter` into Core | **Decided** | Section 11. `IViewExporter` contract, default `CsvViewExporter` + `LiteXlsxViewExporter` no-dep. Advanced (ClosedXML/EPPlus) in a separate package. |
| D17 | Case-sensitivity & ILIKE/LIKE: provider-detected on the server, not a client flag | **Decided** | Section 8.2. The client only sends intent (`Contains`/`Equals`), Vista picks the translation based on the EF Core provider. |
| D18 | A single filter tree (`FilterNode`) replaces DynData's 3 paths (`externalFilter` + `globalSearch` + `jsonQB`) | **Decided** | Section 8. The adapter (Pillar 2) translates the specific grid format into the neutral tree. |
| D19 | Absolute export hard cap of 1,000,000 rows (cannot be bypassed via configuration) | **Decided** | Section 10.2. Beyond that, use a background job. |
| D20 | Compatibility layer `a2n.Vista.Compat.DynData` (route aliases `/dyndata/*` etc.) | **Revised by D98: none** | Section 12.5. Manual migration; DynData ergonomics via Style A + the migration guide. |
| D21 | `PagedResult<T>` immutable record, `long` totals, no `object context`, async-only materialization | **Decided** | Section 10. Breaking from DynData's `PagingResult<T>` (mutable class, `int`, sync overload). |
| D22 | `IViewExporter` mandatory properties: RFC 4180 CSV, UTF-8 BOM, streaming `IAsyncEnumerable`, source-gen accessor, `CancellationToken` | **Decided** | Section 11.3. Explicitly closes the DynData bugs. |
| D23 | `AnonymousType.cs` (Reflection.Emit runtime types) is not ported. Vista uses source-gen partial classes. | **Decided** | Section 12.4. Fundamentally anti-AOT. |
| D24 | Dynamic join via string field name (`InnerJoin`/`LeftJoin`/`RightJoin`/`FullJoin` in DynData) is not ported. The developer uses EF LINQ directly in the source query delegate. | **Decided** | Section 12.4. ~750 lines of code removed, static types replace it. |
| D25 | `MapWritable` exhaustiveness: default **ignore** for `TCrud` fields that are not mapped; opt-in strict via `[VistaWritable(strict: true)]`. Source-gen emits the diagnostic `VISTA0010` (info). | **Decided** | Closes prior Open Question #5. |
| D26 | The read-only View is split into the base class `View<TQuery>` with the builder `IViewBuilder<TQuery>` that **does not have** `CrudOn`. `View<TQuery, TCrud>` is a separate base, not a subclass of `View<TQuery>`. The `NoCrud` marker is removed. | **Decided** | Section 5.1, 5.2. Prevents compile-time access to the CRUD knobs on a read-only view. |
| D27 | The adapter does **not** access the raw `IQueryable<TSource>`. The adapter only speaks `ViewQueryRequest` and `PagedResult<TQuery>`. Optimizations like `Include` are the responsibility of `FromQuery<TSource>(...)` in the View definition. | **Decided** | Closes prior Open Question #3. |
| D28 | Row filter defaults to **TSource** (pre-projection) via `WithRowFilter<TSource>(...)`. Post-projection `WithProjectedRowFilter` exists for special cases. | **Decided** | Section 5.2, 6. Closes prior Open Question #2. |
| D29 | `MaskField(field, predicate, masker)` — the `Func<TProp, TProp>` masker is required. No implicit masking (`null` / `"***"`). | **Decided** | Section 5.2. |
| D30 | `WithConcurrencyToken(field)` opt-in on `ICrudBuilder`. The write endpoint respects the `If-Match` header. Conflict → 409 / 412. | **Decided** | Section 5.2, 14.2. |
| D31 | `WithInterceptor<T>` opt-in. Forecasts the v1.x audit log so v1.0 → v1.x is non-breaking. | **Decided** | Section 5.2. |
| D32 | The list-query endpoint is separated from create: `POST /api/views/{viewName}/query` vs `POST /api/views/{viewName}`. Avoids an MVC routing collision. | **Decided** | Section 12.3. |
| D33 | Error contract: RFC 7807 Problem Details, `type` namespaced under `https://a2n.dev/vista/errors/`. | **Decided** | Section 14.1. JSON shape detail in Spec 05. |
| D34 | `IViewExporter` non-generic. A generic method on a non-generic interface would force reflection when resolving by `Format`. Source-gen produces `ExportColumnAccessors` per view. | **Decided** | Section 11.1. |
| D35 | Distinct-values endpoint `GET /api/views/{viewName}/distinct/{field}` reserved as a contract stub — v1.x implementation. | **Decided** | Section 14.3. v1.x is non-breaking. |
| D36 | `Filterable<TProp>` overload without a generic default parameter. | **Superseded by D42/D45** | Standalone `Filterable(...)` removed; operators configured via `.Field(..., f => f.Operators(...))`. |
| D9-revised | `<Nullable>enable</Nullable>` global before any substantial `a2n.Vista.Core` implementation. Changing it from `disable` in `Directory.Build.props` is a prerequisite for the first PR touching the public API. | **Decided** | Replaces D9 "Open". An AOT-first library must not accumulate NRT debt. |
| D37 | Two authoring styles: anonymous central-template (Style A, DynData-like) + typed class-per-view (Style B). Both produce the same `ViewMetadata`. | **Decided** | Section 4.5. Vista = an evolution of DynData, not a rewrite. |
| D38 | Typing invariant: an anonymous projection is only for read facets (List/Detail); the Write facet REQUIRES a typed `TCrud` + `MapWritable`. An anonymous-only View = read-only. | **Decided** | Section 4.5, 4.6. Closes mass-assignment at the design level. Refines the original formulation "anonymous ⇒ the whole view read-only" into per-facet (read may be anonymous, write must be typed). |
| D39 | Facet model: one View = a resource with ≤3 facets (List mandatory, Detail optional fallback-by-PK, Write optional typed). The PK bridges the facets. Auth per-facet. | **Decided** | Section 4.6. List=grid, Detail=display form, Write=create/edit. |
| D40 | Style A triggers `[RequiresUnreferencedCode]` (registration + anonymous serialization). Full Native AOT → Style B. The Write facet stays AOT-clean in both styles. | **Decided** | Section 4.5, 5.5. Aligned with the ROADMAP Pillar 3 tradeoff. |
| D41 | Style A field metadata via the fluent expression `.Field(x => x.Prop, f => f.PrimaryKey().Hidden())`, not the string callback `meta.FieldName == "..."` (DynData). | **Decided** | Section 5.5, 6A. Safer from typos. |
| D42 | Filter/Sort/Search **default-allow** for all projection fields (opt-out via `.Field(..., f => f.Filterable(false)/.Searchable(false))`). The security boundary = the contents of the curated projection. | **Decided** | Section 4.4, 7. **Supersedes D3 & D14** (formerly opt-in/default-deny). |
| D43 | Authorization **centralized** via `IViewAuthorizer.IsAllowedAsync` + `ShapeQuery` (the `IDynDataAPIAuth` style), registered with `UseAuthorizer<T>`. Without an authorizer → **default allow** + startup warning (not fail-closed). | **Revised by D94** | Section 5.6. **Supersedes D4**. The "no authorizer = allow-all" posture now only applies in Development; non-Development is fail-closed (D94). |
| D44 | Route **global** via `RouteRoot(...)`; the view route is derived `{root}/{viewName}`. No per-view `Route()` (escape-hatch only). | **Decided** | Section 5.6, 12.3. |
| D45 | Per-field configuration via a single builder `.Field(selector, f => f.Label(...).Hidden().Operators(...).Searchable(false))`; auto label from the field name (PascalCase → "Title Case"). | **Decided** | Section 5.4, 5.5. Replaces the verbose `.Filterable().Sortable().Searchable()` chain. |
| D46 | `IViewAuthorizer.ShapeQuery` becomes the home of the server-trusted contextual/row filter (tenant, ownership) — answering option (a) of the `externalFilter` question (see the DataTables reference). Client filter scoping remains subject to the whitelist. | **Decided** | Section 5.6. |
| D47 | Contextual/lookup filter from the **client** (the `externalFilter` equivalent) only to `Scopable` fields (opt-in, default false), separate from UI `Filterable`. Server-trusted scope via `ShapeQuery`. | **Decided** | Section 5.6, 8.3. Option (c). |
| D48 | **Package layering**: `Core` is free of EF & HTTP (neutral contracts + ports `IViewExecutor`/`IViewScope`). `EntityFrameworkCore` implements `IViewExecutor` + DbContext-bound authoring. `IViewAuthorizer` in `AspNetCore` (HTTP-bound). Adapters & `Client.TypeScript` → `Core` only. EF & AspNetCore do **not** reference each other (they meet at `IViewExecutor`). | **Decided** | ROADMAP "NuGet Package Structure". Applied in the csproj. |
| D49 | Detail facet v0.x = fallback to the List projection by-PK (Style A). A Detail facet with its own projection is deferred. | **Decided** | Section 4.6. |
| D50 | `<Nullable>enable</Nullable>` set globally (implements D9-revised). | **Done** | `Directory.Build.props`. |

### 13.1 Implementation reconciliation (DR1–DR10)

Decisions that surfaced/were resolved during the `pilar-1-core` implementation (code = source of truth).
Prefixed `DR` so they do not collide with the `D51+` numbering used by Spec 02–05.

| # | Decision | Status | Note |
|---|-----------|--------|---------|
| DR1 | `IViewRegistry`: primary sink `Add(ViewMetadata)`, `Register<TView>()` (RUC), `Get` **nullable** (miss→null→404), `All`. No `Register(Type)`/`RegisterAssembly` in Core. | **Decided** | §5.3. Refines the non-null `Get` sketch. |
| DR2 | DI **two doors**: `AddVista` (`IVistaBuilder`, EF package — `RouteRoot`, `RegisterTemplate<TTemplate,TDbContext>`, `Register<TView>`, `Register<TView>(plan)`) + `AddVistaEndpoints` (`IVistaEndpointBuilder`, AspNetCore package — `RouteRoot`, `UseAuthorizer<T>`). | **Decided** | §5.3, §5.6. EF & AspNetCore do not reference each other (D48); `RegisterTemplate` requires an explicit `TDbContext`. |
| DR3 | Pillar 1 List = **`GET {root}/{viewName}`** (query string), not `POST .../query`. `POST .../query` (body + `Accept`) is the Pillar 2 adapter form that layers on top. | **Decided** | §5.6, §12.3. |
| DR4 | `WithValidator`/`WithInterceptor` (on `ICrudBuilder`/`ICrudFacetBuilder`) **deferred** — not in Pillar 1 code yet. | **Decided: deferred** | §5.2, §5.5. v1.x forecast. |
| DR5 | Style B `Register<TView>()` = **metadata-only** (executable when + `IViewExecutionPlan` via `Register<TView>(plan)` / source-gen). Style A `RegisterTemplate` produces metadata + plan. | **Decided** | §5.3. The Style B builder does not yet route the source/projection to EF. |
| DR6 | `IViewExecutor.ListAsync` returns `ViewListResult<TRow>(PagedResult<TRow> Page, long TotalRowsUnfiltered)`. **Replaces** the proposed `ViewQueryResult<T>` of Spec 02. | **Decided** | §10.1. |
| DR7 | The write endpoints (Create/Update/Delete) are already mapped but the EF execution is not → **501** (writable) / **404** (read-only). | **Decided: write wiring follows** | §12.3. |
| DR8 | Write is **merged** into `IViewExecutor` (`CreateAsync<TCrud>`/`UpdateAsync<TCrud>`/`DeleteAsync`), **not** a separate `IViewWriter` port. `IViewExecutor` is **generic** (`ListAsync<TRow>`/`DetailAsync<TRow>`), not erased-to-`object`. | **Decided** | Differs from the Spec 02/05 sketch; the code prevails. |
| DR9 | `FilterOrigin` = a **public 3-value enum** (`Filter`/`Search`/`Scope`) passed to `FilterCompiler.Compile(node, origin, view)`. Not a field on `FilterLeaf`; no `Trusted` value (trusted scope goes through `IViewScope`, not validated). | **Decided** | §8.3. `ViewQueryRequest`/`FilterLeaf` stay as in §8 (without `Origin`/`IncludeUnfilteredCount`). |
| DR10 | `app.MapView(string viewName)` (by name) + `app.MapVistaViews()` (generic, resolve by name at request time). `MapView<TView>()` deferred (needs source-gen type→name resolution). | **Decided** | §5.6. |

### 13.2 Follow-up decisions (D94+)

Cross-cutting decisions from the architecture review (operations posture, observability, versioning). Numbered
sequentially after D93 (Spec 05).

| # | Decision | Status | Note |
|---|-----------|--------|---------|
| D94 | **Fail-safe auth posture.** Without an authorizer: **Development** → allow-all + warning; **non-Development** (Production/Staging/UAT/env unset) → **startup fail-closed** unless the explicit opt-in `AllowAnonymousAccess()`. The 2-level model (switch + policy) is retained. | **Decided** | §5.6. **Revises D43**. Organization-neutral: a security omission fails safe; "open" becomes an explicit reviewed decision. |
| D95 | A `MaskField`'d field **defaults to `Filterable(false)`** (explicit opt-in if needed; ideally `Equals`-only). | **Decided** | §5.2, §7. Closes probing of the masked value. |
| D96 | **Style A & Style B are permanent** (no deprecation of Style A). The AOT asymmetry is permanent & explicit: Style A serialization stays RUC forever; its filter/sort/paging is AOT-clean. Use-case guidance (monolith→A, modular monolith→B, microservices→free). | **Decided** | §4.5. |
| D97 | **Cross-assembly view discovery** (Style B in sub-projects, assemblies attached in main) promoted from an Open Question (Spec 03 §17 #4) to a **mandatory Pillar 3 requirement**. | **Decided** | A consequence of the D96 use-case (modular monolith). |
| D98 | **No DynData compatibility layer.** Manual migration; DynData ergonomics preserved via Style A; the migration guide is the primary tool. | **Decided** | §12.5. **Revises D20**. |
| D99 | **Wire versioning via URL**: `/api/views` = latest alias (dev only, not for production clients), `/api/v{n}/views` = pinned (production). The version = the contract envelope (wire/`ViewMetadata`/`FilterNode`), not per-view. Coexistence across versions is allowed by design; v1.0 ships v1 + the alias. | **Decided** | §15 #1, `11-versioning-and-deprecation.md`. **Closes Open Question §15 #1**. |
| D100 | **Vendor-neutral observability**: instrument via OpenTelemetry-native (`ActivitySource`/`Meter`/`ILogger`), with no APM dependency at all; enrich auto-instrumented spans with View semantics; operational status (e.g. the D94 authorizer) via standard health checks. Opt-in & zero-cost when not enabled. | **Decided** | `10-operations-and-observability.md`. |
| D101 | **One `RouteRoot` source.** Currently duplicated in `IVistaBuilder` (EF) & `IVistaEndpointBuilder` (AspNetCore). Unified into a single Core option that both layers read. | **Decided (implementation follows)** | A public-code refactor; see the execution note. |

> **D101 execution note.** Unifying `RouteRoot` touches the public API of two packages and needs
> careful design (EF embeds the route into `ViewMetadata.Route`; AspNetCore owns the live route). Recorded as a
> decision + a separate refactor task — **not yet** executed in this documentation session.

## 14. Error Model & Concurrency

### 14.1 Error contract — RFC 7807 Problem Details

All Vista endpoints return `application/problem+json` for errors, with `type` namespaced under `https://a2n.dev/vista/errors/`. Example classification:

| Condition | HTTP | `type` |
|--------|------|--------|
| Filter on a field that is not `Filterable` | 400 | `.../filter-field-not-allowed` |
| Filter operator outside `AllowedOperators` | 400 | `.../filter-operator-not-allowed` |
| Sort on a field that is not `Sortable` | 400 | `.../sort-field-not-allowed` |
| `TCrud` validation failed | 400 | `.../validation` (per-field detail) |
| Not authenticated | 401 | `.../unauthorized` |
| Authorize policy failed | 403 | `.../forbidden` |
| Not found (CRUD by key) | 404 | `.../not-found` |
| `If-Match` token wrong / missing | 412 | `.../precondition-failed` |
| Concurrency conflict on `SaveChanges` | 409 | `.../concurrency-conflict` |
| Page size / export rows hard limit reached | 413 | `.../payload-too-large` |
| Unexpected error | 500 | `.../unexpected` |

Each response includes machine-readable `extensions`: the rejected field name, the disallowed operator, the allowed list, etc. JSON shape detail in Spec 05.

### 14.2 Concurrency control (write path)

- A View with `WithConcurrencyToken(field)` adds the token to read responses (`GET /{key}` and `query`) as a DTO field or an `ETag` header (default: header).
- The client MUST send `If-Match: <token>` on `PUT` / `DELETE`.
- The endpoint mapper (Spec 05): no header → 412; header does not match the DB value on `SaveChanges` → 409.
- The token may be `byte[] RowVersion` (SQL Server `rowversion`), `xmin` (PostgreSQL), or a `DateTime LastModifiedAt` column (databases without a native rowversion). Encoding to the ETag string: base64url for `byte[]`, ISO-8601 for `DateTime`.

### 14.3 Distinct-values endpoint (stub)

The endpoint `GET /api/views/{viewName}/distinct/{field}?prefix=&take=50` is reserved for supporting AG Grid set filter, MudBlazor SelectFilter, PrimeNG MultiSelect, etc. **Out of v1.0**, but the route and validation (`field ∈ Filterable`, hard cap `take ≤ 1000`) are defined now so v1.x is non-breaking. Full implementation: Spec 04 or a separate spec.

## 15. Open Questions

1. **Route versioning**: **Resolved (D99)** — the wire is versioned via URL: `/api/views` = the "latest" alias
   (for dev/exploration, **do not use from production clients**), `/api/v{n}/views` = pinned (mandatory for
   production). The version = the version of the *contract envelope* (wire/`ViewMetadata`/`FilterNode`), not per-view. Detail
   in `11-versioning-and-deprecation.md`.
2. **Sparse `SelectFields` in `ViewQueryRequest`** (Section 8): when may the adapter set this? The current spec does not explain the trade-off vs the compile-time projection `From<TSource>`. Candidate: `SelectFields` is a **subset** of the fields in `TQuery`, cannot add; source-gen produces an accessor delegate per combination.
3. **`From<TSource>` DI resolution without an explicit factory** (D11): **Resolved** — the convention
   `DbContext.Set<TSource>()`, an explicit factory (`FromQuery`/the `AddView` delegate) wins. Applied
   in `SplitViewExecutionPlan` (EF layer). See Decision Log D11.
4. **`MapWritable` exhaustiveness** — **Decided: default ignore** (see Decision Log D25). Source-gen produces an info-level diagnostic (`VISTA0010`) for `TCrud` fields that are not mapped. Opt-in strict via the attribute `[VistaWritable(strict: true)]` on the `TCrud` class.
5. **Shape of the concurrency token in the read response**: an `ETag` header (HTTP-idiomatic) vs a DTO field (easier for a JS client). Candidate default: the header, with the option to expose to a field via `WithConcurrencyToken(..., exposeAs: "RowVersion")`.

## 16. Next Spec Documents

After this spec stabilizes:

- `02-filter-and-query.md` — expression builder detail, provider-aware filter, sanitization, operator whitelist validation.
- `03-source-generator.md` — codegen contract (input: `ViewMetadata`, output: registration + serialization context + OpenAPI).
- `04-adapter-contract.md` — `IViewAdapter<TRequest, TResponse>` (Pillar 2), including the DataTables & QueryBuilder adapters that are DynData migration targets. Reference for DynData's real behavior (the `metadataQB`/`datatable` contract, the 3 filter paths, the `jsonQB` payload): [`../reference/dyndata-datatables-observed.md`](../reference/dyndata-datatables-observed.md).
- `05-aspnetcore-mapping.md` — `MapVistaViews()`/`MapView(string)`, route conventions, error model, response shape.
- `06-typescript-client.md` — the codegen shape of DTOs + the filter API in TS.
- `07-export.md` — `IViewExporter` detail, default formats, streaming, `LiteXlsxViewExporter` migration from DynData.
- `08-migration-from-dyndata.md` — extended migration guide with concrete per-feature examples. **The primary migration tool (D98)** because there is no compat layer.
- `10-operations-and-observability.md` — the vendor-neutral observability contract, health checks, startup validation (D100, D94).
- `11-versioning-and-deprecation.md` — the public contract surface, package vs wire version scheme, deprecation policy (D96–D99).
