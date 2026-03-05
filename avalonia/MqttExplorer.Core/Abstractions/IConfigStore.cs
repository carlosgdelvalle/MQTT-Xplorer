namespace MqttExplorer.Core.Abstractions;

public interface IConfigStore
{
    Task<T?> LoadAsync<T>(string store, CancellationToken cancellationToken = default);
    Task SaveAsync<T>(string store, T data, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
