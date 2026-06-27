using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// Keyless projection backing the Northwind <c>"Customer and Suppliers by City"</c> view: customers
/// and suppliers unioned and grouped by city.
/// </summary>
public partial class CustomerAndSuppliersByCity
{
    public string? City { get; set; }

    public string? CompanyName { get; set; }

    public string? ContactName { get; set; }

    public string? Relationship { get; set; }
}
