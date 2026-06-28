using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using a2n.Vista.Adapters;
using Microsoft.AspNetCore.Http;

namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// Builds the neutral <see cref="AdapterRequest"/> from an <see cref="HttpContext"/> (Decision Log D112):
/// it merges the query string and any form-urlencoded body into the values bag (the default DataTables
/// server-side transport) and captures the raw body when the content type is JSON (the DataTables
/// <c>ajax</c> JSON variant). This is the only place the adapter pipeline touches ASP.NET types; the
/// adapter itself stays Core-only.
/// </summary>
public static class AdapterRequestFactory
{
    /// <summary>
    /// Creates an <see cref="AdapterRequest"/> for <paramref name="viewName"/> from the current request.
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <param name="viewName">The registered view name the request targets.</param>
    /// <returns>The neutral request bag.</returns>
    public static async Task<AdapterRequest> CreateAsync(HttpContext http, string viewName)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in http.Request.Query)
        {
            values[pair.Key] = ToList(pair.Value);
        }

        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
            foreach (var pair in form)
            {
                // Form values win over query values for the same key (the body is the richer source).
                values[pair.Key] = ToList(pair.Value);
            }
        }

        string? jsonBody = null;
        if (IsJson(http.Request.ContentType))
        {
            using var reader = new StreamReader(http.Request.Body);
            jsonBody = await reader.ReadToEndAsync(http.RequestAborted).ConfigureAwait(false);
        }

        return new AdapterRequest(viewName, values, jsonBody);
    }

    private static List<string> ToList(Microsoft.Extensions.Primitives.StringValues values)
    {
        var list = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (value is not null)
            {
                list.Add(value);
            }
        }

        return list;
    }

    private static bool IsJson(string? contentType) =>
        contentType is not null && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
}
