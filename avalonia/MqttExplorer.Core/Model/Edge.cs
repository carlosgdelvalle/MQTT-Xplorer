using System.Security.Cryptography;
using System.Text;

namespace MqttExplorer.Core.Model;

public sealed class Edge
{
    private string? _cachedHash;

    public Edge(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public TreeNode? Target { get; set; }
    public TreeNode? Source { get; set; }

    public string Hash()
    {
        if (!string.IsNullOrEmpty(_cachedHash))
        {
            return _cachedHash;
        }

        var previous = Source?.SourceEdge?.Hash() ?? (Source?.IsTree == true ? Source.TreeHash : string.Empty);
        var value = $"{previous}{Name}";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        _cachedHash = $"H{Convert.ToHexString(bytes)}";
        return _cachedHash;
    }
}
