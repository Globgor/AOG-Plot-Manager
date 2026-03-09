using PlotManager.Core.Protocol;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for the PlotProtocol packet builder and parser.
/// </summary>
public class PlotProtocolTests
{
    [Fact]
    public void BuildSetValves_CreatesValidPacket()
    {
        // Arrange: open valves 0, 1, and 13 → mask = 0b0010000000000011 = 0x2003
        ushort mask = 0x2003;

        // Act
        byte[] packet = PlotProtocol.BuildSetValves(mask);

        // Assert
        Assert.Equal(PlotProtocol.Sync1, packet[0]);
        Assert.Equal(PlotProtocol.Sync2, packet[1]);
        Assert.Equal(PlotProtocol.Commands.SetValves, packet[2]);
        Assert.Equal(0x20, packet[3]); // MSB
        Assert.Equal(0x03, packet[4]); // LSB

        // CRC = CMD ^ MSB ^ LSB
        byte expectedCrc = (byte)(PlotProtocol.Commands.SetValves ^ 0x20 ^ 0x03);
        Assert.Equal(expectedCrc, packet[5]);
    }

    [Fact]
    public void BuildSetValves_MasksTo14Bits()
    {
        // Arrange: set all 16 bits → should mask to 14 bits (0x3FFF)
        ushort mask = 0xFFFF;

        // Act
        byte[] packet = PlotProtocol.BuildSetValves(mask);

        // Assert: MSB should be 0x3F, not 0xFF
        Assert.Equal(0x3F, packet[3]);
        Assert.Equal(0xFF, packet[4]);
    }

    [Fact]
    public void BuildHeartbeat_CreatesMinimalPacket()
    {
        // Act
        byte[] packet = PlotProtocol.BuildHeartbeat();

        // Assert: SYNC1 + SYNC2 + CMD + CRC (4 bytes)
        Assert.Equal(4, packet.Length);
        Assert.Equal(PlotProtocol.Sync1, packet[0]);
        Assert.Equal(PlotProtocol.Sync2, packet[1]);
        Assert.Equal(PlotProtocol.Commands.Heartbeat, packet[2]);
        Assert.Equal(PlotProtocol.Commands.Heartbeat, packet[3]); // CRC = CMD (no data)
    }

    [Fact]
    public void BuildPrime_EncodesCorrectly()
    {
        // Arrange: prime section 5 (mask = 0x0020) for 3000ms
        ushort sectionMask = 0x0020;
        ushort durationMs = 3000;

        // Act
        byte[] packet = PlotProtocol.BuildPrime(sectionMask, durationMs);

        // Assert
        Assert.Equal(8, packet.Length); // SYNC1+SYNC2+CMD+4data+CRC
        Assert.Equal(PlotProtocol.Commands.Prime, packet[2]);
        Assert.Equal(0x00, packet[3]); // Section MSB
        Assert.Equal(0x20, packet[4]); // Section LSB
        Assert.Equal((byte)(3000 >> 8), packet[5]); // Duration MSB
        Assert.Equal((byte)(3000 & 0xFF), packet[6]); // Duration LSB
    }

    [Fact]
    public void ParseResponse_ValidStatusPacket()
    {
        // Arrange: build a fake status response
        byte cmd = PlotProtocol.Responses.Status;
        byte maskMsb = 0x00;
        byte maskLsb = 0x07; // Sections 0, 1, 2
        byte errFlags = 0x00;
        byte crc = (byte)(cmd ^ maskMsb ^ maskLsb ^ errFlags);

        byte[] data = { PlotProtocol.Sync1, PlotProtocol.Sync2, cmd, maskMsb, maskLsb, errFlags, crc };

        // Act
        ParsedResponse? response = PlotProtocol.ParseResponse(data);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Value.IsStatus);
        Assert.Equal((ushort)0x0007, response.Value.GetValveMask());
        Assert.Equal((byte)0x00, response.Value.GetErrorFlags());
    }

    [Fact]
    public void ParseResponse_InvalidCrc_ReturnsNull()
    {
        // Arrange: corrupt CRC
        byte[] data = { PlotProtocol.Sync1, PlotProtocol.Sync2, 0x80, 0x00, 0x07, 0x00, 0xFF };

        // Act
        ParsedResponse? response = PlotProtocol.ParseResponse(data);

        // Assert
        Assert.Null(response);
    }

    [Fact]
    public void ParseResponse_TooShort_ReturnsNull()
    {
        // Act
        ParsedResponse? response = PlotProtocol.ParseResponse(new byte[] { 0xAA, 0x55 });

        // Assert
        Assert.Null(response);
    }

    [Fact]
    public void BuildEmergencyStop_CreatesValidPacket()
    {
        // Act
        byte[] packet = PlotProtocol.BuildEmergencyStop();

        // Assert
        Assert.Equal(4, packet.Length);
        Assert.Equal(PlotProtocol.Commands.EmergencyStop, packet[2]);
    }
}
