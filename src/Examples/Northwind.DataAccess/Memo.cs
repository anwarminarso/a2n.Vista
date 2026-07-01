using System;

namespace Northwind.DataAccess;

/// <summary>
/// A small, purpose-made writable table used to demonstrate the Vista write path (Create/Update/Delete)
/// end to end. It is deliberately <b>not</b> part of the real Microsoft Northwind schema: the shipped
/// <c>northwind.db</c> is a read-only sample, so the write self-test exercises this isolated table
/// against its own database rather than mutating the sample data.
/// </summary>
/// <remarks>
/// The entity carries the shape a single-source Style B <see cref="a2n.Vista.Authoring.View{TQuery, TCrud}"/>
/// write facet needs: a straightforward primary key (<see cref="MemoId"/>), a couple of scalar columns
/// the whitelist maps (<see cref="Subject"/>, <see cref="Body"/>), and a concurrency token
/// (<see cref="RowVersion"/>) honoured through <c>If-Match</c>/<c>ETag</c>.
/// </remarks>
public partial class Memo
{
    /// <summary>Store-assigned primary key.</summary>
    public int MemoId { get; set; }

    /// <summary>A short subject line — a client-writable scalar field.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The memo body — a client-writable scalar field.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Optimistic-concurrency token. A fresh value is assigned when the entity is created; the write
    /// path compares it against the request's <c>If-Match</c> header and never lets a client assign it
    /// directly (it is excluded from the writable whitelist).
    /// </summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}
