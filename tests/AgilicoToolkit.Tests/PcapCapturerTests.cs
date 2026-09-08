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

[TestClass]
public class Crc32Tests
{
    [TestMethod]
    public void Compute_EmptyArray_ReturnsZero()
    {
        byte[] empty = Array.Empty<byte>();
        uint result = agilicomsptoolkit.Crc32.Compute(empty);
        Assert.AreEqual(0u, result);
    }

    [TestMethod]
    public void Compute_StandardAsciiString_MatchesStandardChecksum()
    {
        // CRC32 of "123456789" is standard 0xCBF43926
        byte[] input = System.Text.Encoding.ASCII.GetBytes("123456789");
        uint result = agilicomsptoolkit.Crc32.Compute(input);
        Assert.AreEqual(0xCBF43926u, result);
    }

    [TestMethod]
    public void ComputeHex_ReturnsCorrectFormattedHexString()
    {
        byte[] input = System.Text.Encoding.ASCII.GetBytes("123456789");
        string hex = agilicomsptoolkit.Crc32.ComputeHex(input);
        Assert.AreEqual("CBF43926", hex);
    }
}

[TestClass]
public class DiagnosticsModelTests
{
    [TestMethod]
    public void LanDevice_DefaultProperties_InitializedCorrectly()
    {
        var device = new agilicomsptoolkit.LanDevice
        {
            IpAddress = "192.168.1.100",
            MacAddress = "00:15:65:11:22:33",
            Manufacturer = "Yealink",
            Hostname = "DeskPhone-101"
        };

        Assert.AreEqual("192.168.1.100", device.IpAddress);
        Assert.AreEqual("00:15:65:11:22:33", device.MacAddress);
        Assert.AreEqual("Yealink", device.Manufacturer);
        Assert.AreEqual("Online", device.Status);
    }

    [TestMethod]
    public void HardwareItem_HealthyState_ReflectedProperly()
    {
        var item = new agilicomsptoolkit.HardwareItem
        {
            ComponentType = "Processor (CPU)",
            Name = "Intel Core i7",
            Status = "Healthy",
            Details = "Cores: 8 | Max Speed: 3200 MHz",
            IsHealthy = true
        };

        Assert.IsTrue(item.IsHealthy);
        Assert.AreEqual("Processor (CPU)", item.ComponentType);
        Assert.AreEqual("Healthy", item.Status);
    }
}

[TestClass]
public class ServiceLayerTests
{
    [TestMethod]
    public async Task PowerShellRunner_BasicEchoCommand_ExecutesSuccessfully()
    {
        var runner = new agilicomsptoolkit.Services.PowerShellRunner();
        var result = await runner.ExecuteCommandAsync("Write-Output 'AGILICO_TEST_OK'", TimeSpan.FromSeconds(15));
        
        Assert.AreEqual(0, result.exitCode);
        Assert.IsTrue(result.stdout.Contains("AGILICO_TEST_OK"));
    }

    [TestMethod]
    public async Task SoundConverterService_InvalidPath_ReturnsHelpfulError()
    {
        var service = new agilicomsptoolkit.Services.SoundConverterService();
        var result = await service.ConvertToTelephonyWavAsync("C:\\non_existent_audio_file.mp3", null);
        
        Assert.IsFalse(result.success);
        Assert.IsTrue(result.message.Contains("does not exist") || result.message.Contains("No input file"));
    }
}
