using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// A line item on an order. Maps to the Northwind <c>"Order Details"</c> table; keyed by the
/// (<see cref="OrderId"/>, <see cref="ProductId"/>) composite.
/// </summary>
public partial class OrderDetail
{
    public int OrderId { get; set; }

    public int ProductId { get; set; }

    public double UnitPrice { get; set; }

    public int Quantity { get; set; }

    public double Discount { get; set; }

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Order Order { get; set; } = null!;

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Product Product { get; set; } = null!;
}
