// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Representative typed Style B view fixtures for the Phase 4 HTTP-SURFACE dispatch invoker
// (spec source-generator-http-surface, task 10.1; Decision Log D123/D124).
//
// These are REAL, minimal, VALID, partial, single-source typed Style B views. Because this assembly
// references Core AND the EF layer AND the source generator (as an analyzer), the ViewInvokerGenerator
// emits INTO this assembly, per covered view, a `file sealed` <View>_VistaViewInvoker.g.cs holding a
// reflection-free IViewInvoker (closed-generic List/Detail/Create/Update, direct await, no
// MakeGenericMethod, no ViewListResult<TRow> reflection) plus a [ModuleInitializer] that registers it
// into a2n.Vista.Ports.ViewInvokerStore keyed by the view's runtime Name.
//
// COMPILE-ONCE, QUANTIFY-OVER-REQUESTS (design "Cost control for the master parity property"). The
// master oracle-parity property test (task 10.2, Property 1) compiles this fixture set ONCE, resolves
// each view's GENERATED invoker from the store, and compares its dispatch + serialization — over random
// request shapes — against the reflection oracle. It never re-compiles per iteration. The AOT probe
// (task 11.x) likewise rides these compiled-once views.
//
// The set is deliberately chosen to cover the dispatch/serialization surface Property 1 must exercise
// (mirroring the Phase 2 / write-DSL fixtures):
//   * ProductView              — a read-only SINGLE-KEY view (View<TRow>): the minimal read invoker
//                                (ListAsync<TRow> + DetailAsync<TRow>, IsWritable => false), keyed by a
//                                single integer PK, with one client-scopable field so the Scope channel
//                                is exercised.
//   * RegionTerritoryView      — a read-only COMPOSITE-KEY view (View<TRow>): a two-field key
//                                (RegionId, TerritoryId) marked in declaration order, exercising
//                                Detail-by-composite-key extraction on the generated read path.
//   * EmployeeView             — a WRITABLE view with an optimistic-CONCURRENCY TOKEN
//                                (View<TRow, TCrud> + WithConcurrencyToken): the read+write invoker
//                                (Create/Update close TCrud at compile time, IsWritable => true), with a
//                                rowversion token passed through unchanged (D120), and a non-key
//                                whitelist so only safe scalars are writable.
//
// Each view ships a probe/sample App_Json_Context ([JsonSerializable] over TRow, ViewListResult<TRow>,
// PagedResult<TRow>, and — for the writable view — TCrud), which is the EXACT set the generator's
// VISTA0041 guidance names for that view. These developer-authored, source-generated JsonSerializerContexts
// chain into the Serialization_Seam via AddVistaJsonContext(...), making per-view serialization AOT-clean.
//
// Every view is single-source, partial, has an implicit public parameterless constructor, and declares
// its primary key via per-field PrimaryKey() marks — the exact conditions the ViewInvokerGenerator needs
// to emit an invoker + [ModuleInitializer] for it.

using System;
using System.Text.Json.Serialization;
using a2n.Vista.Authoring;
using a2n.Vista.Ports;
using a2n.Vista.Results;

namespace a2n.Vista.GeneratorHttpSurfaceSample;

// =====================================================================================================
// Case 1 — READ-ONLY, SINGLE-KEY view. The minimal read invoker: ListAsync<TRow> + DetailAsync<TRow>,
// IsWritable => false, keyed by a single integer PK. One field is made client-scopable so the generated
// read path exercises the server-trusted Scope channel.
// =====================================================================================================

/// <summary>EF source entity for <see cref="ProductView"/>; keyed by <see cref="ProductId"/>.</summary>
public sealed class ProductEntity
{
    /// <summary>Primary key (EF infers it by convention).</summary>
    public int ProductId { get; set; }

    /// <summary>Product name — filterable/sortable/searchable by default.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Category — also made client-scopable so the Scope channel is exercised.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Unit price — filterable/sortable, not searchable.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>Projected (read) row for <see cref="ProductView"/>.</summary>
public sealed class ProductRow
{
    /// <summary>Primary key.</summary>
    public int ProductId { get; init; }

