using MqttExplorer.Core.Contracts;

namespace MqttExplorer.Core.Model;

public sealed record BufferedMqttMessage(MqttMessageDto Message, DateTimeOffset Received);

public sealed class ChangeBuffer
{
    private readonly List<BufferedMqttMessage> _buffer = new();
    private long _size;
    private readonly long _maxSize = 100_000_000;

    public int Length => _buffer.Count;
    public int EstimatedMessageOverhead { get; init; } = 24;

    public void Push(MqttMessageDto value)
    {
        if (IsFull)
        {
            return;
        }

        _buffer.Add(new BufferedMqttMessage(value, DateTimeOffset.UtcNow));
        _size += EstimatedMessageOverhead + (value.Payload?.Length ?? 0);
    }

    public long Size => _size;
    public bool IsFull => _size >= _maxSize;
    public double FillState => _size / (double)_maxSize;

    public IReadOnlyList<BufferedMqttMessage> PopAll()
    {
        var copy = _buffer.ToArray();
        _buffer.Clear();
        _size = 0;
        return copy;
    }
}
