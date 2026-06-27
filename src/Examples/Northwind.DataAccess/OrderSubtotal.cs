using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// Keyless projection backing the Northwind <c>"Order Subtotals"</c> view: the extended-price subtotal
/// per order.
/// </summary>
public partial class OrderSubtotal
{
    public int? OrderId { get; set; }

    public double? Subtotal { get; set; }
}
