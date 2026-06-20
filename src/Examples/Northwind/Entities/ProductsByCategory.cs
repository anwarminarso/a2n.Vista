using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Products by Category"</c> view: non-discontinued
/// products grouped by category name.
/// </summary>
public partial class ProductsByCategory
{
    public string CategoryName { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string QuantityPerUnit { get; set; } = string.Empty;

    public int? UnitsInStock { get; set; }

    public string Discontinued { get; set; } = string.Empty;
}
