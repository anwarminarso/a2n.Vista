using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Quarterly Orders"</c> view: distinct customers that
/// placed orders in 1997.
/// </summary>
public partial class QuarterlyOrder
{
    public string CustomerId { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;
}
