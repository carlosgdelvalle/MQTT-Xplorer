using System.Text.Json;
using System.Text.Json.Nodes;
using MqttExplorer.Core.Abstractions;

namespace MqttExplorer.Infrastructure;

public sealed class JsonConfigStore : IConfigStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public JsonConfigStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<T?> LoadAsync<T>(string store, CancellationToken cancellationToken = default)
    {
        var root = await ReadRootAsync(cancellationToken);
        if (root[store] is null)
        {
            return default;
        }

        return root[store]!.Deserialize<T>(_jsonOptions);
    }

    public async Task SaveAsync<T>(string store, T data, CancellationToken cancellationToken = default)
    {
        var root = await ReadRootAsync(cancellationToken);
        root[store] = JsonSerializer.SerializeToNode(data, _jsonOptions);
        await WriteRootAsync(root, cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await WriteRootAsync(new JsonObject(), cancellationToken);
    }

    private async Task<JsonObject> ReadRootAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new JsonObject();
        }

        await using var stream = File.OpenRead(_filePath);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        return node as JsonObject ?? new JsonObject();
    }

    private async Task WriteRootAsync(JsonObject root, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_filePath, root.ToJsonString(_jsonOptions), cancellationToken);
    }
}
