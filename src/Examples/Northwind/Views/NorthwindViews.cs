using a2n.Vista.Authoring;
using Northwind.DataAccess;

namespace a2n.Vista.Examples.Northwind.Views;

/// <summary>
/// Central-template (Gaya A) authoring for the Northwind sample. Registers the read-only
/// <c>vProductCategory</c> view — the Vista port of DynData's <c>NorthwindQueryTemplate.vProductCategory</c>
/// (docs/spec/01-view.md §6A). Because the projection is anonymous and no Write facet is attached, the
/// view is read-only (List + Detail) — see Requirements R3.1/R3.3/R12.
/// </summary>
public class NorthwindViews : ViewTemplate<NorthwindDbContext>
{
    /// <inheritdoc />
    protected override void Configure(IViewTemplateBuilder<NorthwindDbContext> views)
    {
        // Anonymous projection joining Product → Category/Supplier via navigations.
        //
        // ProductId is marked .PrimaryKey() so it is surfaced into ViewMetadata.KeyFields (Decision Log
        // D104); Detail-by-key and deterministic paging resolve from that key even though the column is
        // Hidden from transport (R12.2).
        views.AddView("vProductCategory", (db, sp) =>
                from p in db.Products
                select new
                {
                    p.ProductId,
                    p.ProductName,
                    p.UnitPrice,
                    p.UnitsInStock,
                    p.Discontinued,
                    p.CategoryId,
                    // INNER JOINs via the (now optional) navigations: EF guarantees a matching row,
                    // so null-forgive to keep these projected columns non-null. CategoryId/SupplierId
                    // are nullable FKs, but the example DB has no orphaned products.
                    CategoryName = p.Category!.CategoryName,
                    p.SupplierId,
                    SupplierName = p.Supplier!.CompanyName,
                })
            // Every projected field is filter/sort/searchable by default (default-allow, D42). Only the
            // technical keys need customizing: mark the PK and hide the key columns from transport.
            // CategoryId is also made Scopable (opt-in, D47) so it can be used as a contextual lookup
            // scope key — the DataTables externalFilter (Scope channel) path exercises this (D111).
            .Field(x => x.ProductId, f => f.PrimaryKey().Hidden())
            .Field(x => x.CategoryId, f => f.Hidden().Scopable())
            .Field(x => x.SupplierId, f => f.Hidden());
        // No WithCrud(...) → read-only resource (List + Detail by ProductId).

        // Composite-key view (Decision Log D104/D109): Order Details is keyed by (OrderId, ProductId).
        // Both key columns are marked .PrimaryKey(), so ViewMetadata.KeyFields = [OrderId, ProductId]
        // (in declaration order) and Detail-by-key resolves a row by the composite key.
        views.AddView("vOrderDetail", (db, sp) =>
                from d in db.OrderDetails
                select new
                {
                    d.OrderId,
                    d.ProductId,
                    ProductName = d.Product!.ProductName,
                    d.UnitPrice,
                    d.Quantity,
                    d.Discount,
                })
            .Field(x => x.OrderId, f => f.PrimaryKey())
            .Field(x => x.ProductId, f => f.PrimaryKey());
        // No WithCrud(...) → read-only resource (List + composite-key Detail).
    }
}
