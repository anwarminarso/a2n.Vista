using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A customer demographic grouping. Maps to the Northwind <c>CustomerDemographics</c> table.
/// </summary>
public partial class CustomerDemographic
{
    public string CustomerTypeId { get; set; } = string.Empty;

    public string? CustomerDesc { get; set; }

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();
}
