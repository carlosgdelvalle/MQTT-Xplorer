using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MqttExplorer.Core.Abstractions;
using MqttExplorer.Core.Contracts;
using MqttExplorer.Core.Model;
using MqttExplorer.Infrastructure;

namespace MqttExplorer.Avalonia.ViewModels;

public enum PayloadInspectorMode
{
    Raw,
    Json,
    Hex,
    Base64,
}

public sealed class TopicTimelineEntryViewModel(
    string timeLabel,
    string receivedAtLabel,
    string payloadPreview,
    bool isChanged,
    string diffSummary)
{
    public string TimeLabel { get; } = timeLabel;
    public string ReceivedAtLabel { get; } = receivedAtLabel;
    public string PayloadPreview { get; } = payloadPreview;
    public bool IsChanged { get; } = isChanged;
    public string DiffSummary { get; } = diffSummary;
    public string ChangeIndicator => IsChanged ? "changed" : "same";
    public string HighlightBackground => IsChanged ? "#223A7C2D" : "Transparent";
}

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IMqttConnectionManager _connectionManager;
    private readonly IConfigStore _configStore;
    private readonly TopicTree _topicTree = new();
    private readonly Dictionary<string, TopicActivity> _lastMessageByPath = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<MqttMessageDto> _pendingMessages = new();
    private readonly DispatcherTimer _treeUpdateTimer;
    private readonly DispatcherTimer _layoutPersistTimer;
    private DateTimeOffset _lastTreeInteractionAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastActivityRefreshAt = DateTimeOffset.MinValue;
    private bool _isProgrammaticSelection;
    private bool _isLoadingLayout;

    [ObservableProperty] private string _connectionName = "Local broker";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(ConnectionValidationError))]
    [NotifyPropertyChangedFor(nameof(HasConnectionValidationError))]
    private string _connectionId = "local";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(ConnectionValidationError))]
    [NotifyPropertyChangedFor(nameof(HasConnectionValidationError))]
    private string _brokerUrl = "mqtt://localhost:1883";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _tls;
    [ObservableProperty] private bool _certValidation = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(ConnectionValidationError))]
    [NotifyPropertyChangedFor(nameof(HasConnectionValidationError))]
    private string _subscriptions = "#";
    [ObservableProperty] private string _status = "Disconnected";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private bool _isConnected;
    [ObservableProperty] private TopicNodeViewModel? _selectedNode;
    [ObservableProperty] private ConnectionProfile? _selectedConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private string _publishTopic = string.Empty;
    [ObservableProperty] private string _publishPayload = string.Empty;
    [ObservableProperty] private bool _publishRetain;
    [ObservableProperty] private QoS _publishQos = QoS.AtMostOnce;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingUpdates))]
    [NotifyPropertyChangedFor(nameof(PendingUpdatesLabel))]
    private int _pendingUpdateCount;
    [ObservableProperty] private bool _autoRefreshWhileBrowsing = true;
    [ObservableProperty] private string _topicFilter = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPayload))]
    private PayloadInspectorMode _selectedPayloadMode = PayloadInspectorMode.Raw;
    [ObservableProperty] private GridLength _leftPaneWidth = new(300, GridUnitType.Pixel);
    [ObservableProperty] private GridLength _rightPaneWidth = new(360, GridUnitType.Pixel);

    public ObservableCollection<TopicNodeViewModel> TopicNodes { get; } = [];
    public ObservableCollection<TopicTimelineEntryViewModel> SelectedTopicTimeline { get; } = [];
    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = [];
    public ObservableCollection<QoS> QosLevels { get; } =
    [
        QoS.AtMostOnce,
        QoS.AtLeastOnce,
        QoS.ExactlyOnce,
    ];
    public ObservableCollection<PayloadInspectorMode> PayloadModes { get; } =
    [
        PayloadInspectorMode.Raw,
        PayloadInspectorMode.Json,
        PayloadInspectorMode.Hex,
        PayloadInspectorMode.Base64,
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
        _layoutPersistTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _layoutPersistTimer.Tick += OnLayoutPersistTick;
        _treeUpdateTimer.Start();
        _ = LoadConnectionsAsync();
        _ = LoadUiLayoutAsync();
    }

    public string SelectedPayload => FormatSelectedPayload();
    public string SelectedPayloadMetadata => BuildSelectedPayloadMetadata();
    public bool HasPendingUpdates => PendingUpdateCount > 0;
    public string PendingUpdatesLabel => PendingUpdateCount > 0 ? $"{PendingUpdateCount} pending updates" : "Up to date";
    public bool CanConnect => string.IsNullOrWhiteSpace(ConnectionValidationError);
    public bool HasConnectionValidationError => !CanConnect;
    public string ConnectionValidationError => ValidateConnectionInputs();
    public bool CanPublish => IsConnected && !string.IsNullOrWhiteSpace(PublishTopic);
    public bool CanCopySelection => SelectedNode is not null;

    partial void OnSelectedNodeChanged(TopicNodeViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedPayload));
        OnPropertyChanged(nameof(SelectedPayloadMetadata));
        OnPropertyChanged(nameof(CanCopySelection));
        CopyTopicCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        CopyPayloadCommand.NotifyCanExecuteChanged();
        if (value is not null && string.IsNullOrWhiteSpace(PublishTopic))
        {
            PublishTopic = value.FullPath;
        }

        RebuildSelectedTopicTimeline();
    }

    partial void OnSelectedPayloadModeChanged(PayloadInspectorMode value)
    {
        OnPropertyChanged(nameof(SelectedPayload));
    }

    partial void OnLeftPaneWidthChanged(GridLength value) => QueueLayoutPersistence();
    partial void OnRightPaneWidthChanged(GridLength value) => QueueLayoutPersistence();

    partial void OnTopicFilterChanged(string value)
    {
        RebuildTopicTree();
    }

    partial void OnAutoRefreshWhileBrowsingChanged(bool value)
    {
        if (value && HasPendingUpdates)
        {
            ApplyPendingUpdates();
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
            if (!CanConnect)
            {
                Status = ConnectionValidationError;
                return;
            }

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
            if (!IsConnected)
            {
                Status = "Connect first before publishing.";
                return;
            }

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

    [RelayCommand(CanExecute = nameof(CanCopySelection))]
    private async Task CopyTopicAsync()
    {
        if (SelectedNode is null)
        {
            return;
        }

        await CopyTextToClipboardAsync(SelectedNode.Name, "topic");
    }

    [RelayCommand(CanExecute = nameof(CanCopySelection))]
    private async Task CopyPathAsync()
    {
        if (SelectedNode is null)
        {
            return;
        }

        await CopyTextToClipboardAsync(SelectedNode.FullPath, "path");
    }

    [RelayCommand(CanExecute = nameof(CanCopySelection))]
    private async Task CopyPayloadAsync()
    {
        if (SelectedNode is null)
        {
            return;
        }

        await CopyTextToClipboardAsync(SelectedPayload, "payload");
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
        try
        {
            var dequeuedCount = 0;
            while (_pendingMessages.TryDequeue(out var message))
            {
                TrackTopicActivity(message);
                _topicTree.Enqueue(message);
                dequeuedCount++;
            }

            if (dequeuedCount > 0)
            {
                _topicTree.ApplyUnmergedChanges();
            }

            if (dequeuedCount == 0 && !HasPendingUpdates)
            {
                var needsActivityRefresh = HasRecentActivity() &&
                    DateTimeOffset.UtcNow - _lastActivityRefreshAt >= TimeSpan.FromSeconds(1);
                if (!needsActivityRefresh)
                {
                    return;
                }
            }

            if (ShouldRefreshTreeNow())
            {
                RebuildTopicTree();
                PendingUpdateCount = 0;
                _lastActivityRefreshAt = DateTimeOffset.UtcNow;
            }
            else if (dequeuedCount > 0)
            {
                PendingUpdateCount += dequeuedCount;
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private void RebuildTopicTree()
    {
        var previouslySelectedPath = SelectedNode?.FullPath;
        var snapshotTime = DateTimeOffset.UtcNow;
        var roots = _topicTree.EdgeArray
            .OrderBy(e => e.Name)
            .Where(e => e.Target is not null)
            .Select(e => e.Target!)
            .ToArray();

        var flatNodes = FlattenNodes(roots, snapshotTime);
        var filter = TopicFilter.Trim();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            flatNodes = flatNodes.Where(node =>
                node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                node.FullPath.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var snapshot = flatNodes.ToArray();
        TopicTreeSource = CreateTopicTreeSource(snapshot);
        RestoreSelection(snapshot, previouslySelectedPath);
        RebuildSelectedTopicTimeline();
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
                new TextColumn<TopicNodeViewModel, string>("", node => node.ActivityIndicator),
                new TextColumn<TopicNodeViewModel, string>("Topic", node => node.DisplayName),
                new TextColumn<TopicNodeViewModel, string>("Last Message", node => node.LastMessagePreview),
                new TextColumn<TopicNodeViewModel, string>("Updated", node => node.ReceivedAgo),
                new TextColumn<TopicNodeViewModel, string>("Path", node => node.FullPath),
            },
        };

        source.RowSelection!.SingleSelect = true;
        source.RowSelection.SelectionChanged += (_, _) =>
        {
            var selected = source.RowSelection.SelectedItem;
            if (selected is TopicNodeViewModel selectedNode)
            {
                if (!_isProgrammaticSelection)
                {
                    _lastTreeInteractionAt = DateTimeOffset.UtcNow;
                }
                SelectedNode = selectedNode;
            }
        };

        return source;
    }

    private void RestoreSelection(IReadOnlyList<TopicNodeViewModel> snapshot, string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var matchIndex = -1;
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (string.Equals(snapshot[i].FullPath, selectedPath, StringComparison.Ordinal))
            {
                matchIndex = i;
                break;
            }
        }

        if (matchIndex < 0)
        {
            return;
        }

        _isProgrammaticSelection = true;
        try
        {
            TopicTreeSource.RowSelection?.Select(matchIndex);
            SelectedNode = snapshot[matchIndex];
        }
        finally
        {
            _isProgrammaticSelection = false;
        }
    }

    private IEnumerable<TopicNodeViewModel> FlattenNodes(IEnumerable<TreeNode> roots, DateTimeOffset snapshotTime)
    {
        foreach (var root in roots)
        {
            foreach (var item in FlattenNode(root, 0, snapshotTime))
            {
                yield return item;
            }
        }
    }

    private IEnumerable<TopicNodeViewModel> FlattenNode(TreeNode node, int depth, DateTimeOffset snapshotTime)
    {
        var path = node.Path();
        var payloadText = node.Message?.Payload?.Format(node.Type).Text ?? "(empty)";
        var payloadBase64 = node.Message?.Payload?.Base64Value ?? string.Empty;
        var payloadLength = node.Message?.Length ?? 0;
        var receivedAt = node.Message?.Received;
        var qosLabel = node.Message?.Qos.ToString() ?? string.Empty;
        var retain = node.Message?.Retain ?? false;
        _lastMessageByPath.TryGetValue(path, out var activity);
        var hasRecentActivity = activity is not null &&
            snapshotTime - activity.ReceivedAt <= TimeSpan.FromSeconds(2);
        var receivedAgo = activity is null ? string.Empty : FormatRelativeTime(snapshotTime - activity.ReceivedAt);
        var lastMessage = activity?.LastMessagePreview ?? TruncatePayload(payloadText);

        yield return new TopicNodeViewModel(
            node.SourceEdge?.Name ?? "(root)",
            path,
            payloadText,
            depth,
            lastMessage,
            receivedAgo,
            hasRecentActivity,
            payloadBase64,
            payloadLength,
            qosLabel,
            retain,
            receivedAt);

        foreach (var edge in node.EdgeArray.OrderBy(e => e.Name))
        {
            if (edge.Target is null)
            {
                continue;
            }

            foreach (var child in FlattenNode(edge.Target, depth + 1, snapshotTime))
            {
                yield return child;
            }
        }
    }

    [RelayCommand]
    private void ApplyPendingUpdates()
    {
        try
        {
            while (_pendingMessages.TryDequeue(out var message))
            {
                TrackTopicActivity(message);
                _topicTree.Enqueue(message);
            }

            _topicTree.ApplyUnmergedChanges();
            RebuildTopicTree();
            PendingUpdateCount = 0;
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private bool ShouldRefreshTreeNow()
    {
        if (!AutoRefreshWhileBrowsing)
        {
            return false;
        }

        var isUserExploring = DateTimeOffset.UtcNow - _lastTreeInteractionAt < TimeSpan.FromSeconds(2);
        return !isUserExploring;
    }

    private string ValidateConnectionInputs()
    {
        if (string.IsNullOrWhiteSpace(ConnectionId))
        {
            return "Connection Id is required.";
        }

        if (string.IsNullOrWhiteSpace(BrokerUrl))
        {
            return "Broker URL is required.";
        }

        if (!Uri.TryCreate(BrokerUrl, UriKind.Absolute, out var brokerUri))
        {
            return "Broker URL must be an absolute URI (e.g. mqtt://localhost:1883).";
        }

        if (!IsSupportedScheme(brokerUri.Scheme))
        {
            return "Broker URL scheme must be mqtt, mqtts, ws, or wss.";
        }

        if (!AreSubscriptionsValid(Subscriptions))
        {
            return "Subscriptions must be comma-separated non-empty topics.";
        }

        return string.Empty;
    }

    private static bool IsSupportedScheme(string scheme)
    {
        return scheme.Equals("mqtt", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("mqtts", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreSubscriptionsValid(string rawSubscriptions)
    {
        if (string.IsNullOrWhiteSpace(rawSubscriptions))
        {
            return true;
        }

        var segments = rawSubscriptions.Split(',');
        return segments.All(segment => !string.IsNullOrWhiteSpace(segment));
    }

    [RelayCommand]
    private void ApplyTopicFilter()
    {
        RebuildTopicTree();
    }

    [RelayCommand]
    private void ClearTopicFilter()
    {
        if (!string.IsNullOrEmpty(TopicFilter))
        {
            TopicFilter = string.Empty;
            return;
        }

        RebuildTopicTree();
    }

    private string FormatSelectedPayload()
    {
        if (SelectedNode is null || string.IsNullOrWhiteSpace(SelectedNode.PayloadBase64))
        {
            return "(no payload)";
        }

        var payload = new Base64Message(SelectedNode.PayloadBase64);
        return SelectedPayloadMode switch
        {
            PayloadInspectorMode.Raw => payload.Format().Text,
            PayloadInspectorMode.Json => payload.Format("json").Text,
            PayloadInspectorMode.Hex => payload.ToHexString(),
            PayloadInspectorMode.Base64 => payload.Base64Value,
            _ => payload.Format().Text,
        };
    }

    private string BuildSelectedPayloadMetadata()
    {
        if (SelectedNode is null)
        {
            return "No topic selected.";
        }

        var received = SelectedNode.LastReceivedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        return $"Size: {SelectedNode.PayloadSizeBytes} bytes  |  QoS: {SelectedNode.QosLabel}  |  Retain: {SelectedNode.Retain}  |  Received: {received}";
    }

    private void RebuildSelectedTopicTimeline()
    {
        SelectedTopicTimeline.Clear();
        if (SelectedNode is null || string.IsNullOrWhiteSpace(SelectedNode.FullPath))
        {
            return;
        }

        var node = _topicTree.FindNode(SelectedNode.FullPath);
        if (node is null)
        {
            return;
        }

        var entries = node.MessageHistory
            .ToArray()
            .TakeLast(25)
            .ToArray();

        var timeline = new List<TopicTimelineEntryViewModel>(entries.Length);
        string previousPayload = string.Empty;
        var hasPrevious = false;
        for (var i = 0; i < entries.Length; i++)
        {
            var record = entries[i];
            var payload = record.Payload?.Format().Text ?? string.Empty;
            var isChanged = hasPrevious && !string.Equals(previousPayload, payload, StringComparison.Ordinal);
            var diffSummary = hasPrevious
                ? BuildDiffSummary(previousPayload, payload)
                : "initial";
            var preview = TruncatePayload(payload);
            var localTime = record.Received.ToLocalTime();

            timeline.Add(new TopicTimelineEntryViewModel(
                localTime.ToString("HH:mm:ss"),
                localTime.ToString("yyyy-MM-dd HH:mm:ss"),
                preview,
                isChanged,
                diffSummary));

            previousPayload = payload;
            hasPrevious = true;
        }

        for (var i = timeline.Count - 1; i >= 0; i--)
        {
            SelectedTopicTimeline.Add(timeline[i]);
        }
    }

    private static string BuildDiffSummary(string previous, string current)
    {
        if (string.Equals(previous, current, StringComparison.Ordinal))
        {
            return "same";
        }

        var prefix = 0;
        var maxPrefix = Math.Min(previous.Length, current.Length);
        while (prefix < maxPrefix && previous[prefix] == current[prefix])
        {
            prefix++;
        }

        var previousSuffixIndex = previous.Length - 1;
        var currentSuffixIndex = current.Length - 1;
        while (previousSuffixIndex >= prefix &&
               currentSuffixIndex >= prefix &&
               previous[previousSuffixIndex] == current[currentSuffixIndex])
        {
            previousSuffixIndex--;
            currentSuffixIndex--;
        }

        var removed = Math.Max(0, previousSuffixIndex - prefix + 1);
        var added = Math.Max(0, currentSuffixIndex - prefix + 1);
        return $"chg@{prefix}: -{removed}/+{added}";
    }

    private async Task CopyTextToClipboardAsync(string value, string label)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.Clipboard is null)
        {
            Status = $"Unable to copy {label}: clipboard is unavailable.";
            return;
        }

        await desktop.MainWindow.Clipboard.SetTextAsync(value ?? string.Empty);
        Status = $"Copied {label}.";
    }

    private void QueueLayoutPersistence()
    {
        if (_isLoadingLayout)
        {
            return;
        }

        _layoutPersistTimer.Stop();
        _layoutPersistTimer.Start();
    }

    private async void OnLayoutPersistTick(object? sender, EventArgs e)
    {
        _layoutPersistTimer.Stop();
        await SaveUiLayoutAsync();
    }

    private async Task LoadUiLayoutAsync()
    {
        try
        {
            _isLoadingLayout = true;
            var settings = await _configStore.LoadAsync<UiLayoutSettings>("uiLayout");
            if (settings is null)
            {
                return;
            }

            var left = settings.LeftPaneWidth is > 180 and < 1000 ? settings.LeftPaneWidth : 300;
            var right = settings.RightPaneWidth is > 220 and < 1200 ? settings.RightPaneWidth : 360;
            LeftPaneWidth = new GridLength(left, GridUnitType.Pixel);
            RightPaneWidth = new GridLength(right, GridUnitType.Pixel);
        }
        catch
        {
            // UI layout persistence is optional; ignore malformed or missing settings.
        }
        finally
        {
            _isLoadingLayout = false;
        }
    }

    private async Task SaveUiLayoutAsync()
    {
        var settings = new UiLayoutSettings
        {
            LeftPaneWidth = LeftPaneWidth.IsAbsolute ? LeftPaneWidth.Value : 300,
            RightPaneWidth = RightPaneWidth.IsAbsolute ? RightPaneWidth.Value : 360,
        };
        await _configStore.SaveAsync("uiLayout", settings);
    }

    private void TrackTopicActivity(MqttMessageDto message)
    {
        if (string.IsNullOrWhiteSpace(message.Topic))
        {
            return;
        }

        var preview = TruncatePayload(message.Payload?.Format().Text ?? string.Empty);
        _lastMessageByPath[message.Topic] = new TopicActivity(preview, DateTimeOffset.UtcNow);
    }

    private bool HasRecentActivity()
    {
        if (_lastMessageByPath.Count == 0)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        return _lastMessageByPath.Values.Any(activity =>
            now - activity.ReceivedAt <= TimeSpan.FromSeconds(2));
    }

    private static string TruncatePayload(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        var collapsed = payload.Replace('\r', ' ').Replace('\n', ' ');
        return collapsed.Length <= 80
            ? collapsed
            : $"{collapsed[..77]}...";
    }

    private static string FormatRelativeTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromSeconds(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return $"{(int)elapsed.TotalSeconds}s ago";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        return $"{(int)elapsed.TotalHours}h ago";
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
        _layoutPersistTimer.Stop();
        await SaveUiLayoutAsync();
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

    private sealed class TopicActivity(string lastMessagePreview, DateTimeOffset receivedAt)
    {
        public string LastMessagePreview { get; } = lastMessagePreview;
        public DateTimeOffset ReceivedAt { get; } = receivedAt;
    }

    private sealed class UiLayoutSettings
    {
        public double LeftPaneWidth { get; init; } = 300;
        public double RightPaneWidth { get; init; } = 360;
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
