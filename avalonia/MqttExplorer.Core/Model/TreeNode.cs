namespace MqttExplorer.Core.Model;

public class TreeNode
{
    private string? _cachedPath;
    private IReadOnlyList<TreeNode>? _cachedChildTopics;
    private int? _cachedLeafMessageCount;
    private int? _cachedChildTopicCount;

    public Edge? SourceEdge { get; set; }
    public MessageRecord? Message { get; private set; }
    public RingBuffer<MessageRecord> MessageHistory { get; private set; } = new(20_000, 100);
    public Dictionary<string, Edge> Edges { get; } = new();
    public List<Edge> EdgeArray { get; private set; } = new();
    public bool IsTree { get; set; }
    public string TreeHash { get; set; } = Guid.NewGuid().ToString("N");
    public long Messages { get; private set; }
    public DateTimeOffset LastUpdate { get; private set; } = DateTimeOffset.UtcNow;
    public string Type { get; set; } = "json";

    public bool HasMessage => Message?.Payload is not null && Message.Length != 0;

    public void SetMessage(MessageRecord message)
    {
        MessageHistory.Add(message);
        Message = message;
        Messages++;
        LastUpdate = DateTimeOffset.UtcNow;
    }

    public void AddEdge(Edge edge, bool emitUpdate = false)
    {
        Edges[edge.Name] = edge;
        EdgeArray.Add(edge);
        edge.Source = this;
        edge.Target?.RemoveFromTreeIfEmpty();
        if (emitUpdate)
        {
            ResetCache();
        }
    }

    public void RemoveEdge(Edge edge)
    {
        Edges.Remove(edge.Name);
        EdgeArray = Edges.Values.ToList();
        RemoveFromTreeIfEmpty();
        ResetCache();
    }

    public void RemoveFromTreeIfEmpty()
    {
        if (!HasMessage && EdgeArray.Count == 0)
        {
            RemoveFromParent();
        }
    }

    public TreeNode FirstNode() => SourceEdge?.Source?.FirstNode() ?? this;

    public string Path()
    {
        if (_cachedPath is not null)
        {
            return _cachedPath;
        }

        _cachedPath = string.Join('/',
            Branch()
                .Select(node => node.SourceEdge?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name)));
        return _cachedPath;
    }

    public IReadOnlyList<TreeNode> Branch()
    {
        var previous = Previous();
        if (previous is null)
        {
            return new[] { this };
        }

        return previous.Branch().Concat(new[] { this }).ToArray();
    }

    public void UpdateWithNode(TreeNode node)
    {
        if (node.Message is not null)
        {
            SetMessage(node.Message);
        }

        RemoveFromTreeIfEmpty();
        MergeEdges(node);
        ResetCache();
    }

    public TreeNode? FindNode(string path)
    {
        var topics = path.Split('/', StringSplitOptions.None);
        return FindChild(topics);
    }

    public int LeafMessageCount()
    {
        if (_cachedLeafMessageCount.HasValue)
        {
            return _cachedLeafMessageCount.Value;
        }

        _cachedLeafMessageCount = EdgeArray
            .Where(edge => edge.Target is not null)
            .Select(edge => edge.Target!.LeafMessageCount())
            .Sum() + (int)Messages;
        return _cachedLeafMessageCount.Value;
    }

    public int ChildTopicCount()
    {
        if (_cachedChildTopicCount.HasValue)
        {
            return _cachedChildTopicCount.Value;
        }

        _cachedChildTopicCount = EdgeArray
            .Where(edge => edge.Target is not null)
            .Select(edge => edge.Target!.ChildTopicCount())
            .Sum() + (HasMessage ? 1 : 0);
        return _cachedChildTopicCount.Value;
    }

    public IReadOnlyList<TreeNode> ChildTopics()
    {
        if (_cachedChildTopics is not null)
        {
            return _cachedChildTopics;
        }

        var initial = HasMessage ? new[] { this } : Array.Empty<TreeNode>();
        _cachedChildTopics = EdgeArray
            .Where(edge => edge.Target is not null)
            .SelectMany(edge => edge.Target!.ChildTopics())
            .Concat(initial)
            .ToArray();
        return _cachedChildTopics;
    }

    private TreeNode? Previous() => SourceEdge?.Source;

    private TreeNode? FindChild(IReadOnlyList<string> edges)
    {
        if (edges.Count == 0)
        {
            return this;
        }

        if (!Edges.TryGetValue(edges[0], out var next))
        {
            return null;
        }

        return next.Target?.FindChild(edges.Skip(1).ToArray());
    }

    private void MergeEdges(TreeNode node)
    {
        var didUpdate = false;
        foreach (var (key, edge) in node.Edges)
        {
            if (Edges.TryGetValue(key, out var matching))
            {
                if (matching.Target is not null && edge.Target is not null)
                {
                    matching.Target.UpdateWithNode(edge.Target);
                }
                continue;
            }

            AddEdge(edge);
            didUpdate = true;
        }

        if (didUpdate)
        {
            ResetCache();
        }
    }

    private void RemoveFromParent()
    {
        var previous = Previous();
        if (previous is null || SourceEdge is null)
        {
            return;
        }

        previous.RemoveEdge(SourceEdge);
        if (!IsTree)
        {
            Destroy();
        }
    }

    private void Destroy()
    {
        foreach (var edge in EdgeArray)
        {
            edge.Target?.Destroy();
        }

        EdgeArray = [];
        Edges.Clear();
        MessageHistory = new RingBuffer<MessageRecord>(1, 1);
        Message = null;
    }

    private void ResetCache()
    {
        LastUpdate = DateTimeOffset.UtcNow;
        _cachedPath = null;
        _cachedChildTopics = null;
        _cachedLeafMessageCount = null;
        _cachedChildTopicCount = null;
    }
}
