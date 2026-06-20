using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Customer and Suppliers by City"</c> view: customers
/// and suppliers unioned and grouped by city.
/// </summary>
public partial class CustomerAndSuppliersByCity
{
    public string City { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string ContactName { get; set; } = string.Empty;

    public string Relationship { get; set; } = string.Empty;
}
