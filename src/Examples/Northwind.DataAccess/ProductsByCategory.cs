using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// Keyless projection backing the Northwind <c>"Products by Category"</c> view: non-discontinued
/// products grouped by category name.
/// </summary>
public partial class ProductsByCategory
{
    public string? CategoryName { get; set; }

    public string? ProductName { get; set; }

    public string? QuantityPerUnit { get; set; }

    public int? UnitsInStock { get; set; }

    public string? Discontinued { get; set; }
}
