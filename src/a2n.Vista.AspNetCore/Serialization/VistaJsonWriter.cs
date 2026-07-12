using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace a2n.Vista.AspNetCore.Serialization;

/// <summary>
/// The shared serialization writer for Vista's HTTP responses (Decision Log D124). It resolves a
/// value's runtime <see cref="JsonTypeInfo"/> through the serialization seam
/// (<see cref="VistaJson.Options"/> and its <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>)
/// and serializes with the AOT-safe <see cref="JsonTypeInfo"/> overloads — never the reflection
/// <c>Serialize(object, Type, options)</c> overload.
/// </summary>
/// <remarks>
/// <para>
/// Because it keeps the exact <see cref="VistaJson.Options"/> configuration (web defaults,
/// case-insensitive matching, <c>JsonStringEnumConverter</c>, <see cref="FilterNodeJsonConverter"/>),
/// the output is byte-for-byte identical to the previous framework path for the same value — only the
/// <see cref="JsonTypeInfo"/> resolution mechanism changes. When a chained source-generated context
/// covers the runtime type, serialization is AOT-clean; otherwise it rides the reflection fallback
/// resolver (unless it was opted out via <see cref="VistaJson.DisableReflectionFallback"/>).
/// </para>
/// <para>
/// This writer lives entirely in <c>a2n.Vista.AspNetCore</c> (which already references
/// System.Text.Json); <c>a2n.Vista.Core</c> gains no serialization dependency.
/// </para>
/// </remarks>
public static class VistaJsonWriter
{
    /// <summary>The JSON media type Vista responses are written with.</summary>
    public const string JsonContentType = "application/json; charset=utf-8";

    /// <summary>
    /// Resolves the <see cref="JsonTypeInfo"/> for <paramref name="runtimeType"/> through the seam's
    /// resolver chain. Returns the metadata a source-generated context provides, the reflection
    /// fallback's metadata when no context covers the type, or throws when neither is available (the
    /// fallback was opted out and no context covers the type).
    /// </summary>
    /// <param name="runtimeType">The runtime type whose metadata to resolve.</param>
    /// <returns>The resolved <see cref="JsonTypeInfo"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeType"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">No chained resolver provides metadata for the type.</exception>
    public static JsonTypeInfo GetTypeInfo(Type runtimeType)
    {
        ArgumentNullException.ThrowIfNull(runtimeType);
        return VistaJson.Options.GetTypeInfo(runtimeType);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to UTF-8 bytes using the seam-resolved
    /// <see cref="JsonTypeInfo"/> for <paramref name="runtimeType"/> and the AOT-safe overload.
    /// </summary>
    /// <param name="value">The value to serialize (may be <see langword="null"/>).</param>
    /// <param name="runtimeType">The runtime type governing serialization metadata.</param>
    /// <returns>The serialized UTF-8 JSON bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeType"/> is <see langword="null"/>.</exception>
    public static byte[] SerializeToUtf8Bytes(object? value, Type runtimeType)
    {
        var typeInfo = GetTypeInfo(runtimeType);
        return JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to a JSON string using the seam-resolved
    /// <see cref="JsonTypeInfo"/> for <paramref name="runtimeType"/> and the AOT-safe overload.
    /// </summary>
    /// <param name="value">The value to serialize (may be <see langword="null"/>).</param>
    /// <param name="runtimeType">The runtime type governing serialization metadata.</param>
    /// <returns>The serialized JSON string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeType"/> is <see langword="null"/>.</exception>
    public static string Serialize(object? value, Type runtimeType)
    {
        var typeInfo = GetTypeInfo(runtimeType);
        return JsonSerializer.Serialize(value, typeInfo);
    }

    /// <summary>
    /// Builds an <see cref="IResult"/> that writes <paramref name="value"/> as
    /// <c>application/json</c> at the given status code, serialized through the seam. This is the
    /// AOT-safe replacement for <c>Results.Ok(obj)</c> in the List/Detail/Export handlers, preserving
    /// the status code and byte-for-byte body.
    /// </summary>
    /// <param name="value">The value to serialize (may be <see langword="null"/>).</param>
    /// <param name="runtimeType">The runtime type governing serialization metadata.</param>
    /// <param name="statusCode">The HTTP status code (default <c>200 OK</c>).</param>
    /// <returns>An <see cref="IResult"/> that writes the serialized JSON body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeType"/> is <see langword="null"/>.</exception>
    public static IResult Json(object? value, Type runtimeType, int statusCode = StatusCodes.Status200OK)
    {
        var payload = SerializeToUtf8Bytes(value, runtimeType);
        return new VistaJsonResult(payload, statusCode);
    }

    /// <summary>
    /// Writes <paramref name="value"/> as <c>application/json</c> at the given status code directly to
    /// the response body, serialized through the seam.
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <param name="value">The value to serialize (may be <see langword="null"/>).</param>
    /// <param name="runtimeType">The runtime type governing serialization metadata.</param>
    /// <param name="statusCode">The HTTP status code (default <c>200 OK</c>).</param>
    /// <param name="cancellationToken">A token to observe while writing.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="http"/> or <paramref name="runtimeType"/> is <see langword="null"/>.
    /// </exception>
    public static async Task WriteJsonAsync(
        HttpContext http,
        object? value,
        Type runtimeType,
        int statusCode = StatusCodes.Status200OK,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);

        var payload = SerializeToUtf8Bytes(value, runtimeType);
        http.Response.StatusCode = statusCode;
        http.Response.ContentType = JsonContentType;
        http.Response.ContentLength = payload.Length;
        await http.Response.Body.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A minimal <see cref="IResult"/> that writes a pre-serialized UTF-8 JSON payload with an
    /// <c>application/json</c> content type and a fixed status code, so handlers get byte-for-byte
    /// control over the response body.
    /// </summary>
    private sealed class VistaJsonResult : IResult
    {
        private readonly byte[] _payload;
        private readonly int _statusCode;

        internal VistaJsonResult(byte[] payload, int statusCode)
        {
            _payload = payload;
            _statusCode = statusCode;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            httpContext.Response.StatusCode = _statusCode;
            httpContext.Response.ContentType = JsonContentType;
            httpContext.Response.ContentLength = _payload.Length;
            await httpContext.Response.Body
                .WriteAsync(_payload, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
    }
}
