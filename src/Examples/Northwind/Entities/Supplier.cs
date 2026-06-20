using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A company that supplies products. Maps to the Northwind <c>Suppliers</c> table; source of the
/// <c>SupplierName</c> column projected by the <c>vProductCategory</c> view.
/// </summary>
public partial class Supplier
{
    public int SupplierId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? ContactName { get; set; }

    public string? ContactTitle { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? Region { get; set; }

    public string? PostalCode { get; set; }

    public string? Country { get; set; }

    public string? Phone { get; set; }

    public string? Fax { get; set; }

    public string? HomePage { get; set; }

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
