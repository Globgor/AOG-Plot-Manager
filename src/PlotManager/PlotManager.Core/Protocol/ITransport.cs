namespace PlotManager.Core.Protocol;

/// <summary>
/// Abstraction for the transport layer between PlotManager and Teensy.
/// Implementations: SerialTransport (USB), UdpTransport (Ethernet).
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>Whether the transport is currently connected.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the connection to the device.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Closes the connection gracefully.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Sends raw bytes to the device.</summary>
    Task SendAsync(byte[] data, CancellationToken ct = default);

    /// <summary>Receives raw bytes from the device. Returns empty if timeout.</summary>
    Task<byte[]> ReceiveAsync(int timeoutMs = 100, CancellationToken ct = default);

    /// <summary>Raised when the connection state changes.</summary>
    event EventHandler<bool>? ConnectionStateChanged;
}
