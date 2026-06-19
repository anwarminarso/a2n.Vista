namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A product supplier (a trimmed-down Northwind <c>Suppliers</c> row). Source entity for the
/// <c>SupplierName</c> column projected by the <c>vProductCategory</c> view.
/// </summary>
public class Supplier
{
    /// <summary>Primary key.</summary>
    public int SupplierId { get; set; }

    /// <summary>The supplier's company name.</summary>
    public string CompanyName { get; set; } = "";

    /// <summary>The country the supplier ships from.</summary>
    public string? Country { get; set; }

    /// <summary>The products provided by this supplier.</summary>
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
