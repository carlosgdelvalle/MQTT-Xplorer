using MqttExplorer.Core.Contracts;
using MqttExplorer.Core.Model;
using MqttExplorer.Infrastructure;

namespace MqttExplorer.Tests;

public class CoreParityTests
{
    [Fact]
    public void Base64Message_RoundTripsUnicode()
    {
        var message = Base64Message.FromString("hello mqtt");
        Assert.Equal("hello mqtt", message.ToUnicodeString());
        Assert.NotEmpty(message.ToHexString());
    }

    [Fact]
    public void RingBuffer_EnforcesCapacity()
    {
        var ring = new RingBuffer<TestItem>(capacity: 10, maxItems: 10);
        ring.Add(new TestItem(4));
        ring.Add(new TestItem(4));
        ring.Add(new TestItem(4));

        Assert.Equal(3, ring.Count);
        Assert.Equal(4, ring.Last?.Length);
    }

    [Fact]
    public void TopicTree_AppliesQueuedMessages()
    {
        var tree = new TopicTree();
        tree.Enqueue(new MqttMessageDto("home/lamp/state", Base64Message.FromString("on"), QoS.AtMostOnce, false, null));
        tree.ApplyUnmergedChanges();

        var node = tree.FindNode("home/lamp/state");
        Assert.NotNull(node);
        Assert.Equal("on", node!.Message?.Payload?.ToUnicodeString());
    }

    [Fact]
    public async Task JsonConfigStore_SavesAndLoadsData()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mqtt-xplorer-tests-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonConfigStore(file);
            var profiles = new List<ConnectionProfile>
            {
                new()
                {
                    Id = "local",
                    Name = "Local",
                    Options = new MqttConnectionOptions
                    {
                        Url = "mqtt://localhost:1883",
                        Subscriptions = [new Subscription("#", QoS.AtMostOnce)],
                    },
                    ConfigVersion = 1,
                },
            };

            await store.SaveAsync("connectionsV2", profiles);
            var loaded = await store.LoadAsync<List<ConnectionProfile>>("connectionsV2");

            Assert.NotNull(loaded);
            Assert.Single(loaded!);
            Assert.Equal("local", loaded[0].Id);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private sealed record TestItem(int Length) : ILengthAware;
}
