using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgilicoConnectChecker
{
    public class SrvRecord
    {
        public ushort Priority { get; set; }
        public ushort Weight { get; set; }
        public ushort Port { get; set; }
        public string Target { get; set; } = string.Empty;
    }

    public class PortProbeResult
    {
        public int Port { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Status { get; set; } = "Closed"; // Open, Blocked, Unresponsive
        public double? RttMs { get; set; }

        public string PortDisplay => $"{Protocol} {Port}";
        public string RttDisplay => RttMs.HasValue ? $"{RttMs.Value:F1} ms" : "-";
    }

    public static class VoipTools
    {
        #region MOS Estimator Helper

        public static double CalculateMosScore(double latencyMs, double jitterMs, double lossPercentage)
        {
            // Effective Latency (d)
            double d = latencyMs + (2.0 * jitterMs) + 10.0;

            // Delay Impairment (Id)
            double id;
            if (d < 160.0)
            {
                id = d / 40.0;
            }
            else
            {
                id = (d / 40.0) + ((d - 160.0) / 10.0);
            }

            // Packet Loss Impairment (Ie)
            // Impairment goes from 0 up to 95. Loss above 30% causes R-value to drop to 0.
            double ie = 95.0 * (lossPercentage / (lossPercentage + 10.0));

            // R-value calculation (starts at 93.2 for standard G.711 codec)
            double r = 93.2 - id - ie;
            if (r < 0.0) r = 0.0;

            // Convert R-value to MOS (1.0 to 4.4 range)
            double mos;
            if (r <= 0.0)
            {
                mos = 1.0;
            }
            else if (r >= 100.0)
            {
                mos = 4.4;
            }
            else
            {
                mos = 1.0 + (0.035 * r) + (r * (r - 60.0) * (100.0 - r) * 0.000007);
                if (mos < 1.0) mos = 1.0;
                if (mos > 4.4) mos = 4.4;
            }

            return Math.Round(mos, 2);
        }

        #endregion

        #region DNS SRV Resolver

        public static async Task<List<SrvRecord>> ResolveSrvAsync(string service, string domain, string dnsServer = "8.8.8.8")
        {
            var records = new List<SrvRecord>();
            try
            {
                using var client = new UdpClient(0);
                client.Client.SendTimeout = 3000;
                client.Client.ReceiveTimeout = 3000;

                ushort txId = (ushort)Random.Shared.Next(1, 65535);
                byte[] query = BuildSrvQuery(service, domain, txId);
                
                var ipAddress = IPAddress.Parse(dnsServer);
                var endpoint = new IPEndPoint(ipAddress, 53);

                await client.SendAsync(query, query.Length, endpoint);

                var receiveTask = client.ReceiveAsync();
                var delayTask = Task.Delay(3000);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    if (result.Buffer.Length > 12)
                    {
                        ushort respTxId = (ushort)((result.Buffer[0] << 8) | result.Buffer[1]);
                        if (respTxId == txId)
                        {
                            records = ParseSrvResponse(result.Buffer);
                        }
                    }
                }
            }
            catch { }

            return records;
        }

        private static byte[] BuildSrvQuery(string service, string domain, ushort transactionId)
        {
            var packet = new List<byte>();

            // DNS Header (12 bytes)
            packet.Add((byte)(transactionId >> 8));
            packet.Add((byte)(transactionId & 0xFF));
            packet.Add(0x01); packet.Add(0x00); // Standard Query, Recursion Desired
            packet.Add(0x00); packet.Add(0x01); // Questions: 1
            packet.Add(0x00); packet.Add(0x00); // Answer RRs: 0
            packet.Add(0x00); packet.Add(0x00); // Authority RRs: 0
            packet.Add(0x00); packet.Add(0x00); // Additional RRs: 0

            // Question domain name labels
            string fullName = $"{service.Trim().TrimEnd('.')}.{domain.Trim().TrimStart('.').TrimEnd('.')}";
            string[] labels = fullName.Split('.');
            foreach (var label in labels)
            {
                byte[] labelBytes = Encoding.UTF8.GetBytes(label);
                packet.Add((byte)labelBytes.Length);
                packet.AddRange(labelBytes);
            }
            packet.Add(0x00); // End of name

            // Type: SRV (33 = 0x0021)
            packet.Add(0x00); packet.Add(0x21);
            // Class: IN (1 = 0x0001)
            packet.Add(0x00); packet.Add(0x01);

            return packet.ToArray();
        }

        private static List<SrvRecord> ParseSrvResponse(byte[] response)
        {
            var records = new List<SrvRecord>();
            if (response.Length < 12) return records;

            ushort questions = (ushort)((response[4] << 8) | response[5]);
            ushort answers = (ushort)((response[6] << 8) | response[7]);

            int offset = 12;

            // Skip questions block
            for (int i = 0; i < questions; i++)
            {
                SkipName(response, ref offset);
                offset += 4; // Skip QTYPE and QCLASS
            }

            // Parse answer resource records
            for (int i = 0; i < answers; i++)
            {
                SkipName(response, ref offset); // Owner Name
                if (offset + 10 > response.Length) break;

                ushort type = (ushort)((response[offset] << 8) | response[offset + 1]);
                offset += 8; // Skip Type (2B), Class (2B), TTL (4B)

                ushort rdLen = (ushort)((response[offset] << 8) | response[offset + 1]);
                offset += 2;

                int rdOffset = offset;
                if (type == 0x0021 && rdOffset + rdLen <= response.Length) // SRV Type
                {
                    ushort priority = (ushort)((response[rdOffset] << 8) | response[rdOffset + 1]);
                    ushort weight = (ushort)((response[rdOffset + 2] << 8) | response[rdOffset + 3]);
                    ushort port = (ushort)((response[rdOffset + 4] << 8) | response[rdOffset + 5]);

                    int targetOffset = rdOffset + 6;
                    string target = ParseName(response, ref targetOffset);

                    records.Add(new SrvRecord
                    {
                        Priority = priority,
                        Weight = weight,
                        Port = port,
                        Target = target
                    });
                }

                offset += rdLen;
            }

            return records;
        }

        private static void SkipName(byte[] response, ref int offset)
        {
            while (offset < response.Length)
            {
                byte len = response[offset];
                if (len == 0)
                {
                    offset++;
                    break;
                }
                if ((len & 0xC0) == 0xC0)
                {
                    offset += 2;
                    break;
                }
                offset += 1 + len;
            }
        }

        private static string ParseName(byte[] response, ref int offset)
        {
            var parts = new List<string>();
            int current = offset;
            bool jumped = false;
            int savedOffset = -1;

            while (current < response.Length)
            {
                byte len = response[current];
                if (len == 0)
                {
                    current++;
                    break;
                }

                if ((len & 0xC0) == 0xC0)
                {
                    if (current + 1 >= response.Length) break;
                    int pointer = ((len & 0x3F) << 8) | response[current + 1];
                    if (!jumped)
                    {
                        savedOffset = current + 2;
                        jumped = true;
                    }
                    current = pointer;
                }
                else
                {
                    current++;
                    if (current + len > response.Length) break;
                    parts.Add(Encoding.UTF8.GetString(response, current, len));
                    current += len;
                }
            }

            offset = jumped ? savedOffset : current;
            return string.Join(".", parts);
        }

        #endregion

        #region Outbound Firewall Port Prober

        public static async Task<PortProbeResult> ProbeTcpPortAsync(string target, int port, string serviceName, CancellationToken token)
        {
            var result = new PortProbeResult
            {
                Port = port,
                Protocol = "TCP",
                ServiceName = serviceName,
                Target = target,
                Status = "Closed",
                RttMs = null
            };

            var sw = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(target, port);
                var delayTask = Task.Delay(1500, token); // 1.5 second timeout

                var completed = await Task.WhenAny(connectTask, delayTask);
                if (completed == connectTask)
                {
                    await connectTask; // throws if failed
                    sw.Stop();
                    result.Status = "Open";
                    result.RttMs = sw.Elapsed.TotalMilliseconds;
                }
                else
                {
                    result.Status = "Blocked"; // Timeout
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    result.Status = "Closed"; // Connection refused, but outbound path is open
                }
                else
                {
                    result.Status = "Blocked";
                }
            }
            catch
            {
                result.Status = "Blocked";
            }

            return result;
        }

        public static async Task<PortProbeResult> ProbeUdpPortAsync(string target, int port, string serviceName, CancellationToken token)
        {
            var result = new PortProbeResult
            {
                Port = port,
                Protocol = "UDP",
                ServiceName = serviceName,
                Target = target,
                Status = "Unresponsive", // Default for UDP
                RttMs = null
            };

            var sw = Stopwatch.StartNew();
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(target, token);
                if (addresses.Length == 0)
                {
                    result.Status = "Blocked";
                    return result;
                }

                var targetIp = addresses[0];
                var endpoint = new IPEndPoint(targetIp, port);

                using var client = new UdpClient(0);
                client.Client.SendTimeout = 1500;
                client.Client.ReceiveTimeout = 1500;

                byte[] payload;
                if (port == 3478) // STUN
                {
                    byte[] txId = new byte[12];
                    Random.Shared.NextBytes(txId);
                    payload = BuildStunRequest(txId);
                }
                else if (port == 5060 || port == 5061) // SIP
                {
                    payload = BuildSipOptionsRequest(target, port);
                }
                else
                {
                    // Generic payload
                    payload = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A };
                }

                await client.SendAsync(payload, payload.Length, endpoint);

                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(1500, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var response = await receiveTask;
                    sw.Stop();

                    if (port == 3478)
                    {
                        if (response.Buffer.Length >= 20 && (response.Buffer[0] << 8 | response.Buffer[1]) == 0x0101)
                        {
                            result.Status = "Open";
                        }
                    }
                    else if (port == 5060 || port == 5061)
                    {
                        string respStr = Encoding.UTF8.GetString(response.Buffer);
                        if (respStr.Contains("SIP/2.0"))
                        {
                            result.Status = "Open";
                        }
                    }
                    else
                    {
                        result.Status = "Open";
                    }

                    result.RttMs = sw.Elapsed.TotalMilliseconds;
                }
                else
                {
                    // UDP timeout is common if remote is not listening or ignores payload.
                    // If no ICMP error was returned, we mark as Unresponsive (likely permitted outbound).
                    result.Status = "Unresponsive";
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    // ICMP Port Unreachable returned (outbound route is open!)
                    result.Status = "Open"; 
                }
                else
                {
                    result.Status = "Blocked";
                }
            }
            catch
            {
                result.Status = "Blocked";
            }

            return result;
        }

        private static byte[] BuildStunRequest(byte[] transactionId)
        {
            byte[] packet = new byte[20];
            packet[0] = 0x00;
            packet[1] = 0x01; // Binding Request
            packet[2] = 0x00;
            packet[3] = 0x00; // Length
            // Magic Cookie
            packet[4] = 0x21; packet[5] = 0x12; packet[6] = 0xA4; packet[7] = 0x42;
            Buffer.BlockCopy(transactionId, 0, packet, 8, 12);
            return packet;
        }

        private static byte[] BuildSipOptionsRequest(string host, int port)
        {
            string branch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 10);
            string tag = Guid.NewGuid().ToString("N").Substring(0, 10);
            string callId = Guid.NewGuid().ToString("N").Substring(0, 16) + "@agilico.co.uk";
            
            // Dummy local address info
            string localIp = "127.0.0.1";
            int localPort = 5060;

            string sipRegister =
                $"OPTIONS sip:{host} SIP/2.0\r\n" +
                $"Via: SIP/2.0/UDP {localIp}:{localPort};rport;branch={branch}\r\n" +
                $"Max-Forwards: 70\r\n" +
                $"To: <sip:checker@{host}>\r\n" +
                $"From: <sip:checker@{localIp}:{localPort}>;tag={tag}\r\n" +
                $"Call-ID: {callId}\r\n" +
                $"CSeq: 1 OPTIONS\r\n" +
                $"Contact: <sip:checker@{localIp}:{localPort}>\r\n" +
                $"User-Agent: Agilico Network Diagnostic Tool\r\n" +
                $"Content-Length: 0\r\n\r\n";

            return Encoding.UTF8.GetBytes(sipRegister);
        }

        #endregion
    }
}
