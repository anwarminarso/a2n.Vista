using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// Keyless projection backing the Northwind <c>"Products Above Average Price"</c> view: products whose
/// unit price exceeds the catalog average.
/// </summary>
public partial class ProductsAboveAveragePrice
{
    public string? ProductName { get; set; }

    public double? UnitPrice { get; set; }
}
