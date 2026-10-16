using System.Text;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>
/// The HTTP create/update/delete round trip <c>vWritableMemo</c> should support.
/// Skipped: the shipped <c>northwind.db</c> has no <c>VistaMemos</c> table, so the live host
/// returns 409 on create and 500 on list/update/delete. The sample self-test hides this by
/// using an isolated in-memory database. Do not remove the skip until that host can persist a memo.
/// </summary>
public sealed class WritableMemoRoundTripTests
{
    public const string KnownGap =
        "Known failure: vWritableMemo is registered on the live Northwind and showcase hosts, " +
        "but shipped northwind.db has no VistaMemos table. POST list/update/delete return 500 " +
        "(SQLite 'no such table'), and POST create returns 409 write-conflict because every " +
        "DbUpdateException is classified as a constraint conflict (EfViewExecutor.SaveWriteChangesAsync). " +
        "The in-process self-test passes only against an isolated in-memory database. " +
        "This test is the HTTP round trip the sample should provide, including an ETag on create " +
        "so the client can form If-Match (HandleCreateAsync currently returns the key only). " +
        "See docs/qa/e2e-playwright.md.";

    [Test]
    [Skip(KnownGap)]
    public async Task Create_Update_Delete_Round_Trip()
    {
        var list = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/list",
            """{"page":0,"pageSize":10}""");
        await Assert.That(list.Status).IsEqualTo(200);

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(SampleSession.Northwind, "/api/views/vWritableMemo/create"))
        {
            Content = new StringContent(
                """{"model":{"subject":"e2e-memo","body":"round trip"}}""",
                Encoding.UTF8,
                "application/json"),
        };
        var create = await SendAsync(createRequest).ConfigureAwait(false);
        await Assert.That(create.Status).IsEqualTo(200);
        using var created = create.Json();
        var key = created.RootElement.GetProperty("key").GetInt32();
        await Assert.That(create.ETag).IsNotNull();
        var token = create.ETag!;

        var detail = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/detail",
            $$"""{"key":{{key}}}""");
        await Assert.That(detail.Status).IsEqualTo(200);
        using var row = detail.Json();
        await Assert.That(row.RootElement.GetProperty("subject").GetString()).IsEqualTo("e2e-memo");

        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(SampleSession.Northwind, "/api/views/vWritableMemo/update"))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    key,
                    model = new { subject = "e2e-memo-edited", body = "round trip" },
                }),
                Encoding.UTF8,
                "application/json"),
        };
        updateRequest.Headers.TryAddWithoutValidation("If-Match", token);
        var update = await SendAsync(updateRequest).ConfigureAwait(false);
        await Assert.That(update.Status).IsEqualTo(200);
        var nextToken = string.IsNullOrWhiteSpace(update.ETag) ? token : update.ETag!;

        var edited = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/detail",
            $$"""{"key":{{key}}}""");
        await Assert.That(edited.Status).IsEqualTo(200);
        using var editedJson = edited.Json();
        await Assert.That(editedJson.RootElement.GetProperty("subject").GetString()).IsEqualTo("e2e-memo-edited");

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(SampleSession.Northwind, "/api/views/vWritableMemo/delete"))
        {
            Content = new StringContent($$"""{"key":{{key}}}""", Encoding.UTF8, "application/json"),
        };
        deleteRequest.Headers.TryAddWithoutValidation("If-Match", nextToken);
        var deleted = await SendAsync(deleteRequest).ConfigureAwait(false);
        await Assert.That(deleted.Status).IsEqualTo(200);

        var gone = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/detail",
            $$"""{"key":{{key}}}""");
        await Assert.That(gone.Status).IsEqualTo(404);
    }

    private static async Task<VistaResponse> SendAsync(HttpRequestMessage request)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        string? etag = response.Headers.ETag?.Tag;
        if (etag is null && response.Headers.TryGetValues("ETag", out var values))
        {
            etag = string.Concat(values);
        }

        return new VistaResponse(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            bytes,
            etag);
    }
}
