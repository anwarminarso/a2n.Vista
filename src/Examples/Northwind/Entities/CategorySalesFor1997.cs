using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Category Sales for 1997"</c> view: total product
/// sales aggregated per category for 1997.
/// </summary>
public partial class CategorySalesFor1997
{
    public string CategoryName { get; set; } = string.Empty;

    public byte[]? CategorySales { get; set; }
}
