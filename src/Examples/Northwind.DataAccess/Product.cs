using System;
using System.Collections.Generic;

namespace Northwind.DataAccess;

/// <summary>
/// A product offered for sale. Maps to the Northwind <c>Products</c> table and is the root source
/// entity for the <c>vProductCategory</c> view (joined with <see cref="Category"/> and
/// <see cref="Supplier"/>).
/// </summary>
public partial class Product
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int? SupplierId { get; set; }

    public int? CategoryId { get; set; }

    public string? QuantityPerUnit { get; set; }

    public double? UnitPrice { get; set; }

    public int? UnitsInStock { get; set; }

    public int? UnitsOnOrder { get; set; }

    public int? ReorderLevel { get; set; }

    public string Discontinued { get; set; } = string.Empty;

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Category? Category { get; set; }

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Supplier? Supplier { get; set; }
}