    /// <summary>Name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Category.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Unit price.</summary>
    public decimal UnitPrice { get; init; }
}

/// <summary>
/// Read-only typed Style B view over <see cref="ProductEntity"/> with a single integer primary key. It is
/// <c>partial</c>, single-source, has an implicit public parameterless constructor, and declares its PK —
/// the conditions the <c>ViewInvokerGenerator</c> needs to emit a read-only <c>IViewInvoker</c>
/// (<c>IsWritable =&gt; false</c>) and register it at module load.
/// </summary>
public partial class ProductView : View<ProductRow>
{
    /// <summary>Globally-unique view name; the key the generated invoker is stored under.</summary>
    public const string ViewName = "http-surface-products";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<ProductRow> builder) =>
        builder
            .Named(ViewName)
            .From<ProductEntity>(s => new ProductRow
            {
                ProductId = s.ProductId,
                Name = s.Name,
                Category = s.Category,
                UnitPrice = s.UnitPrice,
            })
            .Field(x => x.ProductId, f => f.PrimaryKey())
            .Field(x => x.Category, f => f.Scopable());
}

/// <summary>
/// Developer-authored, source-generated <c>App_Json_Context</c> for <see cref="ProductView"/> — the exact
/// <c>[JsonSerializable]</c> set the generator's VISTA0041 guidance names for a read-only view
/// (<c>TRow</c>, <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>). Registered into the
/// Serialization_Seam via <c>AddVistaJsonContext(...)</c> so its List/Detail responses serialize AOT-clean.
/// </summary>
[JsonSerializable(typeof(ProductRow))]
[JsonSerializable(typeof(ViewListResult<ProductRow>))]
[JsonSerializable(typeof(PagedResult<ProductRow>))]
public sealed partial class ProductJsonContext : JsonSerializerContext
{
}

// =====================================================================================================
// Case 2 — READ-ONLY, COMPOSITE-KEY view. A two-field key (RegionId, TerritoryId) marked in declaration
// order, exercising Detail-by-composite-key extraction on the generated read path.
// =====================================================================================================

/// <summary>EF source entity for <see cref="RegionTerritoryView"/>; keyed by (RegionId, TerritoryId).</summary>
public sealed class RegionTerritoryEntity
{
    /// <summary>First primary-key component.</summary>
    public int RegionId { get; set; }

    /// <summary>Second primary-key component.</summary>
    public string TerritoryId { get; set; } = string.Empty;

    /// <summary>Territory description — filterable/sortable/searchable.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>Projected (read) row for <see cref="RegionTerritoryView"/> exposing the composite key.</summary>
public sealed class RegionTerritoryRow
{
    /// <summary>First primary-key component.</summary>
    public int RegionId { get; init; }

    /// <summary>Second primary-key component.</summary>
    public string TerritoryId { get; init; } = string.Empty;

    /// <summary>Description.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Read-only typed Style B view over <see cref="RegionTerritoryEntity"/> with a two-field composite key
/// (RegionId, TerritoryId) marked in declaration order. Exercises the generated read invoker's
/// Detail-by-composite-key path (<c>IsWritable =&gt; false</c>).
/// </summary>
public partial class RegionTerritoryView : View<RegionTerritoryRow>
{
    /// <summary>Globally-unique view name; the key the generated invoker is stored under.</summary>
    public const string ViewName = "http-surface-region-territories";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<RegionTerritoryRow> builder) =>
        builder
            .Named(ViewName)
            .From<RegionTerritoryEntity>(s => new RegionTerritoryRow
            {
                RegionId = s.RegionId,
                TerritoryId = s.TerritoryId,
                Description = s.Description,
            })
            .Field(x => x.RegionId, f => f.PrimaryKey())
            .Field(x => x.TerritoryId, f => f.PrimaryKey());
}

/// <summary>
/// Developer-authored, source-generated <c>App_Json_Context</c> for <see cref="RegionTerritoryView"/> —
/// the exact <c>[JsonSerializable]</c> set VISTA0041 names for a read-only view.
/// </summary>
[JsonSerializable(typeof(RegionTerritoryRow))]
[JsonSerializable(typeof(ViewListResult<RegionTerritoryRow>))]
[JsonSerializable(typeof(PagedResult<RegionTerritoryRow>))]
public sealed partial class RegionTerritoryJsonContext : JsonSerializerContext
{
}

// =====================================================================================================
// Case 3 — WRITABLE view with an optimistic-CONCURRENCY TOKEN. View<TRow, TCrud> + WithConcurrencyToken:
// the read+write invoker (Create/Update close TCrud at compile time, IsWritable => true), with a
// rowversion token passed through unchanged (D120), and a non-key whitelist so only safe scalars are
// writable (the key is never assigned by the write path — D25).
// =====================================================================================================

/// <summary>EF source entity for <see cref="EmployeeView"/>; keyed by <see cref="EmployeeId"/>.</summary>
public sealed class EmployeeEntity
{
    /// <summary>Primary key — never assigned by the write path.</summary>
    public int EmployeeId { get; set; }

