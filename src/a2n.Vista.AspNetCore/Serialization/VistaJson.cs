using System.Text.Json;
using System.Text.Json.Serialization;

namespace a2n.Vista.AspNetCore.Serialization;

/// <summary>
/// The System.Text.Json options Vista uses to read action-endpoint request bodies and write responses
/// (Decision Log D110). Case-insensitive property matching, enum-as-string, and the polymorphic
/// <see cref="FilterNodeJsonConverter"/> are registered here so the HTTP layer can (de)serialize the
/// neutral request envelopes without depending on the host's global JSON configuration.
/// </summary>
public static class VistaJson
{
    /// <summary>The shared, read-only options instance used by the Vista endpoint handlers.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        return options;
    }
}
