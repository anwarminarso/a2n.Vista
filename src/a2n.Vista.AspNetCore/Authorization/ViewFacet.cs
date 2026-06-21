namespace a2n.Vista.AspNetCore.Authorization;

/// <summary>
/// Identifies which view facet a request targets, so a single <see cref="IViewAuthorizer"/> can apply
/// different policies for reads versus writes.
/// Authoritative shape: docs/spec/01-view.md §5.6 (Decision Log D43/D48).
/// </summary>
/// <remarks>
/// <para>
/// The facets mirror the authoritative spec exactly (§5.6). Although the conceptual view model groups
/// mutations under a single "Write" facet (see <c>requirements.md</c> Glossary), authorization is
/// deliberately finer-grained: <see cref="Create"/>, <see cref="Update"/> and <see cref="Delete"/> are
/// distinct so an authorizer can, for example, allow updates while denying deletes. <see cref="Export"/>
/// is likewise separate from <see cref="List"/> because bulk export is a higher-risk read.
/// </para>
/// <para>
/// Lives in <c>a2n.Vista.AspNetCore</c> because it is part of the HTTP-bound authorization contract
/// (Requirement R7.5).
/// </para>
/// </remarks>
public enum ViewFacet
{
    /// <summary>Read of many rows (the mandatory query facet). Maps to <c>GET {root}/{viewName}</c>.</summary>
    List,

    /// <summary>Read of a single row by primary key. Maps to <c>GET {root}/{viewName}/{key}</c>.</summary>
    Detail,

    /// <summary>
    /// Read of the view's <see cref="a2n.Vista.Metadata.ViewMetadata"/> shape (fields, key, limits).
    /// Maps to <c>GET {root}/{viewName}/metadata</c> (Decision Log D110).
    /// </summary>
    Metadata,

    /// <summary>Bulk read intended for export. Separated from <see cref="List"/> as a higher-risk read.</summary>
    Export,

    /// <summary>Create a row (typed write). Maps to <c>POST {root}/{viewName}</c>.</summary>
    Create,

    /// <summary>Update an existing row (typed write). Maps to <c>PUT {root}/{viewName}/{key}</c>.</summary>
    Update,

    /// <summary>Delete an existing row. Maps to <c>DELETE {root}/{viewName}/{key}</c>.</summary>
    Delete,
}
