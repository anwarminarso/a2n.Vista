namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A product (a trimmed-down Northwind <c>Products</c> row). This is the root source entity for the
/// <c>vProductCategory</c> view; the view projects it joined with its <see cref="Category"/> and
/// <see cref="Supplier"/>.
/// </summary>
public class Product
{
    /// <summary>Primary key. Surfaced (hidden) in the view so Detail-by-key can resolve a row.</summary>
    public int ProductId { get; set; }

    /// <summary>The product's display name.</summary>
    public string ProductName { get; set; } = "";

    /// <summary>The unit price, or <see langword="null"/> when not priced.</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>The number of units currently in stock.</summary>
    public short UnitsInStock { get; set; }

    /// <summary>Whether the product has been discontinued.</summary>
    public bool Discontinued { get; set; }

    /// <summary>Foreign key to the owning <see cref="Category"/>.</summary>
    public int CategoryId { get; set; }

    /// <summary>The owning category navigation.</summary>
    public Category Category { get; set; } = null!;

    /// <summary>Foreign key to the providing <see cref="Supplier"/>.</summary>
    public int SupplierId { get; set; }

    /// <summary>The providing supplier navigation.</summary>
    public Supplier Supplier { get; set; } = null!;
}
