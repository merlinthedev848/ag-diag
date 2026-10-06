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
    public void Compute_ReadOnlySpan_MatchesByteArray()
    {
        byte[] input = System.Text.Encoding.ASCII.GetBytes("123456789");
        ReadOnlySpan<byte> span = input.AsSpan();
        uint resultSpan = agilicomsptoolkit.Crc32.Compute(span);
        uint resultArr = agilicomsptoolkit.Crc32.Compute(input);
        Assert.AreEqual(resultArr, resultSpan);
        Assert.AreEqual(0xCBF43926u, resultSpan);
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

    [TestMethod]
    public void ComputeHex_ReadOnlySpan_ReturnsCorrectFormattedHexString()
    {
        byte[] input = System.Text.Encoding.ASCII.GetBytes("123456789");
        string hex = agilicomsptoolkit.Crc32.ComputeHex(input.AsSpan());
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

[TestClass]
public class VoipToolsTests
{
    [TestMethod]
    public void CalculateMosScore_IdealConditions_ReturnsHighMosScore()
    {
        // 10ms latency, 2ms jitter, 0% loss
        double mos = agilicomsptoolkit.VoipTools.CalculateMosScore(10, 2, 0);
        Assert.IsTrue(mos >= 4.0 && mos <= 4.4, $"Expected excellent MOS >= 4.0, got {mos}");
    }

    [TestMethod]
    public void CalculateMosScore_HighPacketLoss_ReturnsDegradedMosScore()
    {
        // 200ms latency, 50ms jitter, 25% loss
        double mos = agilicomsptoolkit.VoipTools.CalculateMosScore(200, 50, 25);
        Assert.IsTrue(mos < 3.0, $"Expected degraded MOS < 3.0, got {mos}");
    }

    [TestMethod]
    public void CalculateMosScore_ExtremeImpairment_ClampsToMinimumScore()
    {
        // 1000ms latency, 500ms jitter, 90% loss
        double mos = agilicomsptoolkit.VoipTools.CalculateMosScore(1000, 500, 90);
        Assert.AreEqual(1.0, mos);
    }

    [TestMethod]
    public void PortProbeResult_DisplayProperties_FormattedCorrectly()
    {
        var result = new agilicomsptoolkit.PortProbeResult
        {
            Port = 5060,
            Protocol = "UDP",
            ServiceName = "SIP Signalling",
            Target = "sip.agilico.co.uk",
            Status = "Open",
            RttMs = 24.56
        };

        Assert.AreEqual("UDP 5060", result.PortDisplay);
        Assert.AreEqual("24.6 ms", result.RttDisplay);
    }

    [TestMethod]
    public void BuildStunRequest_ProducesValidStunBindingHeader()
    {
        byte[] txId = new byte[12] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        byte[] stunPacket = agilicomsptoolkit.VoipTools.BuildStunRequest(txId);

        Assert.AreEqual(20, stunPacket.Length);
        Assert.AreEqual(0x00, stunPacket[0]);
        Assert.AreEqual(0x01, stunPacket[1]); // STUN Binding Request
        Assert.AreEqual(0x21, stunPacket[4]); // Magic Cookie byte 1
        Assert.AreEqual(0x12, stunPacket[5]); // Magic Cookie byte 2
        Assert.AreEqual(0xA4, stunPacket[6]); // Magic Cookie byte 3
        Assert.AreEqual(0x42, stunPacket[7]); // Magic Cookie byte 4
    }

    [TestMethod]
    public void BuildSipOptionsRequest_ContainsValidSipHeaders()
    {
        byte[] sipBytes = agilicomsptoolkit.VoipTools.BuildSipOptionsRequest("hp2k.co.uk", 5060);
        string sipString = System.Text.Encoding.UTF8.GetString(sipBytes);

        Assert.IsTrue(sipString.StartsWith("OPTIONS sip:hp2k.co.uk SIP/2.0\r\n"));
        Assert.IsTrue(sipString.Contains("User-Agent: Agilico MSP Toolkit\r\n"));
        Assert.IsTrue(sipString.Contains("CSeq: 1 OPTIONS\r\n"));
    }

    [TestMethod]
    public void BuildSrvQuery_ConstructsDnsHeaderAndLabels()
    {
        ushort txId = 0x1234;
        byte[] query = agilicomsptoolkit.VoipTools.BuildSrvQuery("_sip._udp", "hp2k.co.uk", txId);

        Assert.IsTrue(query.Length > 12);
        Assert.AreEqual(0x12, query[0]);
        Assert.AreEqual(0x34, query[1]);
        Assert.AreEqual(0x01, query[2]); // Standard Query
    }
}

[TestClass]
public class PingTrackerTests
{
    [TestMethod]
    public void PingTracker_InitialState_NotRunning()
    {
        var tracker = new agilicomsptoolkit.PingTracker();
        Assert.IsFalse(tracker.IsRunning);
        Assert.AreEqual(string.Empty, tracker.CurrentTarget);
    }

    [TestMethod]
    public void PingTracker_Stop_CanBeCalledSafelyWhenNotRunning()
    {
        var tracker = new agilicomsptoolkit.PingTracker();
        tracker.Stop(); // Should not throw
        Assert.IsFalse(tracker.IsRunning);
    }
}

[TestClass]
public class NetworkEngineTests
{
    [TestMethod]
    public void NetworkEngine_DefaultSettings_MatchNetworkGuidance()
    {
        var engine = new agilicomsptoolkit.NetworkEngine();
        
        Assert.AreEqual("customerportal.hp2k.co.uk", engine.DomainToCheck);
        Assert.AreEqual("stun-gb-a.hp2k.co.uk", engine.StunServer);
        Assert.AreEqual(3478, engine.StunPort);
        Assert.AreEqual(10, engine.SelectedTests.Length);
        Assert.IsTrue(engine.SelectedTests.All(t => t));
    }
}
