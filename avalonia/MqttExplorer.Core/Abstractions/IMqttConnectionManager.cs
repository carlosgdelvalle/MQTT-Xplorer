using MqttExplorer.Core.Contracts;

namespace MqttExplorer.Core.Abstractions;

public interface IMqttConnectionManager : IAsyncDisposable
{
    event Action<string, ConnectionState>? ConnectionStateChanged;
    event Action<string, MqttMessageDto>? ConnectionMessageReceived;

    IReadOnlyCollection<string> ActiveConnectionIds { get; }

    Task AddOrReplaceConnectionAsync(string connectionId, MqttConnectionOptions options, CancellationToken cancellationToken = default);
    Task PublishAsync(string connectionId, MqttMessageDto message, CancellationToken cancellationToken = default);
    Task RemoveConnectionAsync(string connectionId, CancellationToken cancellationToken = default);
    Task RemoveAllConnectionsAsync(CancellationToken cancellationToken = default);
}
