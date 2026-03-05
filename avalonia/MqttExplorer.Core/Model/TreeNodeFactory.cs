using MqttExplorer.Core.Contracts;

namespace MqttExplorer.Core.Model;

public static class TreeNodeFactory
{
    private static long _messageCounter;

    public static TreeNode FromMessage(MqttMessageDto message, DateTimeOffset? receiveDate = null)
    {
        var node = new TreeNode();
        var edges = message.Topic.Split('/');

        node.SetMessage(new MessageRecord(
            message.Payload,
            message.MessageId,
            message.Retain,
            message.Qos,
            message.Payload?.Length ?? 0,
            receiveDate ?? DateTimeOffset.UtcNow,
            _messageCounter++));

        InsertNodeAtPosition(edges, node);
        return node;
    }

    private static void InsertNodeAtPosition(IEnumerable<string> edgeNames, TreeNode node)
    {
        TreeNode currentNode = TopicTree.CreateDetachedRoot();
        Edge? edge = null;

        foreach (var edgeName in edgeNames)
        {
            edge = new Edge(edgeName);
            currentNode.AddEdge(edge);
            currentNode = new TreeNode { SourceEdge = edge };
            edge.Target = currentNode;
        }

        node.SourceEdge = edge;
        if (node.SourceEdge is not null)
        {
            node.SourceEdge.Target = node;
        }
    }
}
