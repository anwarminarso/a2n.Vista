using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Write;
using Microsoft.AspNetCore.Http;

namespace a2n.Vista.AspNetCore.Execution;

/// <summary>
/// Binds the raw HTTP write request into the pieces the write pipeline needs: the
/// <see cref="VistaWriteRequestBody"/> envelope, the typed <c>TCrud</c> model (closed over the view's
/// runtime <c>CrudType</c>), the Core-neutral row key, and the <c>If-Match</c> precondition header
/// (Decision Log D120). It is a pure binder — it performs no authorization, scope, or persistence — so
/// the endpoint mapper stays a dumb mapper and the executor stays HTTP-free (Requirement R14.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Classification of malformed input.</b> The binder distinguishes the request-shape failures the
/// error model requires:
/// </para>
/// <list type="bullet">
///   <item><description>an <b>array</b> body (a bulk batch) →
///   <see cref="VistaBulkNotEnabledException"/> (Requirement R15.1);</description></item>
///   <item><description>a <b>non-object</b> body or invalid JSON →
///   <see cref="VistaInvalidRequestException"/> classified <see cref="WriteErrorCode.MalformedBody"/>
///   (Requirement R9.1);</description></item>
///   <item><description>a <b>missing key</b> on Update/Delete →
///   <see cref="VistaInvalidRequestException"/> classified <see cref="WriteErrorCode.MissingKey"/>
///   (Requirements R2.8, R5.5, R9.2).</description></item>
/// </list>
/// <para>
/// The 428 precondition-required gate, the concurrency comparison, and key coercion against the view's
/// ordered <c>KeyFields</c> are deliberately <b>not</b> done here; they belong to the endpoint gate and
/// the EF executor respectively. This binder only surfaces the header value and the neutral key shape.
/// </para>
/// </remarks>
public static class VistaWriteBinding
{
    /// <summary>
    /// Reads and validates the top-level write envelope from the request body. An empty body, a scalar,
    /// or otherwise non-object JSON is a malformed write; a JSON array is a (disabled) bulk batch.
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <returns>The parsed <see cref="VistaWriteRequestBody"/> envelope.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="http"/> is <see langword="null"/>.</exception>
    /// <exception cref="VistaBulkNotEnabledException">The body is a JSON array (Requirement R15.1).</exception>
    /// <exception cref="VistaInvalidRequestException">
    /// The body is empty, not valid JSON, or not a JSON object (Requirement R9.1).
    /// </exception>
    public static async Task<VistaWriteRequestBody> ReadBodyAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (http.Request.ContentLength is 0)
        {
            throw new VistaInvalidRequestException(
                "A write request requires a JSON object body.", WriteErrorCode.MalformedBody);
        }

        JsonElement root;
        try
        {
            using var document = await JsonDocument
                .ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted)
                .ConfigureAwait(false);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new VistaInvalidRequestException(
                $"The request body is not valid JSON: {ex.Message}", WriteErrorCode.MalformedBody);
        }

        // R15.1: an array body is a bulk batch, which is not enabled in this milestone.
        if (root.ValueKind == JsonValueKind.Array)
        {
            throw new VistaBulkNotEnabledException();
        }

        // R9.1: a single-entity write must be a JSON object envelope ({ "model": ..., "key": ... }).
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new VistaInvalidRequestException(
                "A write request body must be a JSON object.", WriteErrorCode.MalformedBody);
        }

        return root.Deserialize<VistaWriteRequestBody>(VistaJson.Options) ?? new VistaWriteRequestBody();
    }

    /// <summary>
    /// Binds the envelope's <see cref="VistaWriteRequestBody.Model"/> member to the view's typed write
    /// model. The reflection bridge closes deserialization over the runtime <paramref name="crudType"/>
    /// (the view's <c>CrudType</c>), mirroring the read bridge in <see cref="ViewRequestExecutor"/>.
    /// </summary>
    /// <param name="body">The parsed write envelope.</param>
    /// <param name="crudType">The view's <c>CrudType</c> (the <c>TCrud</c> contract) to bind into.</param>
    /// <returns>The bound write model instance, typed as <paramref name="crudType"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> or <paramref name="crudType"/> is <see langword="null"/>.</exception>
    /// <exception cref="VistaBulkNotEnabledException">The model is a JSON array (Requirement R15.1).</exception>
    /// <exception cref="VistaInvalidRequestException">
    /// The model is absent, null, or not a JSON object (Requirement R9.1).
    /// </exception>
    [RequiresUnreferencedCode(
        "Binds the write model by deserializing the JSON payload into the view's runtime CrudType; "
        + "use the source generator path for AOT.")]
    public static object BindModel(VistaWriteRequestBody body, Type crudType)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(crudType);

        if (body.Model is not { } model
            || model.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new VistaInvalidRequestException(
                "A write request requires a 'model' payload.", WriteErrorCode.MalformedBody);
        }

        if (model.ValueKind == JsonValueKind.Array)
        {
            throw new VistaBulkNotEnabledException();
        }

        if (model.ValueKind != JsonValueKind.Object)
        {
            throw new VistaInvalidRequestException(
                "The write 'model' must be a JSON object.", WriteErrorCode.MalformedBody);
        }

        try
        {
            return model.Deserialize(crudType, VistaJson.Options)
                ?? throw new VistaInvalidRequestException(
                    "The write 'model' payload was null.", WriteErrorCode.MalformedBody);
        }
        catch (JsonException ex)
        {
            throw new VistaInvalidRequestException(
                $"The write 'model' payload is not valid: {ex.Message}", WriteErrorCode.MalformedBody);
        }
    }

    /// <summary>
    /// Reads the row key from the envelope's <see cref="VistaWriteRequestBody.Key"/> member into the
    /// Core-neutral key shape (a scalar, or a field-name→value map for a composite key) via
    /// <see cref="VistaKeyReader"/>. Coercion against the view's ordered <c>KeyFields</c> happens later,
    /// in the executor.
    /// </summary>
    /// <param name="body">The parsed write envelope.</param>
    /// <returns>A boxed scalar, or an <c>IReadOnlyDictionary&lt;string, object?&gt;</c> for a composite key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    /// <exception cref="VistaInvalidRequestException">
    /// The key is absent, null, or otherwise unreadable (Requirements R2.8, R5.5, R9.2).
    /// </exception>
    public static object ReadKey(VistaWriteRequestBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.Key is not { } key
            || key.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new VistaInvalidRequestException(
                "An update or delete request requires a 'key'.", WriteErrorCode.MissingKey);
        }

        try
        {
            return VistaKeyReader.Read(key);
        }
        catch (JsonException ex)
        {
            throw new VistaInvalidRequestException(
                $"The request 'key' is invalid: {ex.Message}", WriteErrorCode.MissingKey);
        }
    }

    /// <summary>
    /// Reads the optimistic-concurrency precondition from the HTTP <c>If-Match</c> request header.
    /// Returns <see langword="null"/> when the header is absent, empty, or whitespace-only, so the
    /// endpoint can apply the 428 precondition-required gate for token views and ignore it for tokenless
    /// views (Requirements R6.1, R6.2, R6.6). No 428 decision is made here — this is a pure read.
    /// </summary>
    /// <param name="http">The current HTTP context.</param>
    /// <returns>The trimmed <c>If-Match</c> value, or <see langword="null"/> when it is missing/blank.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="http"/> is <see langword="null"/>.</exception>
    public static string? ReadIfMatch(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        var raw = http.Request.Headers.IfMatch.ToString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
