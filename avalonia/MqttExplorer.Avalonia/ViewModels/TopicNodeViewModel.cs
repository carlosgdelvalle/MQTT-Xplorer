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
        bool hasRecentActivity = false)
    {
        _name = name;
        _fullPath = fullPath;
        _payloadText = payloadText;
        _depth = depth;
        _lastMessagePreview = lastMessagePreview ?? string.Empty;
        _receivedAgo = receivedAgo ?? string.Empty;
        _hasRecentActivity = hasRecentActivity;
        _isExpanded = true;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _fullPath;
    [ObservableProperty] private string _payloadText;
    [ObservableProperty] private int _depth;
    [ObservableProperty] private string _lastMessagePreview;
    [ObservableProperty] private string _receivedAgo;
    [ObservableProperty] private bool _hasRecentActivity;
    [ObservableProperty] private bool _isExpanded;
    public ObservableCollection<TopicNodeViewModel> Children { get; } = [];

    public string DisplayName => $"{new string(' ', Depth * 2)}{Name}";
    public string ActivityIndicator => HasRecentActivity ? "●" : string.Empty;
}
