// Licensed to the a2n.Vista project. Published artifact — English only.

namespace a2n.Vista.Write;

/// <summary>
/// A per-request sink through which the execution layer publishes the <b>post-write</b> optimistic-concurrency
/// token, so the HTTP layer can echo the token the row actually carries after a successful write
/// (Decision Log D146).
/// </summary>
/// <remarks>
/// <para>
/// The <c>IViewExecutor</c> update/delete facet reports success as a <see cref="bool"/>, so the endpoint had no
/// post-write token to report and echoed the request's own <c>If-Match</c> value back as the <c>ETag</c>. For a
/// store-generated <c>rowversion</c> that value is stale the moment it is sent, which makes the client's next
/// update <em>guaranteed</em> to conflict (409) — the response actively misinformed the client.
/// </para>
/// <para>
/// This seam fixes that without widening the executor port (and therefore without touching the generated
/// dispatch invoker): the writer sets the token it read back after <c>SaveChanges</c>, and the mapper reads it
/// when composing the success response. It lives in Core so the EF layer and the AspNetCore layer can meet
/// behind it without referencing each other, and it is registered <b>scoped</b> — one instance per request,
/// never shared across requests, and not designed for concurrent use.
/// </para>
/// </remarks>
public interface IWriteTokenSink
{
    /// <summary>
    /// The token read back after the last successful write in this request, or <see langword="null"/> when no
    /// write published one (a tokenless view, a delete, or a failed write).
    /// </summary>
    string? PostWriteToken { get; }

    /// <summary>Publishes the post-write token for this request.</summary>
    /// <param name="token">The formatted token, or <see langword="null"/> to clear it.</param>
    void SetPostWriteToken(string? token);

    /// <summary>
    /// The row produced by the scoped detail read after a successful update, or <see langword="null"/>
    /// when that read returned no row, and for create, delete, and a failed write. The instance is the
    /// view's query type, run through the same read plan and masks as <c>GET</c> detail.
    /// </summary>
    object? PostWriteRow { get; }

    /// <summary>Publishes the detail row for this request.</summary>
    /// <param name="row">The row, or <see langword="null"/> to clear it.</param>
    void SetPostWriteRow(object? row);

    /// <summary>
    /// <see langword="true"/> when the update saved and the following scoped detail read could not see
    /// the row. The HTTP layer answers <c>204</c> with no body and no <c>ETag</c>.
    /// </summary>
    bool PostWriteNotVisible { get; }

    /// <summary>Marks this request's update as saved but no longer visible to the caller.</summary>
    void SetPostWriteNotVisible();
}

/// <summary>
/// Default request-scoped <see cref="IWriteTokenSink"/>: a single nullable slot, last write wins.
/// </summary>
public sealed class WriteTokenSink : IWriteTokenSink
{
    /// <inheritdoc />
    public string? PostWriteToken { get; private set; }

    /// <inheritdoc />
    public void SetPostWriteToken(string? token) => PostWriteToken = token;

    /// <inheritdoc />
    public object? PostWriteRow { get; private set; }

    /// <inheritdoc />
    public void SetPostWriteRow(object? row) => PostWriteRow = row;

    /// <inheritdoc />
    public bool PostWriteNotVisible { get; private set; }

    /// <inheritdoc />
    public void SetPostWriteNotVisible() => PostWriteNotVisible = true;
}
