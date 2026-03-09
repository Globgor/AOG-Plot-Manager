using System.Net;
using System.Net.Sockets;

namespace PlotManager.Core.Protocol;

using PlotManager.Core.Models;

/// <summary>
/// UDP client for receiving AgOpenGPS broadcasts and sending section control overrides.
///
/// AOG broadcasts GPS data and section control packets on UDP.
/// This client listens on the configured port, parses relevant PGNs,
/// and provides events for the PlotModeController to react to.
/// </summary>
public class AogUdpClient : IDisposable
{
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    /// <summary>Port to listen on (AOG sends to machine modules).</summary>
    public int ListenPort { get; init; } = AogProtocol.AogSendPort;

    /// <summary>Port to send overridden packets to (machine module port).</summary>
    public int TargetPort { get; init; } = AogProtocol.AogSendPort;

    /// <summary>Target IP for sending overridden section control (typically broadcast).</summary>
    public IPAddress TargetAddress { get; init; } = IPAddress.Broadcast;

    /// <summary>Fires when a GPS update is received from AOG.</summary>
    public event Action<AogGpsData>? OnGpsUpdate;

    /// <summary>Fires when a section control packet is received from AOG.</summary>
    public event Action<ushort, byte[]>? OnSectionControl;

    /// <summary>Whether the client is currently listening.</summary>
    public bool IsListening => _receiveTask != null && !_receiveTask.IsCompleted;

    /// <summary>
    /// Starts listening for AOG UDP broadcasts.
    /// </summary>
    public void Start()
    {
        if (IsListening) return;

        _cts = new CancellationTokenSource();
        _listener = new UdpClient(ListenPort);
        _listener.EnableBroadcast = true;

        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// Stops listening and releases resources.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Close();
        _listener?.Dispose();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Sends a section control packet (PGN 229) to the machine module.
    /// Used to inject overridden section states.
    /// </summary>
    public void SendSectionControl(ushort valveMask)
    {
        byte[] packet = AogProtocol.BuildSectionControlPacket(valveMask);
        SendRaw(packet);
    }

    /// <summary>
    /// Sends an overridden copy of an original AOG section control packet.
    /// </summary>
    public void SendOverriddenPacket(byte[] originalPacket, ushort overrideMask)
    {
        byte[] modified = AogProtocol.OverrideSectionPacket(originalPacket, overrideMask);
        SendRaw(modified);
    }

    /// <summary>
    /// Sends raw bytes to the target endpoint.
    /// </summary>
    public void SendRaw(byte[] data)
    {
        using var sender = new UdpClient();
        sender.EnableBroadcast = true;
        sender.Send(data, data.Length, new IPEndPoint(TargetAddress, TargetPort));
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    // ════════════════════════════════════════════════════════════════════
    // Private
    // ════════════════════════════════════════════════════════════════════

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_listener == null) break;

                UdpReceiveResult result = await _listener.ReceiveAsync(ct);
                ProcessPacket(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                // Network error — skip and retry
            }
        }
    }

    private void ProcessPacket(byte[] data)
    {
        if (!AogProtocol.IsValidPacket(data))
            return;

        byte pgn = AogProtocol.GetPgn(data);

        switch (pgn)
        {
            case AogProtocol.PgnSectionControl:
                ProcessSectionControl(data);
                break;

            case AogProtocol.PgnGpsData:
                ProcessGpsData(data);
                break;
        }
    }

    private void ProcessSectionControl(byte[] data)
    {
        ushort originalMask = AogProtocol.ExtractSectionMask(data);
        OnSectionControl?.Invoke(originalMask, data);
    }

    private void ProcessGpsData(byte[] data)
    {
        // PGN 253 GPS Data format (simplified):
        // Bytes 5-8:  Latitude  (int32 LE, scaled by 1e-7)
        // Bytes 9-12: Longitude (int32 LE, scaled by 1e-7)
        // Bytes 13-14: Heading  (uint16 LE, scaled by 10, degrees)
        // Bytes 15-16: Speed    (uint16 LE, scaled by 10, km/h)
        // Byte 17:     Fix Quality (GGA quality field)
        // Bytes 18-19: COG      (uint16 LE, scaled by 10, degrees)
        if (data.Length < AogProtocol.OffsetData + 12) return;

        int offset = AogProtocol.OffsetData;

        int latRaw = BitConverter.ToInt32(data, offset);
        int lonRaw = BitConverter.ToInt32(data, offset + 4);
        ushort headingRaw = BitConverter.ToUInt16(data, offset + 8);
        ushort speedRaw = BitConverter.ToUInt16(data, offset + 10);

        // Extract fix quality if available
        var fixQuality = PlotManager.Core.Models.GpsFixQuality.RtkFix;
        if (data.Length >= AogProtocol.OffsetData + 13)
        {
            byte fixByte = data[offset + 12];
            fixQuality = fixByte switch
            {
                0 => PlotManager.Core.Models.GpsFixQuality.NoFix,
                1 => PlotManager.Core.Models.GpsFixQuality.Autonomous,
                2 => PlotManager.Core.Models.GpsFixQuality.Dgps,
                4 => PlotManager.Core.Models.GpsFixQuality.RtkFix,
                5 => PlotManager.Core.Models.GpsFixQuality.RtkFloat,
                _ => PlotManager.Core.Models.GpsFixQuality.Autonomous,
            };
        }

        // Extract COG if available
        double cogDegrees = headingRaw / 10.0; // Default to heading
        if (data.Length >= AogProtocol.OffsetData + 15)
        {
            ushort cogRaw = BitConverter.ToUInt16(data, offset + 13);
            cogDegrees = cogRaw / 10.0;
        }

        var gpsData = new AogGpsData
        {
            Latitude = latRaw * 1e-7,
            Longitude = lonRaw * 1e-7,
            HeadingDegrees = headingRaw / 10.0,
            SpeedKmh = speedRaw / 10.0,
            FixQuality = fixQuality,
            CourseOverGroundDegrees = cogDegrees,
        };

        OnGpsUpdate?.Invoke(gpsData);
    }
}
