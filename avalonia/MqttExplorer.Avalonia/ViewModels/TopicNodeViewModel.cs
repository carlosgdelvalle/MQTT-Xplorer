using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MqttExplorer.Avalonia.ViewModels;

public sealed partial class TopicNodeViewModel : ObservableObject
{
    public TopicNodeViewModel(
        string name,
        string fullPath,
        string payloadText,
        int depth = 0,
        string? lastMessagePreview = null,
        string? receivedAgo = null,
        bool hasRecentActivity = false,
        string? payloadBase64 = null,
        int payloadSizeBytes = 0,
        string? qosLabel = null,
        bool retain = false,
        DateTimeOffset? lastReceivedAt = null)
    {
        _name = name;
        _fullPath = fullPath;
        _payloadText = payloadText;
        _depth = depth;
        _lastMessagePreview = lastMessagePreview ?? string.Empty;
        _receivedAgo = receivedAgo ?? string.Empty;
        _hasRecentActivity = hasRecentActivity;
        _payloadBase64 = payloadBase64 ?? string.Empty;
        _payloadSizeBytes = payloadSizeBytes;
        _qosLabel = qosLabel ?? string.Empty;
        _retain = retain;
        _lastReceivedAt = lastReceivedAt;
        _isExpanded = true;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _fullPath;
    [ObservableProperty] private string _payloadText;
    [ObservableProperty] private int _depth;
    [ObservableProperty] private string _lastMessagePreview;
    [ObservableProperty] private string _receivedAgo;
    [ObservableProperty] private bool _hasRecentActivity;
    [ObservableProperty] private string _payloadBase64;
    [ObservableProperty] private int _payloadSizeBytes;
    [ObservableProperty] private string _qosLabel;
    [ObservableProperty] private bool _retain;
    [ObservableProperty] private DateTimeOffset? _lastReceivedAt;
    [ObservableProperty] private bool _isExpanded;
    public ObservableCollection<TopicNodeViewModel> Children { get; } = [];

    public string DisplayName => $"{new string(' ', Depth * 2)}{Name}";
    public string ActivityIndicator => HasRecentActivity ? "●" : string.Empty;
}
