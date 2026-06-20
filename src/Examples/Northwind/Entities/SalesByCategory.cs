using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Sales by Category"</c> view: 1997 product sales
/// grouped by category.
/// </summary>
public partial class SalesByCategory
{
    public int? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public string? ProductName { get; set; }

    public byte[]? ProductSales { get; set; }
}
