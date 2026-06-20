using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A sales territory within a <see cref="Region"/>. Maps to the Northwind <c>Territories</c> table.
/// </summary>
public partial class Territory
{
    public string TerritoryId { get; set; } = string.Empty;

    public string TerritoryDescription { get; set; } = string.Empty;

    public int RegionId { get; set; }

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Region Region { get; set; } = null!;

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
