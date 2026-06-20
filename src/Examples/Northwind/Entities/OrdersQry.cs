using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>"Orders Qry"</c> view: orders joined with the placing
/// customer's company details.
/// </summary>
public partial class OrdersQry
{
    public int? OrderId { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public int? EmployeeId { get; set; }

    public DateTime? OrderDate { get; set; }

    public DateTime? RequiredDate { get; set; }

    public DateTime? ShippedDate { get; set; }

    public int? ShipVia { get; set; }

    public int? Freight { get; set; }

    public string ShipName { get; set; } = string.Empty;

    public string ShipAddress { get; set; } = string.Empty;

    public string ShipCity { get; set; } = string.Empty;

    public string ShipRegion { get; set; } = string.Empty;

    public string ShipPostalCode { get; set; } = string.Empty;

    public string ShipCountry { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;
}
