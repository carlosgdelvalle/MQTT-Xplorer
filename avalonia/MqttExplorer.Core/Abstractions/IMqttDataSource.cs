using MqttExplorer.Core.Contracts;

namespace MqttExplorer.Core.Abstractions;

public interface IMqttDataSource : IAsyncDisposable
{
    event Action<ConnectionState>? StateChanged;
    event Action<MqttMessageDto>? MessageReceived;

    Task ConnectAsync(MqttConnectionOptions options, CancellationToken cancellationToken = default);
    Task PublishAsync(MqttMessageDto message, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
