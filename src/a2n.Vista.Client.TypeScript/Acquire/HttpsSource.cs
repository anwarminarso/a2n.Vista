using System.Net.Http;
using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Acquire;

/// <summary>
/// The HTTPS acquire-stage source (design §A.2, Requirements 1.3, 1.4). Performs a single HTTPS
/// GET for the OpenAPI document with a fixed 30-second budget and never throws for an expected
/// failure: a timeout or a non-success HTTP status is returned as
/// <see cref="AcquireError.Fetch"/> naming the source URL.
/// </summary>
/// <remarks>
/// Only the <c>https</c> scheme is accepted; a non-HTTPS URL is a configuration error rejected at
/// construction. The 30-second budget is enforced with a linked <see cref="CancellationTokenSource"/>
/// rather than <see cref="HttpClient.Timeout"/>, so the budget holds regardless of the underlying
/// client's own timeout and works with an injected stub handler/client in tests (task 3.4).
/// </remarks>
public sealed class HttpsSource : IOpenApiSource, IDisposable
{
    /// <summary>The fixed HTTPS fetch budget (Requirement 1.3).</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private readonly Uri _url;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly TimeSpan _fetchTimeout;

    /// <summary>
    /// Creates a source that fetches <paramref name="url"/> using an internally owned
    /// <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="url">The absolute HTTPS URL of the OpenAPI document.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="url"/> is not an absolute <c>https</c> URL.
    /// </exception>
    public HttpsSource(Uri url)
        : this(url, CreateOwnedClient(), ownsClient: true, FetchTimeout)
    {
    }

    /// <summary>
    /// Creates a source that fetches <paramref name="url"/> through an injected
    /// <see cref="HttpMessageHandler"/>. Useful for driving the source from an in-process stub in
    /// tests. The handler is owned by the created client and disposed with this source.
    /// </summary>
    /// <param name="url">The absolute HTTPS URL of the OpenAPI document.</param>
    /// <param name="handler">The message handler that performs (or stubs) the request.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="url"/> is not an absolute <c>https</c> URL.
    /// </exception>
    public HttpsSource(Uri url, HttpMessageHandler handler)
        : this(url, CreateClient(handler), ownsClient: true, FetchTimeout)
    {
    }

    /// <summary>
    /// Test-only seam: creates a source over an injected <see cref="HttpMessageHandler"/> with an
    /// overridable fetch budget, so the timeout branch can be exercised deterministically without waiting
    /// the full 30 seconds. The public constructors always use the fixed <see cref="FetchTimeout"/>.
    /// </summary>
    /// <param name="url">The absolute HTTPS URL of the OpenAPI document.</param>
    /// <param name="handler">The message handler that performs (or stubs) the request.</param>
    /// <param name="fetchTimeout">The fetch budget to enforce for this instance.</param>
    internal HttpsSource(Uri url, HttpMessageHandler handler, TimeSpan fetchTimeout)
        : this(url, CreateClient(handler), ownsClient: true, fetchTimeout)
    {
    }

    /// <summary>
    /// Creates a source that fetches <paramref name="url"/> through an injected
    /// <see cref="HttpClient"/>. The client's lifetime is owned by the caller and is not disposed
    /// by this source. The 30-second budget is still enforced independently of the client's own
    /// <see cref="HttpClient.Timeout"/>.
    /// </summary>
    /// <param name="url">The absolute HTTPS URL of the OpenAPI document.</param>
    /// <param name="httpClient">The client used to perform the request.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="url"/> is not an absolute <c>https</c> URL.
    /// </exception>
    public HttpsSource(Uri url, HttpClient httpClient)
        : this(url, httpClient, ownsClient: false, FetchTimeout)
    {
    }

    private HttpsSource(Uri url, HttpClient httpClient, bool ownsClient, TimeSpan fetchTimeout)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (!url.IsAbsoluteUri || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The OpenAPI source URL must be an absolute https URL; got '{url}'.",
                nameof(url));
        }

        _url = url;
        _httpClient = httpClient;
        _ownsClient = ownsClient;
        _fetchTimeout = fetchTimeout;
    }

    /// <inheritdoc />
    public async Task<Result<ReadOnlyMemory<byte>, AcquireError>> ReadAsync(CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(_fetchTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            using var response = await _httpClient
                .GetAsync(_url, HttpCompletionOption.ResponseContentRead, linked.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var reason = string.IsNullOrEmpty(response.ReasonPhrase)
                    ? $"HTTP {(int)response.StatusCode}"
                    : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                return Result<ReadOnlyMemory<byte>, AcquireError>.Err(
                    new AcquireError.Fetch(_url.ToString(), $"the server returned a non-success response ({reason})."));
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
            return Result<ReadOnlyMemory<byte>, AcquireError>.Ok(bytes);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Our 30-second budget elapsed (not a caller cancellation): a typed, non-throwing failure.
            return Result<ReadOnlyMemory<byte>, AcquireError>.Err(
                new AcquireError.Fetch(_url.ToString(), "the fetch did not complete within the 30-second timeout."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-initiated cancellation is honored by propagating, not by masquerading as a fetch failure.
            throw;
        }
        catch (HttpRequestException ex)
        {
            return Result<ReadOnlyMemory<byte>, AcquireError>.Err(
                new AcquireError.Fetch(_url.ToString(), ex.Message));
        }
    }

    /// <summary>Releases the internally owned <see cref="HttpClient"/>, if any.</summary>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static HttpClient CreateOwnedClient()
    {
        // Timeout is governed by the linked CancellationTokenSource in ReadAsync, so disable the
        // client's own timeout to keep a single source of truth for the 30-second budget.
        return new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
