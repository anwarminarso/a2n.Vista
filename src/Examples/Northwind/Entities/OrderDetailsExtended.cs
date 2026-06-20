using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Order Details Extended"</c> view: order details with
/// the computed extended price (unit price * quantity * (1 - discount)).
/// </summary>
public partial class OrderDetailsExtended
{
    public int? OrderId { get; set; }

    public int? ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public double? UnitPrice { get; set; }

    public int? Quantity { get; set; }

    public double? Discount { get; set; }

    public double? ExtendedPrice { get; set; }
}
