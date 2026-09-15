using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AgilicoDiagMac.Core
{
    public class LanDevice
    {
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Status { get; set; } = "Online";
    }

    public class LanScanner : IDisposable
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true, SetLastError = true)]
        private static extern int SendARP(int DestIP, int SrcIP, byte[] pMacAddr, ref int PhyAddrLen);

        private static readonly HttpClient _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(5),
            DefaultRequestVersion = System.Net.HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        private static readonly ConcurrentDictionary<string, string> _ouiCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim _macApiSemaphore = new SemaphoreSlim(1, 1);

        // Curated list of common OUI prefixes for instant resolution without internet
        private static readonly Dictionary<string, string> _commonOuis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "00:15:65", "Yealink" }, { "00:15:B9", "Yealink" }, { "80:5E:C0", "Yealink" }, { "E4:34:93", "Yealink" }, { "00:04:13", "Snom" },
            { "00:04:F2", "Polycom" }, { "00:E0:75", "Polycom" }, { "64:16:7F", "Polycom" }, { "00:0B:82", "Grandstream" }, { "C0:74:AD", "Grandstream" },
            { "00:11:58", "Cisco" }, { "00:1B:D4", "Cisco" }, { "00:01:96", "Cisco" }, { "00:14:1C", "Cisco" }, { "A4:93:4C", "Cisco" },
            { "00:27:0D", "Cisco" }, { "3C:08:F6", "Cisco" }, { "50:06:AB", "Cisco" }, { "70:69:5A", "Cisco" }, { "84:B8:B8", "Cisco" },
            { "B4:FB:E4", "Ubiquiti" }, { "F0:9F:C2", "Ubiquiti" }, { "18:E8:29", "Ubiquiti" }, { "74:83:C2", "Ubiquiti" }, { "04:18:D6", "Ubiquiti" },
            { "D8:D3:85", "HP" }, { "00:9C:02", "HP" }, { "00:11:0A", "HP" }, { "00:17:A4", "HP" }, { "00:1A:4B", "HP" },
            { "F8:FF:C2", "Apple" }, { "00:14:51", "Apple" }, { "00:16:CB", "Apple" }, { "34:36:3B", "Apple" }, { "A4:D1:8C", "Apple" }, { "40:4C:5E", "Apple" }, { "7C:D1:C3", "Apple" }, { "E0:C9:7A", "Apple" },
            { "00:23:14", "Intel" }, { "00:15:17", "Intel" }, { "F8:B3:B6", "Intel" }, { "88:B1:11", "Intel" }, { "00:1B:21", "Intel" }, { "3C:58:C2", "Intel" }, { "A4:4E:31", "Intel" },
            { "00:14:22", "Dell" }, { "D4:AE:52", "Dell" }, { "84:8F:69", "Dell" }, { "00:16:F7", "Dell" }, { "00:18:8B", "Dell" }, { "00:21:9B", "Dell" }, { "18:66:DA", "Dell" },
            { "B4:2E:99", "Gigabyte" }, { "00:D8:61", "Micro-Star" },
            { "DC:A6:32", "Raspberry Pi" }, { "B8:27:EB", "Raspberry Pi" },
            { "50:3E:AA", "TP-Link" }, { "74:DA:38", "TP-Link" }, { "84:16:F9", "TP-Link" }, { "B0:4E:26", "TP-Link" }, { "EC:08:6B", "TP-Link" },
            { "00:09:5B", "Netgear" }, { "00:14:6C", "Netgear" }, { "00:1E:2A", "Netgear" }, { "00:26:F2", "Netgear" }, { "20:4E:7F", "Netgear" },
            { "00:18:82", "Huawei" }, { "00:25:9E", "Huawei" }, { "28:6E:D4", "Huawei" },
            { "0C:38:3E", "Fanvil" }, { "00:A0:A5", "Fanvil" }, { "00:21:04", "Gigaset" }
        };

        public async Task<List<LanDevice>> ScanNetworkAsync(Action<int, int> progressCallback, Action<LanDevice> deviceFoundCallback, CancellationToken token)
        {
            var activeDevices = new List<LanDevice>();
            string localIp = await Task.Run(() => GetLocalIpAddress(), token);
            
            if (localIp == "127.0.0.1" || string.IsNullOrEmpty(localIp))
            {
                return activeDevices;
            }

            IPAddress subnetMask = await Task.Run(() =>
            {
                IPAddress mask = IPAddress.Parse("255.255.255.0"); // fallback /24
                try
                {
                    var localAddr = IPAddress.Parse(localIp);
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up) continue;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                                ua.Address.Equals(localAddr) && ua.IPv4Mask != null)
                            {
                                mask = ua.IPv4Mask;
                                break;
                            }
                        }
                    }
                }
                catch { /* use fallback /24 */ }
                return mask;
            }, token);

            byte[] ipBytes = IPAddress.Parse(localIp).GetAddressBytes();
            byte[] maskBytes = subnetMask.GetAddressBytes();
            byte[] networkBytes = new byte[4];
            byte[] broadcastBytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
            }

            uint networkAddr = (uint)(networkBytes[0] << 24 | networkBytes[1] << 16 | networkBytes[2] << 8 | networkBytes[3]);
            uint broadcastAddr = (uint)(broadcastBytes[0] << 24 | broadcastBytes[1] << 16 | broadcastBytes[2] << 8 | broadcastBytes[3]);
            
            uint hostCount = 0;
            if (broadcastAddr > networkAddr + 1)
            {
                hostCount = broadcastAddr - networkAddr - 1;
            }

            const uint maxHosts = 1024;
            if (hostCount > maxHosts) hostCount = maxHosts;
            if (hostCount == 0) return activeDevices;

            int totalIPs = (int)hostCount;
            int completed = 0;
            var syncLock = new object();

            var options = new ParallelOptions 
            { 
                MaxDegreeOfParallelism = 50, 
                CancellationToken = token 
            };

            // Pre-fetch ARP table on macOS/Linux
            var arpTable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new Dictionary<string, string>() : await GetMacArpTableAsync();

            await Parallel.ForEachAsync(Enumerable.Range(1, (int)hostCount), options, async (offset, ct) =>
            {
                uint targetAddr = networkAddr + (uint)offset;
                string targetIp = $"{(targetAddr >> 24) & 0xFF}.{(targetAddr >> 16) & 0xFF}.{(targetAddr >> 8) & 0xFF}.{targetAddr & 0xFF}";
                
                try
                {
                    bool isAlive = false;
                    try
                    {
                        using var pinger = new Ping();
                        var reply = await pinger.SendPingAsync(targetIp, 1000);
                        isAlive = (reply.Status == IPStatus.Success);
                    }
                    catch { }

                    if (ct.IsCancellationRequested) return;

                    if (isAlive || targetIp == localIp)
                    {
                        if (ct.IsCancellationRequested) return;

                        string mac = "";
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            await Task.Run(() => { mac = GetMacAddressWindows(targetIp); }, ct);
                        }
                        else
                        {
                            if (arpTable.TryGetValue(targetIp, out var m)) mac = m;
                        }
                        
                        var device = new LanDevice
                        {
                            IpAddress = targetIp,
                            MacAddress = string.IsNullOrEmpty(mac) ? (targetIp == localIp ? "Local Interface" : "Unknown") : mac,
                        };
                        
                        if (ct.IsCancellationRequested) return;

                        try
                        {
                            if (device.IpAddress == localIp)
                            {
                                device.Hostname = Dns.GetHostName();
                            }
                            else
                            {
                                var dnsTask = Dns.GetHostEntryAsync(device.IpAddress);
                                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                var timeoutTask = Task.Delay(1000, delayCts.Token);
                                var completedTask = await Task.WhenAny(dnsTask, timeoutTask);
                                if (completedTask == dnsTask)
                                {
                                    delayCts.Cancel();
                                    var hostEntry = await dnsTask;
                                    device.Hostname = hostEntry.HostName;
                                }
                                else
                                {
                                    device.Hostname = "-";
                                }
                            }
                        }
                        catch { device.Hostname = "-"; }

                        if (ct.IsCancellationRequested) return;

                        device.Manufacturer = await GetManufacturerAsync(device.MacAddress, ct);

                        if (ct.IsCancellationRequested) return;

                        lock (syncLock)
                        {
                            activeDevices.Add(device);
                        }
                        
                        deviceFoundCallback?.Invoke(device);
                    }
                }
                finally
                {
                    lock (syncLock)
                    {
                        completed++;
                        if (completed % 10 == 0 || completed == totalIPs)
                        {
                            progressCallback?.Invoke(completed, totalIPs);
                        }
                    }
                }
            });

            return activeDevices.OrderBy(d => 
            {
                if (IPAddress.TryParse(d.IpAddress, out var ip))
                {
                    var bytes = ip.GetAddressBytes();
                    return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
                }
                return 0u;
            }).ToList();
        }

        private static async Task<Dictionary<string, string>> GetMacArpTableAsync()
        {
            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var p = Process.Start(new ProcessStartInfo("arp", "-a")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    string output = await p.StandardOutput.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    
                    // Regex matching: (192.168.1.1) at 0:15:65:aa:bb:cc on en0 [ethernet]
                    var match = Regex.Matches(output, @"\(([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\)\s+at\s+([0-9a-fA-F:]+)", RegexOptions.Compiled);
                    foreach (Match m in match)
                    {
                        if (m.Groups.Count >= 3)
                        {
                            string ip = m.Groups[1].Value;
                            string mac = FormatMac(m.Groups[2].Value);
                            table[ip] = mac;
                        }
                    }
                }
            }
            catch { }
            return table;
        }

        private static string FormatMac(string raw)
        {
            var parts = raw.Split(':');
            return string.Join(":", parts.Select(p => p.PadLeft(2, '0').ToUpperInvariant()));
        }

        private string GetMacAddressWindows(string ipAddress)
        {
            try
            {
                var addr = IPAddress.Parse(ipAddress);
                byte[] mac = new byte[6];
                int len = mac.Length;
                int ipInt = BitConverter.ToInt32(addr.GetAddressBytes(), 0);
                
                int result = SendARP(ipInt, 0, mac, ref len);
                if (result == 0 && len == 6)
                {
                    return BitConverter.ToString(mac, 0, len).Replace("-", ":");
                }
            }
            catch { }
            return string.Empty;
        }

        public async Task<string> GetManufacturerAsync(string macAddress, CancellationToken token)
        {
            if (string.IsNullOrEmpty(macAddress) || macAddress == "Unknown" || macAddress == "Local Interface")
                return "-";

            string cleanMac = macAddress.Replace("-", ":").ToUpperInvariant();
            if (cleanMac.Length < 8) return "-";

            string prefix = cleanMac.Substring(0, 8); // "XX:XX:XX"
            
            if (_commonOuis.TryGetValue(prefix, out var vendor))
                return vendor;

            if (_ouiCache.TryGetValue(prefix, out var cachedVendor))
                return cachedVendor;

            try
            {
                await _macApiSemaphore.WaitAsync(token);
                try
                {
                    if (_ouiCache.TryGetValue(prefix, out cachedVendor))
                        return cachedVendor;

                    string url = $"https://api.macvendors.com/{Uri.EscapeDataString(prefix)}";
                    using var resp = await _httpClient.GetAsync(url, token);
                    if (resp.IsSuccessStatusCode)
                    {
                        string body = await resp.Content.ReadAsStringAsync(token);
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            string resolved = body.Trim();
                            _ouiCache[prefix] = resolved;
                            return resolved;
                        }
                    }
                }
                finally
                {
                    await Task.Delay(300, token); // respect rate limits
                    _macApiSemaphore.Release();
                }
            }
            catch { }

            return "-";
        }

        public static string GetLocalIpAddress()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                        {
                            return ua.Address.ToString();
                        }
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        public void Dispose()
        {
        }
    }
}
