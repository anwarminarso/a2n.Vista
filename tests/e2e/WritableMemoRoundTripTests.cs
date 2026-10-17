using System.Text;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.E2E;

/// <summary>
/// HTTP create/update/delete round trip for <c>vWritableMemo</c>: a quoted strong ETag, token
/// rotation, rejection of the previous token, a legacy unquoted token, and rejection of <c>*</c>.
/// Delete stays 200 until the follow-up contract change.
/// </summary>
public sealed class WritableMemoRoundTripTests
{
    [Test]
    public async Task Create_Update_Delete_Round_Trip()
    {
        var subject = "e2e-memo-" + Guid.NewGuid().ToString("N");
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
                JsonSerializer.Serialize(new { model = new { subject, body = "round trip" } }),
                Encoding.UTF8,
                "application/json"),
        };
        var create = await SendAsync(createRequest).ConfigureAwait(false);
        await Assert.That(create.Status).IsEqualTo(200);
        using var created = create.Json();
        var key = created.RootElement.GetProperty("key").GetInt32();
        var token = create.ETag;
        await Assert.That(token).IsNotNull();
        await Assert.That(token!).StartsWith("\"");
        await Assert.That(token!).EndsWith("\"");

        var detail = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/detail",
            $$"""{"key":{{key}}}""");
        await Assert.That(detail.Status).IsEqualTo(200);
        using var row = detail.Json();
        await Assert.That(row.RootElement.GetProperty("subject").GetString()).IsEqualTo(subject);
        await Assert.That(detail.ETag).IsEqualTo(token);

        var edited = subject + "-edited";
        var update = await UpdateAsync(key, edited, token!).ConfigureAwait(false);
        await Assert.That(update.Status).IsEqualTo(200);
        var nextToken = update.ETag;
        await Assert.That(nextToken).IsNotNull();
        await Assert.That(nextToken!).StartsWith("\"");
        await Assert.That(nextToken).IsNotEqualTo(token);

        var afterUpdate = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/detail",
            $$"""{"key":{{key}}}""");
        await Assert.That(afterUpdate.Status).IsEqualTo(200);
        using var editedJson = afterUpdate.Json();
        await Assert.That(editedJson.RootElement.GetProperty("subject").GetString()).IsEqualTo(edited);

        var stale = await UpdateAsync(key, edited + "-stale", token!).ConfigureAwait(false);
        await Assert.That(stale.Status).IsEqualTo(412);
        using var staleJson = stale.Json();
        await Assert.That(staleJson.RootElement.GetProperty("code").GetString())
            .IsEqualTo("write-concurrency-conflict");
        var stillEdited = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/detail",
            $$"""{"key":{{key}}}""");
        using var stillEditedJson = stillEdited.Json();
        await Assert.That(stillEditedJson.RootElement.GetProperty("subject").GetString()).IsEqualTo(edited);

        var bare = nextToken!.Trim('"');
        var legacy = await UpdateAsync(key, edited + "-legacy", bare).ConfigureAwait(false);
        await Assert.That(legacy.Status).IsEqualTo(200);
        var afterLegacy = legacy.ETag;
        await Assert.That(afterLegacy).IsNotNull();
        await Assert.That(afterLegacy).IsNotEqualTo(nextToken);

        var star = await UpdateAsync(key, edited + "-star", "*").ConfigureAwait(false);
        await Assert.That(star.Status).IsEqualTo(412);
        var afterStar = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/detail",
            $$"""{"key":{{key}}}""");
        using var afterStarJson = afterStar.Json();
        await Assert.That(afterStarJson.RootElement.GetProperty("subject").GetString())
            .IsEqualTo(edited + "-legacy");

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(SampleSession.Northwind, "/api/views/vWritableMemo/delete"))
        {
            Content = new StringContent($$"""{"key":{{key}}}""", Encoding.UTF8, "application/json"),
        };
        deleteRequest.Headers.TryAddWithoutValidation("If-Match", afterLegacy);
        var deleted = await SendAsync(deleteRequest).ConfigureAwait(false);
        await Assert.That(deleted.Status).IsEqualTo(200);

        var gone = await SampleSession.Http.PostJsonAsync(
            SampleSession.Northwind,
            "/api/views/vWritableMemo/detail",
            $$"""{"key":{{key}}}""");
        await Assert.That(gone.Status).IsEqualTo(404);
    }

    private static async Task<VistaResponse> UpdateAsync(int key, string subject, string ifMatch)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(SampleSession.Northwind, "/api/views/vWritableMemo/update"))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    key,
                    model = new { subject, body = "round trip" },
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<VistaResponse> SendAsync(HttpRequestMessage request)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        string? etag = null;
        if (response.Headers.TryGetValues("ETag", out var values))
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
