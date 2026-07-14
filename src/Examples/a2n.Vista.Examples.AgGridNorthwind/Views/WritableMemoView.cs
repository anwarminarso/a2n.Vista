using a2n.Vista.Authoring;
using Northwind.DataAccess;

namespace a2n.Vista.Examples.AgGridNorthwind.Views;

/// <summary>
/// Class-per-view (Style B) <b>writable</b> view for the Vista write-path demo (Requirement R16.4). It
/// projects a single source entity (<see cref="Memo"/>) for reads and declares a typed write facet with
/// an explicit <c>MapWritable</c> whitelist and an optimistic-concurrency token, so the Create/Update/
/// Delete endpoints are enabled for it (<see cref="a2n.Vista.Metadata.ViewMetadata.IsReadOnly"/> is
/// <see langword="false"/>).
/// </summary>
/// <remarks>
/// The write contract (<see cref="MemoWriteModel"/>) is a strongly-typed DTO, never the entity itself —
/// this is what closes mass-assignment by design (Decision Log D38). Only the two whitelisted scalar
/// fields (<c>Subject</c>, <c>Body</c>) can be written; the key (<c>MemoId</c>) and the concurrency
/// token (<c>RowVersion</c>) are protected — the build-time authoring guard rejects mapping either.
/// </remarks>
public partial class WritableMemoView : View<MemoRow, MemoWriteModel>
{
    /// <summary>The globally-unique view name; the write routes are exposed under its composed route.</summary>
    public const string ViewName = "vWritableMemo";

    /// <inheritdoc />
    protected override void Configure(IViewBuilder<MemoRow, MemoWriteModel> builder)
    {
        // Single-source read projection over the Memo entity (member-initialization so filter/sort push
        // down to SQL); MemoId is the primary key used for Detail-by-key and to resolve the target row on
        // update/delete.
        builder
            .Named(ViewName)
            .From<Memo>(m => new MemoRow
            {
                MemoId = m.MemoId,
                Subject = m.Subject,
                Body = m.Body,
            })
            .Field(x => x.MemoId, f => f.PrimaryKey());

        // Typed write facet: default-deny whitelist mapping the write DTO to entity scalars, plus the
        // concurrency token honoured through If-Match/ETag. Neither the key nor the token may be
        // whitelisted (enforced at build/startup). Declared on the write-capable builder directly, since
        // the read-side fluent methods above narrow to the read-only IViewBuilder<TQuery>.
        builder
            .CrudOn<Memo>()
                .MapWritable(c => c.Subject, e => e.Subject)
                .MapWritable(c => c.Body, e => e.Body)
                .WithConcurrencyToken(e => e.RowVersion);
    }
}

/// <summary>Projected (read) row returned to clients for <see cref="WritableMemoView"/>.</summary>
public sealed class MemoRow
{
    /// <summary>Primary key.</summary>
    public int MemoId { get; init; }

    /// <summary>Subject line.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Memo body.</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// Typed write contract (<c>TCrud</c>) a client posts to create or update a <see cref="Memo"/>. It
/// exposes only the whitelisted, client-writable fields — no key and no concurrency token — so a client
/// can never assign protected members through the body.
/// </summary>
public sealed class MemoWriteModel
{
    /// <summary>The subject to write.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>The body to write.</summary>
    public string Body { get; init; } = string.Empty;
}
