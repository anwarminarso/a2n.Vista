using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A sales region. Maps to the Northwind <c>Region</c> table.
/// </summary>
public partial class Region
{
    public int RegionId { get; set; }

    public string RegionDescription { get; set; } = string.Empty;

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Territory> Territories { get; set; } = new List<Territory>();
}
