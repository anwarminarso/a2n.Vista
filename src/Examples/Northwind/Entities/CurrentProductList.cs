using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Current Product List"</c> view: non-discontinued
/// products, ordered by name.
/// </summary>
public partial class CurrentProductList
{
    public int? ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
}
