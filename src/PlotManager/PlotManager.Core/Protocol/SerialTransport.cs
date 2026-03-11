using System.IO.Ports;

namespace PlotManager.Core.Protocol;

/// <summary>
/// USB Serial transport for Teensy 4.1 communication.
/// Primary transport — reliable, low-latency, no network configuration required.
/// </summary>
public class SerialTransport : ITransport
{
    private SerialPort? _port;
    private readonly string _portName;
    private readonly int _baudRate;

    public bool IsConnected => _port?.IsOpen ?? false;

    public event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>
    /// Creates a new SerialTransport.
    /// </summary>
    /// <param name="portName">COM port name (e.g., "COM3" on Windows, "/dev/ttyACM0" on Linux).</param>
    /// <param name="baudRate">Baud rate, must match Teensy firmware (default: 115200).</param>
    public SerialTransport(string portName, int baudRate = 115200)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _port = new SerialPort(_portName, _baudRate)
        {
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            ReadTimeout = 500,
            WriteTimeout = 500,
            DtrEnable = true,    // Required for Teensy auto-reset
            RtsEnable = true,
        };

        // R10 FIX: Run blocking Open on thread pool to avoid freezing UI
        await Task.Run(() => _port.Open(), ct);
        ConnectionStateChanged?.Invoke(this, true);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_port?.IsOpen == true)
        {
            _port.Close();
            ConnectionStateChanged?.Invoke(this, false);
        }
        return Task.CompletedTask;
    }

    public Task SendAsync(byte[] data, CancellationToken ct = default)
    {
        if (_port?.IsOpen != true)
            throw new InvalidOperationException("Serial port is not open.");

        _port.Write(data, 0, data.Length);
        return Task.CompletedTask;
    }

    public async Task<byte[]> ReceiveAsync(int timeoutMs = 100, CancellationToken ct = default)
    {
        if (_port?.IsOpen != true)
            return Array.Empty<byte>();

        var buffer = new byte[256];

        try
        {
            // R11 FIX: Use BaseStream.ReadAsync instead of blocking ThreadPool via Task.Run
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            int bytesRead = await _port.BaseStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            return buffer[..bytesRead];
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<byte>();
        }
        catch (TimeoutException)
        {
            return Array.Empty<byte>();
        }
    }

    public void Dispose()
    {
        _port?.Close();
        _port?.Dispose();
        _port = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Lists available serial ports on the system.
    /// </summary>
    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();
}