    /// <summary>Whitelisted string scalar.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Whitelisted string scalar.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Whitelisted value-type scalar.</summary>
    public int ReportsTo { get; set; }

    /// <summary>Optimistic-concurrency token (rowversion); not in the writable whitelist.</summary>
    public int Version { get; set; }
}

/// <summary>Projected (read) row for <see cref="EmployeeView"/>.</summary>
public sealed class EmployeeRow
{
    /// <summary>Primary key.</summary>
    public int EmployeeId { get; init; }

    /// <summary>Full name.</summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>Title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Reports-to id.</summary>
    public int ReportsTo { get; init; }
}

/// <summary>Typed write contract for <see cref="EmployeeView"/>: only safe non-key scalars are writable.</summary>
public sealed class EmployeeCrud
{
    /// <summary>New full name.</summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>New title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>New reports-to id.</summary>
    public int ReportsTo { get; init; }
}

/// <summary>
/// Writable typed Style B view over <see cref="EmployeeEntity"/> declaring a non-key <c>MapWritable</c>
/// whitelist and an optimistic-concurrency token via <c>WithConcurrencyToken(e =&gt; e.Version)</c>. The
/// <c>ViewInvokerGenerator</c> emits a read+write <c>IViewInvoker</c> (<c>IsWritable =&gt; true</c>) whose
/// <c>CreateAsync</c>/<c>UpdateAsync</c> close <see cref="EmployeeCrud"/> at compile time; row identity
/// comes from the request key and the concurrency token is passed through unchanged (D120).
/// </summary>
public partial class EmployeeView : View<EmployeeRow, EmployeeCrud>
{
    /// <summary>Globally-unique view name; the key the generated invoker is stored under.</summary>
    public const string ViewName = "http-surface-employees";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<EmployeeRow, EmployeeCrud> builder)
    {
        builder
            .Named(ViewName)
            .From<EmployeeEntity>(s => new EmployeeRow
            {
                EmployeeId = s.EmployeeId,
                FullName = s.FullName,
                Title = s.Title,
                ReportsTo = s.ReportsTo,
            })
            .Field(x => x.EmployeeId, f => f.PrimaryKey());

        builder
            .CrudOn<EmployeeEntity>()
            .MapWritable(c => c.FullName, e => e.FullName)
            .MapWritable(c => c.Title, e => e.Title)
            .MapWritable(c => c.ReportsTo, e => e.ReportsTo)
            .WithConcurrencyToken(e => e.Version);
    }
}

/// <summary>
/// Developer-authored, source-generated <c>App_Json_Context</c> for <see cref="EmployeeView"/> — the exact
/// <c>[JsonSerializable]</c> set VISTA0041 names for a WRITABLE view: <c>TRow</c>,
/// <c>ViewListResult&lt;TRow&gt;</c>, <c>PagedResult&lt;TRow&gt;</c>, and <c>TCrud</c>.
/// </summary>
[JsonSerializable(typeof(EmployeeRow))]
[JsonSerializable(typeof(ViewListResult<EmployeeRow>))]
[JsonSerializable(typeof(PagedResult<EmployeeRow>))]
[JsonSerializable(typeof(EmployeeCrud))]
public sealed partial class EmployeeJsonContext : JsonSerializerContext
{
}
