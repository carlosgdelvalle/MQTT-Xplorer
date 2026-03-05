using MqttExplorer.Core.Abstractions;
using MqttExplorer.Core.Contracts;

namespace MqttExplorer.Infrastructure;

public sealed class MqttConnectionManager : IMqttConnectionManager
{
    private readonly Dictionary<string, IMqttDataSource> _connections = new();

    public event Action<string, ConnectionState>? ConnectionStateChanged;
    public event Action<string, MqttMessageDto>? ConnectionMessageReceived;

    public IReadOnlyCollection<string> ActiveConnectionIds => _connections.Keys.ToArray();

    public async Task AddOrReplaceConnectionAsync(
        string connectionId,
        MqttConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(connectionId, out var existing))
        {
            await RemoveConnectionAsync(connectionId, cancellationToken);
            await existing.DisposeAsync();
        }

        var source = new MqttDataSource();
        source.StateChanged += state => ConnectionStateChanged?.Invoke(connectionId, state);
        source.MessageReceived += message =>
        {
            // Keep parity with current backend: cap forwarded payload to 20k bytes.
            if (message.Payload?.Length > 20_000)
            {
                var clipped = message.Payload.ToBytes().Take(20_000).ToArray();
                message = message with { Payload = Base64Message.FromBytes(clipped) };
            }

            ConnectionMessageReceived?.Invoke(connectionId, message);
        };

        _connections[connectionId] = source;
        await source.ConnectAsync(options, cancellationToken);
    }

    public async Task PublishAsync(string connectionId, MqttMessageDto message, CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(connectionId, out var source))
        {
            await source.PublishAsync(message, cancellationToken);
        }
    }

    public async Task RemoveConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(connectionId, out var source))
        {
            return;
        }

        _connections.Remove(connectionId);
        await source.DisconnectAsync(cancellationToken);
        await source.DisposeAsync();
    }

    public async Task RemoveAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var ids = _connections.Keys.ToArray();
        foreach (var id in ids)
        {
            await RemoveConnectionAsync(id, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RemoveAllConnectionsAsync();
    }
}
