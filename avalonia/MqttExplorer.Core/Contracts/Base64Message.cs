using System.Text;
using System.Text.Json;

namespace MqttExplorer.Core.Contracts;

public sealed class Base64Message
{
    public string Base64Value { get; }

    public int Length => Base64Value.Length;

    public Base64Message(string? base64Value = null)
    {
        Base64Value = base64Value ?? string.Empty;
    }

    public static Base64Message FromBytes(byte[] bytes) => new(Convert.ToBase64String(bytes));

    public static Base64Message FromString(string value) =>
        new(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));

    public byte[] ToBytes()
    {
        if (string.IsNullOrWhiteSpace(Base64Value))
        {
            return Array.Empty<byte>();
        }

        return Convert.FromBase64String(Base64Value);
    }

    public string ToUnicodeString() => Encoding.UTF8.GetString(ToBytes());

    public (string Text, string? Language) Format(string type = "string")
    {
        try
        {
            return type switch
            {
                "json" => (JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(ToUnicodeString()),
                        new JsonSerializerOptions { WriteIndented = true }), "json"),
                "hex" => (ToHexString(), null),
                _ => (ToUnicodeString(), null),
            };
        }
        catch
        {
            return (ToUnicodeString(), null);
        }
    }

    public string ToHexString() =>
        string.Join(' ', ToBytes().Select(b => $"0x{b:X2}"));
}
