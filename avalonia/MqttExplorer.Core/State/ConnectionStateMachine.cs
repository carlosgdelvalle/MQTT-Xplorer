using MqttExplorer.Core.Contracts;

namespace MqttExplorer.Core.State;

public sealed class ConnectionStateMachine
{
    private ConnectionState _state = new(false, false, null);

    public event Action<ConnectionState>? Updated;

    public ConnectionState Current => _state;

    public void SetConnecting()
    {
        _state = _state with { Connecting = true, Connected = false };
        Updated?.Invoke(_state);
    }

    public void SetConnected(bool connected)
    {
        _state = new ConnectionState(false, connected, null);
        Updated?.Invoke(_state);
    }

    public void SetError(Exception exception)
    {
        _state = _state with { Error = exception.Message };
        Updated?.Invoke(_state);
    }
}
