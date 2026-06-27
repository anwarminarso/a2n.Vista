using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// A shipping company. Maps to the Northwind <c>Shippers</c> table.
/// </summary>
public partial class Shipper
{
    public int ShipperId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
