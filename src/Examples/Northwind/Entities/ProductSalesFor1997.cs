using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Product Sales for 1997"</c> view: product sales for
/// 1997 grouped by category and product.
/// </summary>
public partial class ProductSalesFor1997
{
    public string? CategoryName { get; set; }

    public string? ProductName { get; set; }

    public byte[]? ProductSales { get; set; }
}
