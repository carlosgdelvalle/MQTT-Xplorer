using MqttExplorer.Core.Contracts;

namespace MqttExplorer.Core.Model;

public sealed record MessageRecord(
    Base64Message? Payload,
    int? MessageId,
    bool Retain,
    QoS Qos,
    int Length,
    DateTimeOffset Received,
    long MessageNumber
) : ILengthAware;
