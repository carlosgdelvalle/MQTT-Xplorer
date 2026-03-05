using MqttExplorer.Core.Contracts;

namespace MqttExplorer.Core.Model;

public sealed class TopicTree : TreeNode
{
    private readonly ChangeBuffer _unmergedMessages = new();
    private bool _paused;

    public event Action? DidUpdate;

    public string? ConnectionId { get; private set; }
    public Func<TreeNode, bool>? NodeFilter { get; private set; }

    public TopicTree()
    {
        IsTree = true;
        TreeHash = Guid.NewGuid().ToString("N");
    }

    public static TreeNode CreateDetachedRoot() => new() { IsTree = true };

    public void Configure(string connectionId, Func<TreeNode, bool>? nodeFilter = null)
    {
        ConnectionId = connectionId;
        NodeFilter = nodeFilter;
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public void Enqueue(MqttMessageDto message)
    {
        _unmergedMessages.Push(message);
    }

    public void ApplyUnmergedChanges()
    {
        if (_paused)
        {
            return;
        }

        foreach (var item in _unmergedMessages.PopAll())
        {
            var node = TreeNodeFactory.FromMessage(item.Message, item.Received);
            if (NodeFilter is null || NodeFilter(node))
            {
                UpdateWithNode(node.FirstNode());
            }
        }

        DidUpdate?.Invoke();
    }
}
