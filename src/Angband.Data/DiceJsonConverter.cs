using System.Text.Json;
using System.Text.Json.Serialization;
using Angband.Core.Randomness;

namespace Angband.Data;

/// <summary>Reads dice as strings (<c>"2d6+1"</c>) or plain numbers (<c>5</c>).</summary>
public sealed class DiceJsonConverter : JsonConverter<Dice>
{
    public override Dice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return Dice.Constant(reader.GetInt32());
        var text = reader.GetString();
        return Dice.TryParse(text, out var dice) ? dice : throw new JsonException($"Invalid dice expression '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, Dice value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
