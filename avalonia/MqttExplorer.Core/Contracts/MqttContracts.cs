namespace MqttExplorer.Core.Contracts;

public enum QoS : byte
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2,
}

public sealed record Subscription(string Topic, QoS Qos);

public sealed class MqttConnectionOptions
{
    public string Url { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool Tls { get; init; }
    public bool CertValidation { get; init; } = true;
    public string? ClientId { get; init; }
    public IReadOnlyList<Subscription> Subscriptions { get; init; } = Array.Empty<Subscription>();
    public string? CertificateAuthorityBase64 { get; init; }
    public string? ClientCertificateBase64 { get; init; }
    public string? ClientKeyBase64 { get; init; }
}

public sealed class ConnectionProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public MqttConnectionOptions Options { get; init; } = new();
    public int? ConfigVersion { get; init; }
}

public sealed record MqttMessageDto(
    string Topic,
    Base64Message? Payload,
    QoS Qos,
    bool Retain,
    int? MessageId
);

public sealed record ConnectionState(bool Connecting, bool Connected, string? Error);
