using System.Net;
using System.Reflection;
using System.Text;
using a2n.Vista.Client.TypeScript.Acquire;
using a2n.Vista.Client.TypeScript.Pipeline;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Integration tests for <see cref="HttpsSource"/> (task 3.4; Requirements 1.3, 1.4). They drive the
/// source through an in-process <see cref="HttpMessageHandler"/> stub injected via the
/// <c>HttpsSource(Uri, HttpMessageHandler)</c> constructor, so every outcome is deterministic and fast
/// (no real socket, no real 30-second wait).
///
/// The three acquire outcomes are pinned:
/// <list type="bullet">
///   <item>a successful fetch yields <c>Ok</c> carrying the exact response bytes;</item>
///   <item>a non-success HTTP status yields <see cref="AcquireError.Fetch"/> naming the source URL and the status;</item>
///   <item>the internal 30-second budget elapsing yields <see cref="AcquireError.Fetch"/> naming the URL and a timeout detail.</item>
/// </list>
/// A non-HTTPS URL is a configuration error rejected at construction, and a caller-initiated cancellation
/// is propagated (never masqueraded as a fetch failure) — the distinction that makes the timeout test meaningful.
///
/// The 30-second-budget branch is exercised deterministically (in milliseconds) by constructing the source
/// through its test-only internal constructor — which takes an overridable fetch budget — via reflection,
/// matching the repo's no-<c>InternalsVisibleTo</c> convention. The public constructors keep the fixed budget.
/// </summary>
public sealed class HttpsSourceTests
{
    private static readonly Uri SourceUrl = new("https://example.com/openapi/v1.json");

    /// <summary>An in-process message handler that returns whatever the supplied delegate produces.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _respond(request, cancellationToken);
    }

    [Test]
    public async Task Successful_Fetch_Yields_Ok_With_Exact_Bytes()
    {
        var payload = Encoding.UTF8.GetBytes("{\"openapi\":\"3.0.4\"}");
        HttpMethod? observedMethod = null;
        Uri? observedUri = null;
        var handler = new StubHandler((request, _) =>
        {
            observedMethod = request.Method;
            observedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        });

        using var source = new HttpsSource(SourceUrl, handler);

        var result = await source.ReadAsync(CancellationToken.None);

        await Assert.That(result.IsOk).IsTrue();
        await Assert.That(result.Value.ToArray()).IsEquivalentTo(payload);
        // The source must issue a GET at exactly the configured URL.
        await Assert.That(observedMethod).IsEqualTo(HttpMethod.Get);
        await Assert.That(observedUri).IsEqualTo(SourceUrl);
    }

    [Test]
    [Arguments(HttpStatusCode.NotFound, "404")]
    [Arguments(HttpStatusCode.InternalServerError, "500")]
    public async Task NonSuccess_Response_Yields_Fetch_Error_Identifying_Url_And_Status(
        HttpStatusCode status,
        string expectedStatusText)
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status)));

        using var source = new HttpsSource(SourceUrl, handler);

        var result = await source.ReadAsync(CancellationToken.None);

        await Assert.That(result.IsError).IsTrue();
        var fetch = result.Error as AcquireError.Fetch;
        await Assert.That(fetch).IsNotNull();
        await Assert.That(fetch!.Url).IsEqualTo(SourceUrl.ToString());
        // The detail must name the offending status code so the failure is actionable.
        await Assert.That(fetch.Detail).Contains(expectedStatusText);
    }

    /// <summary>
    /// Constructs an <see cref="HttpsSource"/> through its test-only internal constructor (the one that
    /// takes an overridable fetch budget) via reflection, matching the repo's no-<c>InternalsVisibleTo</c>
    /// convention. This lets the genuine 30-second-budget branch be exercised in milliseconds.
    /// </summary>
    private static HttpsSource CreateWithBudget(Uri url, HttpMessageHandler handler, TimeSpan budget)
    {
        var ctor = typeof(HttpsSource).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            new[] { typeof(Uri), typeof(HttpMessageHandler), typeof(TimeSpan) },
            modifiers: null)
            ?? throw new InvalidOperationException("The internal HttpsSource(Uri, HttpMessageHandler, TimeSpan) constructor was not found.");

        return (HttpsSource)ctor.Invoke(new object[] { url, handler, budget });
    }

    [Test]
    public async Task Elapsed_Fetch_Budget_Yields_Fetch_Error_With_Timeout_Detail()
    {
        // The handler honors the linked token: it never completes on its own, so only the internal budget
        // elapsing can end the request — driving the genuine timeout branch deterministically and fast.
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK); // unreachable
        });

        using var source = CreateWithBudget(SourceUrl, handler, TimeSpan.FromMilliseconds(50));

        var result = await source.ReadAsync(CancellationToken.None);

        await Assert.That(result.IsError).IsTrue();
        var fetch = result.Error as AcquireError.Fetch;
        await Assert.That(fetch).IsNotNull();
        await Assert.That(fetch!.Url).IsEqualTo(SourceUrl.ToString());
        await Assert.That(fetch.Detail).Contains("timeout");
    }

    [Test]
    public async Task Caller_Cancellation_Is_Propagated_Not_Masqueraded_As_A_Fetch_Failure()
    {
        // A handler that blocks on the token until the caller cancels; caller cancellation must surface as
        // an OperationCanceledException, never as a typed Fetch failure (that is reserved for the budget).
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK); // unreachable
        });

        using var source = new HttpsSource(SourceUrl, handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await source.ReadAsync(cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task NonHttps_Url_Is_Rejected_At_Construction()
    {
        var httpUrl = new Uri("http://example.com/openapi/v1.json");

        await Assert.That(() => new HttpsSource(httpUrl))
            .Throws<ArgumentException>();
    }
}
