using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgilicoDiagMac.Core;
using AgilicoDiagMac.Platform;

namespace AgilicoDiagMac.Tests
{
    internal class Program
    {
        public static async Task<int> Main()
        {
            Console.WriteLine("Running Agilico macOS Diagnostic Engine Tests...");
            int passed = 0;
            int failed = 0;

            // Test 1: CRC32
            try
            {
                byte[] testBytes = Encoding.UTF8.GetBytes("123456789");
                string hex = Crc32.ComputeHex(testBytes);
                if (hex == "CBF43926")
                {
                    Console.WriteLine("[PASS] Test 1: CRC32 checksum matches standard vector (CBF43926).");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"[FAIL] Test 1: CRC32 expected CBF43926, got {hex}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Test 1: {ex.Message}");
                failed++;
            }

            // Test 2: MOS Score Calculation
            try
            {
                double mosGood = VoipTools.CalculateMosScore(10, 2, 0);
                double mosBad = VoipTools.CalculateMosScore(250, 40, 20);
                if (mosGood >= 4.0 && mosBad < 3.0)
                {
                    Console.WriteLine($"[PASS] Test 2: MOS Score calculation (Good={mosGood}, Bad={mosBad}).");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"[FAIL] Test 2: Unexpected MOS scores: Good={mosGood}, Bad={mosBad}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Test 2: {ex.Message}");
                failed++;
            }

            // Test 3: PCAP Capturer
            try
            {
                using var pcap = new PcapCapturer();
                pcap.Start();
                byte[] sample = Encoding.UTF8.GetBytes("AGILICO_VOIP_PACKET");
                pcap.RecordPacket(sample, "192.168.1.50", 5060, "109.73.119.38", 5060, true);
                pcap.Stop();

                byte[] pcapBytes = pcap.GetPcapBytes();
                if (pcapBytes.Length >= 24 && BitConverter.ToUInt32(pcapBytes, 0) == 0xa1b2c3d4 && pcap.PacketCount == 1)
                {
                    Console.WriteLine($"[PASS] Test 3: PCAP Capturer generated valid Wireshark PCAP ({pcapBytes.Length} bytes).");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"[FAIL] Test 3: Invalid PCAP generation (Length: {pcapBytes.Length}, PacketCount: {pcap.PacketCount})");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Test 3: {ex.Message}");
                failed++;
            }

            // Test 4: OUI Vendor Lookup
            try
            {
                using var scanner = new LanScanner();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                string cisco = await scanner.GetManufacturerAsync("00:11:58:12:34:56", cts.Token);
                string apple = await scanner.GetManufacturerAsync("F8:FF:C2:AB:CD:EF", cts.Token);
                if (cisco == "Cisco" && apple == "Apple")
                {
                    Console.WriteLine("[PASS] Test 4: LanScanner offline OUI resolution (Cisco & Apple verified).");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"[FAIL] Test 4: OUI mismatch: Cisco={cisco}, Apple={apple}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Test 4: {ex.Message}");
                failed++;
            }

            // Test 5: macOS Wi-Fi & Audio Device Diagnostics
            try
            {
                var wifi = await MacSystemTools.GetWifiMetricsAsync();
                var audio = await MacSystemTools.GetAudioDevicesAsync();
                var sockets = await MacSocketMonitor.GetActiveSocketsAsync();

                if (!string.IsNullOrEmpty(wifi.SignalQuality) && audio.Count > 0)
                {
                    Console.WriteLine($"[PASS] Test 5: macOS Voice & Network diagnostics (Wi-Fi={wifi.SignalQuality}, AudioDevices={audio.Count}, ActiveSockets={sockets.Count}).");
                    passed++;
                }
                else
                {
                    Console.WriteLine("[FAIL] Test 5: Wi-Fi or Audio diagnostics failed.");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Test 5: {ex.Message}");
                failed++;
            }

            Console.WriteLine("==========================================================");
            Console.WriteLine($"Test Suite Summary: {passed} Passed, {failed} Failed.");
            Console.WriteLine("==========================================================");

            return failed == 0 ? 0 : 1;
        }
    }
}
