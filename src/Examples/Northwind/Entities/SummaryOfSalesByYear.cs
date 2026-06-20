using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Summary of Sales by Year"</c> view: shipped order
/// subtotals grouped by year.
/// </summary>
public partial class SummaryOfSalesByYear
{
    public DateTime? ShippedDate { get; set; }

    public int? OrderId { get; set; }

    public double? Subtotal { get; set; }
}
