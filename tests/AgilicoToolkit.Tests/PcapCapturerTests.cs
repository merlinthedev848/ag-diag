using System;
using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgilicoToolkit.Tests;

[TestClass]
public class PcapCapturerTests
{
    [TestMethod]
    public void GlobalHeader_UsesRawIpLinkType()
    {
        using var capturer = new agilicomsptoolkit.PcapCapturer();
        capturer.Start();
        byte[] bytes = capturer.GetPcapBytes();

        Assert.AreEqual(24, bytes.Length);
        Assert.AreEqual(0xa1b2c3d4u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.AreEqual(101u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4)));
    }

    [TestMethod]
    public void SyntheticPacket_DoesNotContainFabricatedEthernetHeader()
    {
        using var capturer = new agilicomsptoolkit.PcapCapturer();
        capturer.Start();
        capturer.RecordPacket(new byte[] { 1, 2, 3 }, "10.0.0.1", 5000, "10.0.0.2", 5001, true);
        byte[] bytes = capturer.GetPcapBytes();

        Assert.IsTrue(capturer.ContainsSyntheticPackets);
        Assert.AreEqual(1, capturer.PacketCount);
        // First packet starts at byte 40: PCAP header (16) + IPv4 header (20) + UDP header (8).
        Assert.AreEqual(0x45, bytes[40]);
        Assert.AreEqual(10, bytes[52]);
        Assert.AreEqual(10, bytes[56]);
    }

    [TestMethod]
    public void IpFilter_RejectsNonMatchingPacket()
    {
        using var capturer = new agilicomsptoolkit.PcapCapturer();
        capturer.Start(ipFilter: "10.0.0.99");
        capturer.RecordPacket(new byte[] { 1 }, "10.0.0.1", 1, "10.0.0.2", 2, true);
        Assert.AreEqual(0, capturer.PacketCount);
    }
}
