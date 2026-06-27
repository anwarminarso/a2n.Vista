using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// Keyless projection backing the Northwind <c>"Current Product List"</c> view: non-discontinued
/// products, ordered by name.
/// </summary>
public partial class CurrentProductList
{
    public int? ProductId { get; set; }

    public string? ProductName { get; set; }
}
