using a2n.Vista.Authoring;
using a2n.Vista.Examples.Northwind.Data;

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
        // ProductId is intentionally the FIRST projected field: Gaya A does not surface the primary key
        // into metadata yet, so Detail-by-key falls back to a name convention that ends at "first
        // projected field". Keeping ProductId first means Detail by ProductId resolves correctly even
        // though the field is Hidden (R12.2).
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
            .Field(x => x.ProductId, f => f.PrimaryKey().Hidden())
            .Field(x => x.CategoryId, f => f.Hidden())
            .Field(x => x.SupplierId, f => f.Hidden());
        // No WithCrud(...) → read-only resource (List + Detail by ProductId).
    }
}
