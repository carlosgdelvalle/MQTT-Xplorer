using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MqttExplorer.Core.Abstractions;
using MqttExplorer.Core.Contracts;
using MqttExplorer.Core.Model;
using MqttExplorer.Infrastructure;

namespace MqttExplorer.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IMqttConnectionManager _connectionManager;
    private readonly IConfigStore _configStore;
    private readonly TopicTree _topicTree = new();
    private readonly ConcurrentQueue<MqttMessageDto> _pendingMessages = new();
    private readonly DispatcherTimer _treeUpdateTimer;
    private DateTimeOffset _lastTreeInteractionAt = DateTimeOffset.MinValue;

    [ObservableProperty] private string _connectionName = "Local broker";
    [ObservableProperty] private string _connectionId = "local";
    [ObservableProperty] private string _brokerUrl = "mqtt://localhost:1883";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _tls;
    [ObservableProperty] private bool _certValidation = true;
    [ObservableProperty] private string _subscriptions = "#";
    [ObservableProperty] private string _status = "Disconnected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private TopicNodeViewModel? _selectedNode;
    [ObservableProperty] private ConnectionProfile? _selectedConnection;

    [ObservableProperty] private string _publishTopic = string.Empty;
    [ObservableProperty] private string _publishPayload = string.Empty;
    [ObservableProperty] private bool _publishRetain;
    [ObservableProperty] private QoS _publishQos = QoS.AtMostOnce;

    public ObservableCollection<TopicNodeViewModel> TopicNodes { get; } = [];
    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = [];
    public ObservableCollection<QoS> QosLevels { get; } =
    [
        QoS.AtMostOnce,
        QoS.AtLeastOnce,
        QoS.ExactlyOnce,
    ];
    [ObservableProperty] private FlatTreeDataGridSource<TopicNodeViewModel> _topicTreeSource;

    public MainWindowViewModel()
        : this(
            new MqttConnectionManager(),
            new JsonConfigStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MQTT-Xplorer-Avalonia",
                "config.json")))
    {
    }

    public MainWindowViewModel(IMqttConnectionManager connectionManager, IConfigStore configStore)
    {
        _connectionManager = connectionManager;
        _configStore = configStore;
        _topicTreeSource = CreateTopicTreeSource();
        _connectionManager.ConnectionStateChanged += OnConnectionStateChanged;
        _connectionManager.ConnectionMessageReceived += OnConnectionMessageReceived;
        _treeUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _treeUpdateTimer.Tick += (_, _) => FlushPendingTreeUpdates();
        _treeUpdateTimer.Start();
        _ = LoadConnectionsAsync();
    }

    public string SelectedPayload =>
        SelectedNode?.PayloadText ?? "(no payload)";

    partial void OnSelectedNodeChanged(TopicNodeViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedPayload));
        if (value is not null && string.IsNullOrWhiteSpace(PublishTopic))
        {
            PublishTopic = value.FullPath;
        }
    }

    partial void OnSelectedConnectionChanged(ConnectionProfile? value)
    {
        if (value is null)
        {
            return;
        }

        ConnectionId = value.Id;
        ConnectionName = value.Name;
        BrokerUrl = value.Options.Url;
        Username = value.Options.Username ?? string.Empty;
        Password = value.Options.Password ?? string.Empty;
        Tls = value.Options.Tls;
        CertValidation = value.Options.CertValidation;
        Subscriptions = string.Join(", ", value.Options.Subscriptions.Select(s => s.Topic));
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            var options = new MqttConnectionOptions
            {
                Url = BrokerUrl,
                Username = string.IsNullOrWhiteSpace(Username) ? null : Username,
                Password = string.IsNullOrWhiteSpace(Password) ? null : Password,
                Tls = Tls,
                CertValidation = CertValidation,
                Subscriptions = ParseSubscriptions(Subscriptions),
            };

            Status = "Connecting...";
            await _connectionManager.AddOrReplaceConnectionAsync(ConnectionId, options);
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveConnectionAsync()
    {
        var profile = new ConnectionProfile
        {
            Id = ConnectionId,
            Name = string.IsNullOrWhiteSpace(ConnectionName) ? ConnectionId : ConnectionName,
            Options = new MqttConnectionOptions
            {
                Url = BrokerUrl,
                Username = string.IsNullOrWhiteSpace(Username) ? null : Username,
                Password = string.IsNullOrWhiteSpace(Password) ? null : Password,
                Tls = Tls,
                CertValidation = CertValidation,
                Subscriptions = ParseSubscriptions(Subscriptions),
            },
            ConfigVersion = 1,
        };

        var existing = SavedConnections.FirstOrDefault(c => c.Id == profile.Id);
        if (existing is not null)
        {
            SavedConnections.Remove(existing);
        }

        SavedConnections.Add(profile);
        await PersistConnectionsAsync();
        Status = $"Saved connection '{profile.Name}'";
    }

    [RelayCommand]
    private async Task LoadConnectionsAsync()
    {
        SavedConnections.Clear();
        var profiles = await _configStore.LoadAsync<List<ConnectionProfile>>("connectionsV2") ?? [];
        foreach (var profile in profiles)
        {
            SavedConnections.Add(profile);
        }

        if (SavedConnections.Count == 0)
        {
            await TryImportLegacyConnectionsAsync();
        }

        SelectedConnection = SavedConnections.FirstOrDefault();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _connectionManager.RemoveConnectionAsync(ConnectionId);
        IsConnected = false;
        Status = "Disconnected";
    }

    [RelayCommand]
    private async Task PublishAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PublishTopic))
            {
                Status = "Publish topic is required.";
                return;
            }

            var message = new MqttMessageDto(
                PublishTopic,
                Base64Message.FromString(PublishPayload),
                PublishQos,
                PublishRetain,
                null);

            await _connectionManager.PublishAsync(ConnectionId, message);
            Status = $"Published to {PublishTopic}";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private void OnConnectionStateChanged(string id, ConnectionState state)
    {
        if (id != ConnectionId)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = state.Connected;
            Status = state.Error is not null
                ? $"Error: {state.Error}"
                : state.Connecting
                    ? "Connecting..."
                    : state.Connected
                        ? "Connected"
                        : "Disconnected";
        });
    }

    private void OnConnectionMessageReceived(string id, MqttMessageDto message)
    {
        if (id != ConnectionId)
        {
            return;
        }

        _pendingMessages.Enqueue(message);
    }

    private void FlushPendingTreeUpdates()
    {
        if (_pendingMessages.IsEmpty)
        {
            return;
        }

        try
        {
            while (_pendingMessages.TryDequeue(out var message))
            {
                _topicTree.Enqueue(message);
            }

            _topicTree.ApplyUnmergedChanges();
            var isUserExploring = DateTimeOffset.UtcNow - _lastTreeInteractionAt < TimeSpan.FromSeconds(2);
            if (!isUserExploring)
            {
                RebuildTopicTree();
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private void RebuildTopicTree()
    {
        var roots = _topicTree.EdgeArray
            .OrderBy(e => e.Name)
            .Where(e => e.Target is not null)
            .Select(e => e.Target!)
            .ToArray();

        var flatNodes = FlattenNodes(roots).ToArray();
        TopicTreeSource = CreateTopicTreeSource(flatNodes);
    }

    private static void SyncNodes(
        ObservableCollection<TopicNodeViewModel> targetCollection,
        IReadOnlyList<TreeNode> sourceNodes)
    {
        var sourcePathSet = new HashSet<string>(
            sourceNodes
                .Select(GetNodePath)
                .Where(path => !string.IsNullOrWhiteSpace(path)),
            StringComparer.Ordinal);

        for (var i = targetCollection.Count - 1; i >= 0; i--)
        {
            var existing = targetCollection[i];
            if (string.IsNullOrWhiteSpace(existing.FullPath) || !sourcePathSet.Contains(existing.FullPath))
            {
                targetCollection.RemoveAt(i);
            }
        }

        for (var targetIndex = 0; targetIndex < sourceNodes.Count; targetIndex++)
        {
            var source = sourceNodes[targetIndex];
            var path = source.Path();
            var existingIndex = IndexOfByPath(targetCollection, path);

            if (existingIndex < 0)
            {
                targetCollection.Insert(targetIndex, ToViewModel(source));
                continue;
            }

            if (existingIndex != targetIndex)
            {
                targetCollection.Move(existingIndex, targetIndex);
            }

            var existing = targetCollection[targetIndex];
            UpdateNode(existing, source);
        }
    }

    private static TopicNodeViewModel ToViewModel(TreeNode node, int depth = 0)
    {
        var vm = new TopicNodeViewModel(
            node.SourceEdge?.Name ?? "(root)",
            node.Path(),
            node.Message?.Payload?.Format(node.Type).Text ?? "(empty)",
            depth);

        foreach (var edge in node.EdgeArray.OrderBy(e => e.Name))
        {
            if (edge.Target is not null)
            {
                vm.Children.Add(ToViewModel(edge.Target, depth + 1));
            }
        }

        return vm;
    }

    private static void UpdateNode(TopicNodeViewModel existing, TreeNode source)
    {
        existing.Name = source.SourceEdge?.Name ?? "(root)";
        existing.FullPath = source.Path();
        existing.PayloadText = source.Message?.Payload?.Format(source.Type).Text ?? "(empty)";
        existing.IsExpanded = true;

        var children = source.EdgeArray
            .OrderBy(e => e.Name)
            .Where(e => e.Target is not null)
            .Select(e => e.Target!)
            .ToArray();

        SyncNodes(existing.Children, children);
    }

    private static int IndexOfByPath(ObservableCollection<TopicNodeViewModel> nodes, string path)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (string.Equals(nodes[i].FullPath, path, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetNodePath(TreeNode node)
    {
        try
        {
            return node.Path() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private FlatTreeDataGridSource<TopicNodeViewModel> CreateTopicTreeSource(
        IEnumerable<TopicNodeViewModel>? rootNodes = null)
    {
        var source = new FlatTreeDataGridSource<TopicNodeViewModel>(rootNodes ?? Array.Empty<TopicNodeViewModel>())
        {
            Columns =
            {
                new TextColumn<TopicNodeViewModel, string>("Topic", node => node.DisplayName),
                new TextColumn<TopicNodeViewModel, string>("Path", node => node.FullPath),
            },
        };

        source.RowSelection!.SingleSelect = true;
        source.RowSelection.SelectionChanged += (_, _) =>
        {
            var selected = source.RowSelection.SelectedItem;
            if (selected is TopicNodeViewModel selectedNode)
            {
                _lastTreeInteractionAt = DateTimeOffset.UtcNow;
                SelectedNode = selectedNode;
            }
        };

        return source;
    }

    private static IEnumerable<TopicNodeViewModel> FlattenNodes(IEnumerable<TreeNode> roots)
    {
        foreach (var root in roots)
        {
            foreach (var item in FlattenNode(root, 0))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<TopicNodeViewModel> FlattenNode(TreeNode node, int depth)
    {
        yield return new TopicNodeViewModel(
            node.SourceEdge?.Name ?? "(root)",
            node.Path(),
            node.Message?.Payload?.Format(node.Type).Text ?? "(empty)",
            depth);

        foreach (var edge in node.EdgeArray.OrderBy(e => e.Name))
        {
            if (edge.Target is null)
            {
                continue;
            }

            foreach (var child in FlattenNode(edge.Target, depth + 1))
            {
                yield return child;
            }
        }
    }

    private static IReadOnlyList<Subscription> ParseSubscriptions(string value)
    {
        var topics = value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (topics.Length == 0)
        {
            return [new Subscription("#", QoS.AtMostOnce)];
        }

        return topics.Select(topic => new Subscription(topic, QoS.AtMostOnce)).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        _treeUpdateTimer.Stop();
        await _connectionManager.RemoveAllConnectionsAsync();
        await _connectionManager.DisposeAsync();
    }

    private async Task PersistConnectionsAsync()
    {
        await _configStore.SaveAsync("connectionsV2", SavedConnections.ToList());
    }

    private async Task TryImportLegacyConnectionsAsync()
    {
        var legacy = await _configStore.LoadAsync<Dictionary<string, LegacyConnection>>("connections");
        if (legacy is null || legacy.Count == 0)
        {
            return;
        }

        foreach (var item in legacy.Values)
        {
            var protocol = item.Protocol == "ws" ? "ws" : "mqtt";
            var url = $"{protocol}://{item.Host}:{item.Port}";
            var subscriptions = item.Subscriptions?.Select(s => new Subscription(s, QoS.AtMostOnce)).ToArray()
                ?? [new Subscription("#", QoS.AtMostOnce)];

            SavedConnections.Add(new ConnectionProfile
            {
                Id = item.Id,
                Name = item.Name,
                Options = new MqttConnectionOptions
                {
                    Url = url,
                    Username = item.Username,
                    Password = item.Password,
                    Tls = item.Encryption,
                    CertValidation = item.CertValidation,
                    ClientId = item.ClientId,
                    Subscriptions = subscriptions,
                },
                ConfigVersion = 1,
            });
        }

        await PersistConnectionsAsync();
        Status = "Imported legacy connections.";
    }

    private sealed class LegacyConnection
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Host { get; init; } = "localhost";
        public int Port { get; init; } = 1883;
        public string Protocol { get; init; } = "mqtt";
        public string? Username { get; init; }
        public string? Password { get; init; }
        public bool Encryption { get; init; }
        public bool CertValidation { get; init; } = true;
        public string? ClientId { get; init; }
        public string[]? Subscriptions { get; init; }
    }
}
