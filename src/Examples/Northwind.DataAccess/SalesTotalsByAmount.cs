using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// Keyless projection backing the Northwind <c>"Sales Totals by Amount"</c> view: order subtotals
/// above a threshold, with the placing company and ship date.
/// </summary>
public partial class SalesTotalsByAmount
{
    public byte[]? SaleAmount { get; set; }

    public int? OrderId { get; set; }

    public string? CompanyName { get; set; }

    public DateTime? ShippedDate { get; set; }
}
