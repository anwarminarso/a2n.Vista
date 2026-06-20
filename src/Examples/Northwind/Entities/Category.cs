using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A product category. Maps to the Northwind <c>Categories</c> table; source of the
/// <c>CategoryName</c> column projected by the <c>vProductCategory</c> view.
/// </summary>
public partial class Category
{
    public int CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public string? Description { get; set; }

    public byte[]? Picture { get; set; }

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
