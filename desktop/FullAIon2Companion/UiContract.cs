using System.Text.Json;
using System.Text.Json.Serialization;
namespace FullAIon2Companion;

/// <summary>UI states use their names, matching the JavaScript renderer.</summary>
public static class UiContract
{
    public static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
