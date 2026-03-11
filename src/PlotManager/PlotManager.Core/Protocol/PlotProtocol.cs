namespace PlotManager.Core.Protocol;

/// <summary>
/// Protocol constants and message builder/parser for PlotManager ↔ Teensy communication.
///
/// Packet format:
///   [SYNC_1] [SYNC_2] [CMD] [DATA...] [CRC]
///
/// Where:
///   SYNC_1  = 0xAA
///   SYNC_2  = 0x55
///   CMD     = Command byte (see Commands class)
///   DATA    = Variable-length payload (depends on CMD)
///   CRC     = XOR of all bytes from CMD to end of DATA
/// </summary>
public static class PlotProtocol
{
    public const byte Sync1 = 0xAA;
    public const byte Sync2 = 0x55;

    /// <summary>Command bytes for PlotManager → Teensy messages.</summary>
    public static class Commands
    {
        /// <summary>Set valve states. Data: [MSB] [LSB] — 14-bit valve mask.</summary>
        public const byte SetValves = 0x01;

        /// <summary>Prime a section. Data: [SectionMask MSB] [SectionMask LSB] [Duration MSB] [Duration LSB].</summary>
        public const byte Prime = 0x02;

        /// <summary>Heartbeat/keepalive. No data.</summary>
        public const byte Heartbeat = 0x03;

        /// <summary>Emergency stop — close all valves immediately. No data.</summary>
        public const byte EmergencyStop = 0x04;
    }

    /// <summary>Response bytes for Teensy → PlotManager messages.</summary>
    public static class Responses
    {
        /// <summary>Status response. Data: [ValveMask MSB] [ValveMask LSB] [ErrorFlags].</summary>
        public const byte Status = 0x80;

        /// <summary>Acknowledgement. Data: [AckedCmd].</summary>
        public const byte Ack = 0x81;
    }

    /// <summary>
    /// Builds a SET_VALVES command packet.
    /// </summary>
    /// <param name="valveMask">14-bit mask where bit N = section N (0-13) state. 1=OPEN, 0=CLOSED.</param>
    public static byte[] BuildSetValves(ushort valveMask)
    {
        // Mask to 14 bits
        valveMask &= 0x3FFF;
        byte msb = (byte)(valveMask >> 8);
        byte lsb = (byte)(valveMask & 0xFF);
        return BuildPacket(Commands.SetValves, msb, lsb);
    }

    /// <summary>
    /// Builds a PRIME command packet.
    /// </summary>
    /// <param name="sectionMask">14-bit mask of sections to prime.</param>
    /// <param name="durationMs">Prime duration in milliseconds (max 65535).</param>
    public static byte[] BuildPrime(ushort sectionMask, ushort durationMs)
    {
        sectionMask &= 0x3FFF;
        return BuildPacket(Commands.Prime,
            (byte)(sectionMask >> 8), (byte)(sectionMask & 0xFF),
            (byte)(durationMs >> 8), (byte)(durationMs & 0xFF));
    }

    /// <summary>
    /// Builds a HEARTBEAT command packet.
    /// </summary>
    public static byte[] BuildHeartbeat()
    {
        return BuildPacket(Commands.Heartbeat);
    }

    /// <summary>
    /// Builds an EMERGENCY_STOP command packet.
    /// </summary>
    public static byte[] BuildEmergencyStop()
    {
        return BuildPacket(Commands.EmergencyStop);
    }

    /// <summary>
    /// Attempts to parse a response packet from raw bytes.
    /// Returns null if the data is invalid or incomplete.
    /// </summary>
    /// <remarks>
    /// S5 NOTE: The CRC is a single-byte XOR over CMD + DATA. This is intentionally
    /// simple — it matches the firmware implementation (Teensy 4.1) and is adequate
    /// for the wired USB serial link (0.5m cable, no EMI exposure). A CRC-16 would
    /// add complexity without meaningful benefit for this use case.
    /// </remarks>
    public static ParsedResponse? ParseResponse(byte[] data)
    {
        if (data.Length < 4) return null; // Minimum: SYNC1 + SYNC2 + CMD + CRC
        if (data[0] != Sync1 || data[1] != Sync2) return null;

        byte cmd = data[2];
        byte[] payload = data[3..^1];
        byte receivedCrc = data[^1];

        // Verify CRC
        byte expectedCrc = cmd;
        foreach (byte b in payload)
        {
            expectedCrc ^= b;
        }

        if (receivedCrc != expectedCrc) return null;

        return new ParsedResponse(cmd, payload);
    }

    /// <summary>
    /// Builds a packet with header, command, data, and CRC.
    /// </summary>
    private static byte[] BuildPacket(byte command, params byte[] data)
    {
        var packet = new byte[3 + data.Length + 1]; // SYNC1 + SYNC2 + CMD + DATA + CRC
        packet[0] = Sync1;
        packet[1] = Sync2;
        packet[2] = command;

        byte crc = command;
        for (int i = 0; i < data.Length; i++)
        {
            packet[3 + i] = data[i];
            crc ^= data[i];
        }
        packet[^1] = crc;

        return packet;
    }
}

/// <summary>
/// Represents a parsed response from Teensy.
/// </summary>
public readonly record struct ParsedResponse(byte Command, byte[] Payload)
{
    /// <summary>Whether this is a status response.</summary>
    public bool IsStatus => Command == PlotProtocol.Responses.Status;

    /// <summary>Whether this is an acknowledgement.</summary>
    public bool IsAck => Command == PlotProtocol.Responses.Ack;

    /// <summary>
    /// Extracts the valve mask from a STATUS response.
    /// </summary>
    public ushort GetValveMask()
    {
        if (!IsStatus || Payload.Length < 2) return 0;
        return (ushort)((Payload[0] << 8) | Payload[1]);
    }

    /// <summary>
    /// Extracts the error flags from a STATUS response.
    /// </summary>
    public byte GetErrorFlags()
    {
        if (!IsStatus || Payload.Length < 3) return 0;
        return Payload[2];
    }
}
