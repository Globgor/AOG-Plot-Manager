namespace PlotManager.Core.Protocol;

/// <summary>
/// Constants and helpers for AgOpenGPS communication protocol.
/// AOG uses custom UDP packets with preamble 0x80 0x81.
///
/// PGN 229 (0xE5) — Section Control (64 sections):
///   [0x80] [0x81] [Src=0x7F] [PGN=0xE5] [Len=10]
///   [Sec1-8] [Sec9-16] [Sec17-24] [Sec25-32]
///   [Sec33-40] [Sec41-48] [Sec49-56] [Sec57-64]
///   [Lspeed] [Rspeed] [CRC]
///
/// PGN 253 (0xFD) — GPS Data:
///   [0x80] [0x81] [Src=0x7F] [PGN=0xFD] [Len]
///   [Lat 4 bytes LE] [Lon 4 bytes LE] [Heading 2 bytes LE]
///   [Speed 2 bytes LE] [...]
/// </summary>
public static class AogProtocol
{
    // ── Preamble & Source ──
    public const byte Preamble1 = 0x80;
    public const byte Preamble2 = 0x81;
    public const byte SourceAog = 0x7F;

    // ── PGN Numbers ──
    public const byte PgnSectionControl = 0xE5;  // PGN 229
    public const byte PgnGpsData = 0xFD;          // PGN 253

    // ── Header offsets ──
    public const int OffsetPreamble1 = 0;
    public const int OffsetPreamble2 = 1;
    public const int OffsetSource = 2;
    public const int OffsetPgn = 3;
    public const int OffsetLength = 4;
    public const int OffsetData = 5;

    // ── Section control data offsets (relative to OffsetData) ──
    public const int SectionControlDataLen = 10;
    public const int HeaderLen = 5;

    // ── UDP ports ──
    public const int AogSendPort = 8888;    // AOG sends to modules
    public const int AogListenPort = 9999;  // AOG listens from modules

    /// <summary>
    /// Packs a 14-bit valve mask into AOG-compatible section bytes (RelayLo + RelayHi).
    /// Section 1 = bit 0 of RelayLo, Section 8 = bit 7 of RelayLo.
    /// Section 9 = bit 0 of RelayHi, Section 14 = bit 5 of RelayHi.
    /// </summary>
    /// <param name="valveMask">14-bit valve mask (bit 0 = section 1).</param>
    /// <returns>Tuple of (RelayLo, RelayHi) bytes.</returns>
    public static (byte RelayLo, byte RelayHi) PackSectionBytes(ushort valveMask)
    {
        valveMask &= 0x3FFF; // Mask to 14 bits
        byte relayLo = (byte)(valveMask & 0xFF);        // Sections 1-8
        byte relayHi = (byte)((valveMask >> 8) & 0x3F); // Sections 9-14
        return (relayLo, relayHi);
    }

    /// <summary>
    /// Unpacks AOG section bytes (RelayLo + RelayHi) into a 14-bit valve mask.
    /// </summary>
    public static ushort UnpackSectionBytes(byte relayLo, byte relayHi)
    {
        return (ushort)((relayLo & 0xFF) | ((relayHi & 0x3F) << 8));
    }

    /// <summary>
    /// Builds a complete PGN 229 Section Control packet with the given 14-bit mask.
    /// Remaining sections (15-64) are forced to 0.
    /// </summary>
    /// <param name="valveMask">14-bit valve mask.</param>
    /// <param name="leftSpeed">Left boom tip speed byte.</param>
    /// <param name="rightSpeed">Right boom tip speed byte.</param>
    /// <returns>Complete AOG UDP packet bytes.</returns>
    public static byte[] BuildSectionControlPacket(ushort valveMask, byte leftSpeed = 0, byte rightSpeed = 0)
    {
        var (relayLo, relayHi) = PackSectionBytes(valveMask);

        // Total: 5 header + 10 data + 1 CRC = 16 bytes
        var packet = new byte[HeaderLen + SectionControlDataLen + 1];

        packet[OffsetPreamble1] = Preamble1;
        packet[OffsetPreamble2] = Preamble2;
        packet[OffsetSource] = SourceAog;
        packet[OffsetPgn] = PgnSectionControl;
        packet[OffsetLength] = SectionControlDataLen;

        // Section data bytes
        packet[OffsetData + 0] = relayLo;      // Sections 1-8
        packet[OffsetData + 1] = relayHi;       // Sections 9-16
        packet[OffsetData + 2] = 0;             // Sections 17-24 (unused)
        packet[OffsetData + 3] = 0;             // Sections 25-32 (unused)
        packet[OffsetData + 4] = 0;             // Sections 33-40 (unused)
        packet[OffsetData + 5] = 0;             // Sections 41-48 (unused)
        packet[OffsetData + 6] = 0;             // Sections 49-56 (unused)
        packet[OffsetData + 7] = 0;             // Sections 57-64 (unused)
        packet[OffsetData + 8] = leftSpeed;
        packet[OffsetData + 9] = rightSpeed;

        // CRC = sum of bytes from Source to end of data, ANDed with 0xFF
        byte crc = 0;
        for (int i = OffsetSource; i < packet.Length - 1; i++)
        {
            crc += packet[i];
        }
        packet[^1] = crc;

        return packet;
    }

