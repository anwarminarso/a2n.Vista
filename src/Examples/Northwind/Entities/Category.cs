namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A product category (a trimmed-down Northwind <c>Categories</c> row). Source entity for the
/// <c>CategoryName</c> column projected by the <c>vProductCategory</c> view.
/// </summary>
public class Category
{
    /// <summary>Primary key.</summary>
    public int CategoryId { get; set; }

    /// <summary>The category's display name (for example "Beverages").</summary>
    public string CategoryName { get; set; } = "";

    /// <summary>A short description of the category.</summary>
    public string? Description { get; set; }

    /// <summary>The products that belong to this category.</summary>
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
