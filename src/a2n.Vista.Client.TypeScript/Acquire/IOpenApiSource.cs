using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Acquire;

/// <summary>
/// The acquire-stage seam (design §A.2). An <see cref="IOpenApiSource"/> reads the raw OpenAPI
/// document bytes from a location (a local file or an HTTPS URL) and returns them, or a typed
/// <see cref="AcquireError"/> for an expected failure. Implementations never throw for expected
/// failures, so the buffered pipeline can route every fatal cause through its single abort path.
/// </summary>
public interface IOpenApiSource
{
    /// <summary>
    /// Reads the raw document bytes.
    /// </summary>
    /// <param name="ct">A token that cancels the read at the caller's request.</param>
    /// <returns>
    /// A success carrying the raw document bytes, or a typed <see cref="AcquireError"/> describing
    /// an expected failure (missing file, fetch timeout, non-success response).
    /// </returns>
    Task<Result<ReadOnlyMemory<byte>, AcquireError>> ReadAsync(CancellationToken ct);
}