    /// <summary>
    /// Validates an incoming AOG packet (checks preamble, source, length).
    /// </summary>
    public static bool IsValidPacket(byte[] data)
    {
        if (data.Length < HeaderLen + 1) return false;
        if (data[OffsetPreamble1] != Preamble1) return false;
        if (data[OffsetPreamble2] != Preamble2) return false;

        int declaredLen = data[OffsetLength];
        if (data.Length < HeaderLen + declaredLen + 1) return false;

        // Verify CRC
        byte crc = 0;
        for (int i = OffsetSource; i < HeaderLen + declaredLen; i++)
        {
            crc += data[i];
        }

        return data[HeaderLen + declaredLen] == crc;
    }

    /// <summary>
    /// Extracts the PGN number from a valid AOG packet.
    /// </summary>
    public static byte GetPgn(byte[] data)
    {
        return data[OffsetPgn];
    }

    /// <summary>
    /// Extracts the original 14-bit section mask from an incoming PGN 229 packet.
    /// </summary>
    public static ushort ExtractSectionMask(byte[] data)
    {
        if (data.Length < OffsetData + 2) return 0;
        return UnpackSectionBytes(data[OffsetData], data[OffsetData + 1]);
    }

    /// <summary>
    /// Creates a modified copy of an existing PGN 229 packet with overridden section bytes.
    /// Preserves speed bytes and recalculates CRC.
    /// </summary>
    /// <param name="originalPacket">Original AOG section control packet.</param>
    /// <param name="overrideMask">New 14-bit valve mask to inject.</param>
    /// <returns>New packet with overridden sections and recalculated CRC.</returns>
    public static byte[] OverrideSectionPacket(byte[] originalPacket, ushort overrideMask)
    {
        var modified = (byte[])originalPacket.Clone();
        var (relayLo, relayHi) = PackSectionBytes(overrideMask);

        modified[OffsetData + 0] = relayLo;
        modified[OffsetData + 1] = relayHi;

        // Recalculate CRC
        int declaredLen = modified[OffsetLength];
        byte crc = 0;
        for (int i = OffsetSource; i < HeaderLen + declaredLen; i++)
        {
            crc += modified[i];
        }
        modified[HeaderLen + declaredLen] = crc;

        return modified;
    }
}

/// <summary>
/// Parsed GPS data from AgOpenGPS PGN 253.
/// </summary>
public record AogGpsData
{
    /// <summary>Latitude in decimal degrees (WGS84).</summary>
    public required double Latitude { get; init; }

    /// <summary>Longitude in decimal degrees (WGS84).</summary>
    public required double Longitude { get; init; }

    /// <summary>Heading in degrees (0 = North, 90 = East).</summary>
    public required double HeadingDegrees { get; init; }

    /// <summary>Speed in km/h.</summary>
    public required double SpeedKmh { get; init; }

    /// <summary>GPS Fix Quality (RTK Fix required for trial work).</summary>
    public PlotManager.Core.Models.GpsFixQuality FixQuality { get; init; } =
        PlotManager.Core.Models.GpsFixQuality.RtkFix;

    /// <summary>
    /// Course Over Ground in degrees (0=North, 90=East).
    /// Represents the actual trajectory of movement, which may differ from
    /// heading on slopes (crab-walk). Use for rear boom projection on uneven terrain.
    /// </summary>
    public double CourseOverGroundDegrees { get; init; }
}
