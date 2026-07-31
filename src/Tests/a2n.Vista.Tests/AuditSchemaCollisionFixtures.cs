// Licensed to the a2n.Vista project. Published artifact — English only.

// Two row types that deliberately share the simple name `OrderRow` in different namespaces, so the OpenAPI
// component-naming guard (audit BUG-08) can be exercised: keyed by simple name they collapsed onto one
// component and the second view's operations documented the first view's shape.

namespace a2n.Vista.Tests.AuditFixtures.Sales
{
    /// <summary>The sales-side order row (same simple name as the purchasing-side one).</summary>
    internal sealed class OrderRow
    {
        public int Id { get; init; }

        public int CustomerId { get; init; }
    }
}

namespace a2n.Vista.Tests.AuditFixtures.Purchasing
{
    /// <summary>The purchasing-side order row (same simple name as the sales-side one).</summary>
    internal sealed class OrderRow
    {
        public int Id { get; init; }

        public int SupplierId { get; init; }
    }
}
