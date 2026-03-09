using PlotManager.Core.Protocol;
using Xunit;

namespace PlotManager.Tests;

/// <summary>
/// Tests for AogProtocol PGN 229 packing, unpacking, CRC, and section override.
/// </summary>
public class AogProtocolTests
{
    [Fact]
    public void PackSectionBytes_Sections1to8_InRelayLo()
    {
        // Sections 1, 3, 5 → bits 0, 2, 4 → RelayLo = 0b00010101 = 0x15
        ushort mask = 0b00010101;

        var (relayLo, relayHi) = AogProtocol.PackSectionBytes(mask);

        Assert.Equal(0x15, relayLo);
        Assert.Equal(0x00, relayHi);
    }

    [Fact]
    public void PackSectionBytes_Sections9to14_InRelayHi()
    {
        // Section 9 = bit 8, Section 14 = bit 13 → 0x2100
        ushort mask = (1 << 8) | (1 << 13); // Section 9 + Section 14

        var (relayLo, relayHi) = AogProtocol.PackSectionBytes(mask);

        Assert.Equal(0x00, relayLo);
        Assert.Equal(0x21, relayHi); // 0b00100001
    }

    [Fact]
    public void PackUnpack_Roundtrip()
    {
        ushort originalMask = 0x2A55; // Arbitrary 14-bit value
        originalMask &= 0x3FFF;

        var (lo, hi) = AogProtocol.PackSectionBytes(originalMask);
        ushort recovered = AogProtocol.UnpackSectionBytes(lo, hi);

        Assert.Equal(originalMask, recovered);
    }

    [Fact]
    public void PackSectionBytes_MasksTo14Bits()
    {
        // Set bits 14 and 15 (should be masked off)
        ushort mask = 0xFFFF;

        var (relayLo, relayHi) = AogProtocol.PackSectionBytes(mask);

        Assert.Equal(0xFF, relayLo);
        Assert.Equal(0x3F, relayHi); // Only bits 0-5 (sections 9-14)
    }

    [Fact]
    public void BuildSectionControlPacket_CorrectStructure()
    {
        ushort mask = 0x0001; // Section 1 only

        byte[] packet = AogProtocol.BuildSectionControlPacket(mask);

        // Header
        Assert.Equal(AogProtocol.Preamble1, packet[0]);
        Assert.Equal(AogProtocol.Preamble2, packet[1]);
        Assert.Equal(AogProtocol.SourceAog, packet[2]);
        Assert.Equal(AogProtocol.PgnSectionControl, packet[3]);
        Assert.Equal(AogProtocol.SectionControlDataLen, packet[4]);

        // Section bytes
        Assert.Equal(0x01, packet[5]); // RelayLo: section 1 on
        Assert.Equal(0x00, packet[6]); // RelayHi: none

        // Total length: 5 header + 10 data + 1 CRC = 16
        Assert.Equal(16, packet.Length);
    }

    [Fact]
    public void BuildSectionControlPacket_ValidCrc()
    {
        byte[] packet = AogProtocol.BuildSectionControlPacket(0x0001);

        Assert.True(AogProtocol.IsValidPacket(packet));
    }

    [Fact]
    public void IsValidPacket_Detects_CorruptCrc()
    {
        byte[] packet = AogProtocol.BuildSectionControlPacket(0x0001);
        packet[^1] ^= 0xFF; // Corrupt CRC

        Assert.False(AogProtocol.IsValidPacket(packet));
    }

    [Fact]
    public void IsValidPacket_Detects_WrongPreamble()
    {
        byte[] packet = AogProtocol.BuildSectionControlPacket(0x0001);
        packet[0] = 0xAA; // Wrong preamble

        Assert.False(AogProtocol.IsValidPacket(packet));
    }

    [Fact]
    public void ExtractSectionMask_FromPacket()
    {
        ushort expectedMask = 0x0A0F;
        byte[] packet = AogProtocol.BuildSectionControlPacket(expectedMask);

        ushort extracted = AogProtocol.ExtractSectionMask(packet);

        Assert.Equal(expectedMask, extracted);
    }

    [Fact]
    public void OverrideSectionPacket_PreservesCrc()
    {
        byte[] original = AogProtocol.BuildSectionControlPacket(0x0001);
        ushort newMask = 0x3FFF; // All 14 on

        byte[] overridden = AogProtocol.OverrideSectionPacket(original, newMask);

        Assert.True(AogProtocol.IsValidPacket(overridden), "Overridden packet must have valid CRC");
        Assert.Equal(newMask, AogProtocol.ExtractSectionMask(overridden));
    }

    [Fact]
    public void OverrideSectionPacket_DoesNotMutateOriginal()
    {
        byte[] original = AogProtocol.BuildSectionControlPacket(0x0001);
        byte[] originalCopy = (byte[])original.Clone();

        AogProtocol.OverrideSectionPacket(original, 0x3FFF);

        Assert.Equal(originalCopy, original); // Original should be unchanged
    }

    [Fact]
    public void AllSectionsOff_PacksToZero()
    {
        var (lo, hi) = AogProtocol.PackSectionBytes(0);

        Assert.Equal(0, lo);
        Assert.Equal(0, hi);
    }
}
