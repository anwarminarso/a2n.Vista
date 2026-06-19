using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Ports;

/// <summary>
/// The single execution path for a View's three facets (List, Detail, Write). This port lives in
/// Core (HTTP/EF-neutral) and is implemented by <c>a2n.Vista.EntityFrameworkCore</c>, so Core stays
/// free of any EF/ASP.NET dependency (Requirement R11.1/R11.2, Decision Log D48).
/// Authoritative behavior: docs/spec/01-view.md §4.6 (facets), §8.3 (enforcement),
/// §10 (paging), §12.3 (endpoint mapping).
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no public <c>IQueryable</c> extension for materialization, paging, or CRUD
/// (§10.2). Routing every read and write through this port guarantees that whitelist validation,
/// authorization scope, and hard limits cannot be bypassed.
/// </para>
/// <para>
/// <b>Enforcement contract (R9.2, §8.3).</b> Before building any expression, the implementation MUST
/// validate every <see cref="FilterLeaf"/> in <see cref="ViewQueryRequest.Filter"/> against the
/// whitelist for its <see cref="FilterOrigin"/>: <c>Filter</c> leaves must target a filterable field
/// and use an operator within that field's <see cref="FieldMetadata.AllowedOperators"/>; <c>Search</c>
/// leaves must target a searchable string field; <c>Scope</c> leaves must target a <c>Scopable</c>
/// field. A violation surfaces as an invalid-request error (mapped to HTTP 400 by the AspNetCore
/// layer). Server-trusted predicates supplied through <paramref name="scope"/> are AND-ed in and are
/// <b>not</b> subject to this validation (R6.3).
/// </para>
/// <para>
/// <b>Async &amp; cancellation (R10.2).</b> All members are asynchronous and honor the supplied
/// <see cref="CancellationToken"/>.
/// </para>
/// <para>
/// <b>AOT hygiene (R11.4).</b> Members are marked <see cref="RequiresUnreferencedCodeAttribute"/>
/// because the current implementation drives sorting, filtering, projection, and key resolution from
/// string/metadata at runtime (a reflection path). The AOT-clean route is the source generator
/// (Pilar 3); these annotations are removed once it lands.
/// </para>
/// </remarks>
public interface IViewExecutor
{
    /// <summary>
    /// Executes the List facet: applies the server-trusted scope, validates and applies the client
    /// filter/search, sorting, and paging, then materializes one page. Returns both the filtered
    /// page total and the unfiltered total (R10.4).
    /// </summary>
    /// <typeparam name="TRow">The projected (read) row type of the view.</typeparam>
    /// <param name="view">The metadata of the view to execute.</param>
    /// <param name="request">
    /// The neutral query request. <see cref="ViewQueryRequest.PageSize"/> is clamped to the view's
    /// <see cref="HardLimits"/>; "return all" requests (e.g. <c>length=-1</c>) are rejected (R10.3).
    /// </param>
    /// <param name="scope">The server-trusted row-filter scope to AND into the query (may be empty).</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A <see cref="ViewListResult{TRow}"/> whose <see cref="ViewListResult{TRow}.Page"/> carries the
    /// filtered total and whose <see cref="ViewListResult{TRow}.TotalRowsUnfiltered"/> carries the
    /// scope-only total.
    /// </returns>
    [RequiresUnreferencedCode("View execution resolves sort/filter/projection from metadata at runtime; use the source generator path for AOT.")]
    Task<ViewListResult<TRow>> ListAsync<TRow>(
        ViewMetadata view,
        ViewQueryRequest request,
        IViewScope scope,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes the Detail facet: reads a single row by its primary key, with the server-trusted
    /// scope applied. When no dedicated Detail projection is declared, the List projection filtered
    /// by primary key is used (Decision Log D49, §4.6).
    /// </summary>
    /// <typeparam name="TRow">The projected (read) row type of the view.</typeparam>
    /// <param name="view">The metadata of the view to execute.</param>
    /// <param name="key">
    /// The primary-key value identifying the row. Converted to the key field's CLR type by the
    /// implementation.
    /// </param>
    /// <param name="scope">The server-trusted row-filter scope to AND into the query (may be empty).</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// The projected row, or <see langword="null"/> when no row matches the key within the
    /// authorized scope.
    /// </returns>
    [RequiresUnreferencedCode("Detail key resolution and projection are built from metadata at runtime; use the source generator path for AOT.")]
    Task<TRow?> DetailAsync<TRow>(
        ViewMetadata view,
        object key,
        IViewScope scope,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes the Create facet (Write): inserts a new row from a typed write model, applying only
    /// the fields whitelisted via <c>MapWritable</c>. Write is typed-only and never uses an anonymous
    /// projection (R3.2, §4.6).
    /// </summary>
    /// <typeparam name="TCrud">The typed write contract for the view.</typeparam>
    /// <param name="view">The metadata of the view to execute. Must not be read-only (R3.3).</param>
    /// <param name="model">The write model carrying the values to persist.</param>
    /// <param name="scope">The server-trusted row-filter scope (e.g. tenant ownership) to honor.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The primary-key value of the newly created row.</returns>
    [RequiresUnreferencedCode("Write mapping (TCrud to entity) is resolved from metadata at runtime; use the source generator path for AOT.")]
    Task<object> CreateAsync<TCrud>(
        ViewMetadata view,
        TCrud model,
        IViewScope scope,
        CancellationToken cancellationToken)
        where TCrud : class;

    /// <summary>
    /// Executes the Update facet (Write): updates an existing row identified by <paramref name="key"/>
    /// using the whitelisted fields of <paramref name="model"/>.
    /// </summary>
    /// <typeparam name="TCrud">The typed write contract for the view.</typeparam>
    /// <param name="view">The metadata of the view to execute. Must not be read-only (R3.3).</param>
    /// <param name="key">The primary-key value identifying the row to update.</param>
    /// <param name="model">The write model carrying the new values.</param>
    /// <param name="scope">The server-trusted row-filter scope to honor.</param>
    /// <param name="concurrencyToken">
    /// Optional optimistic-concurrency token (HTTP <c>If-Match</c>); <see langword="null"/> when the
    /// view declares no concurrency token. A mismatch surfaces as a concurrency conflict.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when a row was updated; <see langword="false"/> when no row matched the
    /// key within the authorized scope.
    /// </returns>
    [RequiresUnreferencedCode("Write mapping (TCrud to entity) is resolved from metadata at runtime; use the source generator path for AOT.")]
    Task<bool> UpdateAsync<TCrud>(
        ViewMetadata view,
        object key,
        TCrud model,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken)
        where TCrud : class;

    /// <summary>
    /// Executes the Delete facet (Write): deletes the row identified by <paramref name="key"/>.
    /// </summary>
    /// <param name="view">The metadata of the view to execute. Must not be read-only (R3.3).</param>
    /// <param name="key">The primary-key value identifying the row to delete.</param>
    /// <param name="scope">The server-trusted row-filter scope to honor.</param>
    /// <param name="concurrencyToken">
    /// Optional optimistic-concurrency token (HTTP <c>If-Match</c>); <see langword="null"/> when the
    /// view declares no concurrency token. A mismatch surfaces as a concurrency conflict.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when a row was deleted; <see langword="false"/> when no row matched the
    /// key within the authorized scope.
    /// </returns>
    [RequiresUnreferencedCode("Delete key resolution is built from metadata at runtime; use the source generator path for AOT.")]
    Task<bool> DeleteAsync(
        ViewMetadata view,
        object key,
        IViewScope scope,
        string? concurrencyToken,
        CancellationToken cancellationToken);
}
