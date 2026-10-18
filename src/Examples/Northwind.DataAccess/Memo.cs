using System;

namespace Northwind.DataAccess;

/// <summary>
/// A small, purpose-made writable table used to demonstrate the Vista write path (Create/Update/Delete)
/// end to end. It is deliberately <b>not</b> part of the real Microsoft Northwind schema. Hosts copy
/// the extracted template and create <c>VistaMemos</c> on that copy. The template is never written.
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

    /// <summary>A short subject line. The EF model caps it at 120 characters.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The memo body. The EF model caps it at 4000 characters.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Optimistic-concurrency token. The constructor assigns the insert value. Every successful Vista
    /// update replaces it with a new <see cref="Guid"/> before save, and EF puts the previous value in
    /// the <c>UPDATE</c>/<c>DELETE</c> <c>WHERE</c> clause. Clients never assign it (it is excluded from
    /// the writable whitelist). SQLite does not generate this column.
    /// </summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}
