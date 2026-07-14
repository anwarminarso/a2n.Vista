using a2n.Vista.Authoring;
using Northwind.DataAccess;

namespace a2n.Vista.Examples.AgGridNorthwind.Views;

/// <summary>
/// Central-template (Gaya A / Style A) authoring for the AG Grid Northwind sample. Registers the
/// read-only <c>vProductCategory</c> view — the same anonymous Product → Category/Supplier projection the
/// DataTables Northwind example exposes — so the AG Grid server-side row model has a real view with
/// string, numeric, and foreign-key fields to drive text/number/set filters, multi-sort, and quick-filter
/// search (D136, R7.1).
/// </summary>
/// <remarks>
/// This template is declared locally rather than referencing the sibling DataTables Northwind *example
/// host* project: reusing it would couple two example hosts and drag in unrelated adapters/packages
/// (DataTables, OpenAPI, the source-generator analyzer, TestHost). The projection mirrors the shipped
/// Northwind view so the samples stay behaviourally aligned while this host stays self-contained and
/// Core + EF + AspNetCore + AgGrid only.
/// </remarks>
public class AgGridNorthwindViews : ViewTemplate<NorthwindDbContext>
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
            .Field(x => x.ProductId, f => f.PrimaryKey().Hidden())
            .Field(x => x.CategoryId, f => f.Hidden())
            .Field(x => x.SupplierId, f => f.Hidden());
        // No WithCrud(...) → read-only resource (List + Detail by ProductId).
    }
}
