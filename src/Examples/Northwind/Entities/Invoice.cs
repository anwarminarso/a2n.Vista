using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// Keyless projection backing the Northwind <c>Invoices</c> view: a denormalized invoice line joining
/// orders, customers, employees, shippers, products, and order details.
/// </summary>
public partial class Invoice
{
    public string ShipName { get; set; } = string.Empty;

    public string ShipAddress { get; set; } = string.Empty;

    public string ShipCity { get; set; } = string.Empty;

    public string ShipRegion { get; set; } = string.Empty;

    public string ShipPostalCode { get; set; } = string.Empty;

    public string ShipCountry { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public int? Salesperson { get; set; }

    public int? OrderId { get; set; }

    public DateTime? OrderDate { get; set; }

    public DateTime? RequiredDate { get; set; }

    public DateTime? ShippedDate { get; set; }

    public string ShipperName { get; set; } = string.Empty;

    public int? ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public double? UnitPrice { get; set; }

    public int? Quantity { get; set; }

    public double? Discount { get; set; }

    public double? ExtendedPrice { get; set; }

    public double? Freight { get; set; }
}
