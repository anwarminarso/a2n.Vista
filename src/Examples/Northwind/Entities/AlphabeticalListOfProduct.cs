using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Alphabetical list of products"</c> view: active
/// products joined with their category, ordered by name.
/// </summary>
public partial class AlphabeticalListOfProduct
{
    public int? ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int? SupplierId { get; set; }

    public int? CategoryId { get; set; }

    public string QuantityPerUnit { get; set; } = string.Empty;

    public double? UnitPrice { get; set; }

    public int? UnitsInStock { get; set; }

    public int? UnitsOnOrder { get; set; }

    public int? ReorderLevel { get; set; }

    public string Discontinued { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;
}
