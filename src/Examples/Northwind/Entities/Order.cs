using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A customer order header. Maps to the Northwind <c>Orders</c> table.
/// </summary>
public partial class Order
{
    public int OrderId { get; set; }

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

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Customer Customer { get; set; } = null!;

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Employee Employee { get; set; } = null!;

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Shipper ShipViaNavigation { get; set; } = null!;
}
