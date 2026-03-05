using System.Buffers;
using MQTTnet;
using MQTTnet.Protocol;
using MqttExplorer.Core.Abstractions;
using MqttExplorer.Core.Contracts;
using MqttExplorer.Core.State;

namespace MqttExplorer.Infrastructure;

public sealed class MqttDataSource : IMqttDataSource
{
    private readonly IMqttClient _client;
    private readonly ConnectionStateMachine _stateMachine = new();
    private MqttConnectionOptions? _options;

    public MqttDataSource()
    {
        _client = new MqttClientFactory().CreateMqttClient();
        _stateMachine.Updated += state => StateChanged?.Invoke(state);

        _client.ConnectedAsync += async _ =>
        {
            _stateMachine.SetConnected(true);
            if (_options is null)
            {
                return;
            }

            foreach (var subscription in _options.Subscriptions)
            {
                await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(new MqttTopicFilterBuilder()
                        .WithTopic(subscription.Topic)
                        .WithQualityOfServiceLevel(ToMqttQuality(subscription.Qos))
                        .Build())
                    .Build());
            }
        };

        _client.DisconnectedAsync += _ =>
        {
            _stateMachine.SetConnected(false);
            return Task.CompletedTask;
        };

        _client.ApplicationMessageReceivedAsync += args =>
        {
            var payload = args.ApplicationMessage.Payload.ToArray();
            MessageReceived?.Invoke(new MqttMessageDto(
                args.ApplicationMessage.Topic,
                payload.Length == 0 ? null : Base64Message.FromBytes(payload),
                ToQoS(args.ApplicationMessage.QualityOfServiceLevel),
                args.ApplicationMessage.Retain,
                null));
            return Task.CompletedTask;
        };
    }

    public event Action<ConnectionState>? StateChanged;
    public event Action<MqttMessageDto>? MessageReceived;

    public async Task ConnectAsync(MqttConnectionOptions options, CancellationToken cancellationToken = default)
    {
        _options = options;
        _stateMachine.SetConnecting();

        try
        {
            var normalizedUrl = NormalizeUrl(options.Url, options.Tls);
            var uri = new Uri(normalizedUrl);
            var clientId = string.IsNullOrWhiteSpace(options.ClientId) ? null : options.ClientId;
            var cleanSession = string.IsNullOrWhiteSpace(clientId);
            var builder = new MqttClientOptionsBuilder()
                .WithClientId(clientId)
                .WithCredentials(options.Username, options.Password)
                .WithCleanSession(cleanSession);

            if (uri.Scheme.StartsWith("ws", StringComparison.OrdinalIgnoreCase))
            {
                builder.WithWebSocketServer(websocket => websocket.WithUri(normalizedUrl));
            }
            else
            {
                builder.WithTcpServer(uri.Host, uri.Port);
            }

            if (options.Tls)
            {
                builder.WithTlsOptions(new MqttClientTlsOptionsBuilder()
                    .WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12 |
                                      System.Security.Authentication.SslProtocols.Tls13)
                    .WithAllowUntrustedCertificates(!options.CertValidation)
                    .WithIgnoreCertificateChainErrors(!options.CertValidation)
                    .WithIgnoreCertificateRevocationErrors(!options.CertValidation)
                    .Build());
            }

            await _client.ConnectAsync(builder.Build(), cancellationToken);
        }
        catch (Exception ex)
        {
            _stateMachine.SetError(ex);
            throw;
        }
    }

    public async Task PublishAsync(MqttMessageDto message, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected)
        {
            return;
        }

        var mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic(message.Topic)
            .WithQualityOfServiceLevel(ToMqttQuality(message.Qos))
            .WithRetainFlag(message.Retain)
            .WithPayload(message.Payload?.ToBytes() ?? Array.Empty<byte>())
            .Build();

        await _client.PublishAsync(mqttMessage, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _client?.Dispose();
    }

    private static string NormalizeUrl(string url, bool tls)
    {
        if (!tls)
        {
            return url;
        }

        return url
            .Replace("mqtt://", "mqtts://", StringComparison.OrdinalIgnoreCase)
            .Replace("ws://", "wss://", StringComparison.OrdinalIgnoreCase);
    }

    private static QoS ToQoS(MqttQualityOfServiceLevel qos) =>
        qos switch
        {
            MqttQualityOfServiceLevel.AtMostOnce => QoS.AtMostOnce,
            MqttQualityOfServiceLevel.AtLeastOnce => QoS.AtLeastOnce,
            MqttQualityOfServiceLevel.ExactlyOnce => QoS.ExactlyOnce,
            _ => QoS.AtMostOnce,
        };

    private static MqttQualityOfServiceLevel ToMqttQuality(QoS qos) =>
        qos switch
        {
            QoS.AtMostOnce => MqttQualityOfServiceLevel.AtMostOnce,
            QoS.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
            QoS.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce,
        };
}
