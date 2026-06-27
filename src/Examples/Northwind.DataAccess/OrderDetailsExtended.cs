using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// Keyless projection backing the Northwind <c>"Order Details Extended"</c> view: order details with
/// the computed extended price (unit price * quantity * (1 - discount)).
/// </summary>
public partial class OrderDetailsExtended
{
    public int? OrderId { get; set; }

    public int? ProductId { get; set; }

    public string? ProductName { get; set; }

    public double? UnitPrice { get; set; }

    public int? Quantity { get; set; }

    public double? Discount { get; set; }

    public double? ExtendedPrice { get; set; }
}
