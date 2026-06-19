using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;

namespace AgilicoConnectChecker
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
        // P/Invoke hardening: restrict DLL search to System32 only
        [DllImport("iphlpapi.dll", ExactSpelling = true, SetLastError = true)]
        private static extern int SendARP(int DestIP, int SrcIP, byte[] pMacAddr, ref int PhyAddrLen);

        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private readonly Dictionary<string, string> _ouiCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // A curated list of common OUI prefixes for instant resolution without internet
        private readonly Dictionary<string, string> _commonOuis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "00:15:65", "Yealink" }, { "00:15:B9", "Yealink" }, { "80:5E:C0", "Yealink" }, { "E4:34:93", "Yealink" }, { "00:04:13", "Snom" },
            { "00:04:F2", "Polycom" }, { "00:E0:75", "Polycom" }, { "64:16:7F", "Polycom" }, { "00:0B:82", "Grandstream" }, { "C0:74:AD", "Grandstream" },
            { "00:11:58", "Cisco" }, { "00:1B:D4", "Cisco" }, { "00:01:96", "Cisco" }, { "00:14:1C", "Cisco" }, { "A4:93:4C", "Cisco" },
            { "B4:FB:E4", "Ubiquiti" }, { "F0:9F:C2", "Ubiquiti" }, { "18:E8:29", "Ubiquiti" }, { "74:83:C2", "Ubiquiti" }, { "04:18:D6", "Ubiquiti" },
            { "D8:D3:85", "HP" }, { "00:9C:02", "HP" },
            { "F8:FF:C2", "Apple" }, { "00:14:51", "Apple" }, { "00:16:CB", "Apple" }, { "34:36:3B", "Apple" },
            { "00:23:14", "Intel" }, { "00:15:17", "Intel" }, { "F8:B3:B6", "Intel" }, { "88:B1:11", "Intel" }, { "00:1B:21", "Intel" }, 
            { "00:14:22", "Dell" }, { "D4:AE:52", "Dell" }, { "84:8F:69", "Dell" },
            { "B4:2E:99", "Gigabyte" }, { "00:D8:61", "Micro-Star" },
            { "DC:A6:32", "Raspberry Pi" }, { "B8:27:EB", "Raspberry Pi" }
        };

        public async Task<List<LanDevice>> ScanNetworkAsync(Action<int, int> progressCallback, Action<LanDevice> deviceFoundCallback, CancellationToken token)
        {
            var activeDevices = new List<LanDevice>();
            string localIp = GetLocalIpAddress();
            
            if (localIp == "127.0.0.1" || string.IsNullOrEmpty(localIp))
            {
                return activeDevices;
            }

            // Determine actual subnet mask from the network interface
            IPAddress subnetMask = IPAddress.Parse("255.255.255.0"); // fallback
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
                            subnetMask = ua.IPv4Mask;
                            break;
                        }
                    }
                }
            }
            catch { /* use fallback /24 */ }

            // Calculate network range from IP and mask
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
            uint hostCount = broadcastAddr - networkAddr - 1; // exclude network and broadcast

            // Cap at 1024 hosts to prevent accidental huge scans
            const uint maxHosts = 1024;
            if (hostCount > maxHosts)
            {
                hostCount = maxHosts;
            }
            if (hostCount == 0) return activeDevices;

            // Calculate CIDR for display
            int cidr = 0;
            foreach (byte b in maskBytes)
            {
                for (int bit = 7; bit >= 0; bit--)
                {
                    if ((b & (1 << bit)) != 0) cidr++;
                    else break;
                }
            }

            int totalIPs = (int)hostCount;
            int completed = 0;
            
            var pingTasks = new List<Task>();
            var syncLock = new object();

            // Ping all addresses concurrently
            for (uint offset = 1; offset <= hostCount; offset++)
            {
                if (token.IsCancellationRequested) break;
                
                uint targetAddr = networkAddr + offset;
                string targetIp = $"{(targetAddr >> 24) & 0xFF}.{(targetAddr >> 16) & 0xFF}.{(targetAddr >> 8) & 0xFF}.{targetAddr & 0xFF}";
                
                pingTasks.Add(Task.Run(async () =>
                {
                    bool isAlive = false;
                    try
                    {
                        using var pinger = new Ping();
                        var reply = await pinger.SendPingAsync(targetIp, 1000);
                        isAlive = (reply.Status == IPStatus.Success);
                    }
                    catch { }

                    if (isAlive || targetIp == localIp)
                    {
                        var mac = GetMacAddress(targetIp);
                        if (!string.IsNullOrEmpty(mac) || targetIp == localIp)
                        {
                            var device = new LanDevice
                            {
                                IpAddress = targetIp,
                                MacAddress = string.IsNullOrEmpty(mac) ? "Local Interface" : mac,
                            };
                            
                            // Get Hostname immediately
                            try
                            {
                                if (device.IpAddress == localIp)
                                {
                                    device.Hostname = Dns.GetHostName();
                                }
                                else
                                {
                                    var hostEntry = await Dns.GetHostEntryAsync(device.IpAddress);
                                    device.Hostname = hostEntry.HostName;
                                }
                            }
                            catch { device.Hostname = "-"; }

                            // Get Manufacturer immediately
                            device.Manufacturer = await GetManufacturerAsync(device.MacAddress);

                            lock (syncLock)
                            {
                                activeDevices.Add(device);
                            }
                            
                            deviceFoundCallback?.Invoke(device);
                        }
                    }

                    lock (syncLock)
                    {
                        completed++;
                        if (completed % 10 == 0 || completed == totalIPs)
                        {
                            progressCallback?.Invoke(completed, totalIPs);
                        }
                    }
                }, token));
            }

            await Task.WhenAll(pingTasks);

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

        private string GetMacAddress(string ipAddress)
        {
            try
            {
                var addr = IPAddress.Parse(ipAddress);
                byte[] mac = new byte[6];
                int length = mac.Length;
                
                // DestIP is in network byte order
                int destIp = BitConverter.ToInt32(addr.GetAddressBytes(), 0);
                
                int result = SendARP(destIp, 0, mac, ref length);
                if (result == 0)
                {
                    string[] str = new string[(int)length];
                    for (int i = 0; i < length; i++)
                        str[i] = mac[i].ToString("X2");
                    return string.Join(":", str);
                }
            }
            catch { }
            return "";
        }

        private async Task<string> GetManufacturerAsync(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress) || macAddress == "Local Interface")
                return "Current Device";

            if (macAddress.Length < 8) return "Unknown";

            string prefix = macAddress.Substring(0, 8).ToUpper(); // e.g., "00:15:65"
            
            if (_commonOuis.TryGetValue(prefix, out string? vendor) && vendor != null)
                return vendor;

            if (_ouiCache.TryGetValue(prefix, out string? cached) && cached != null)
                return cached;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.macvendors.com/{prefix}");
                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(result))
                    {
                        _ouiCache[prefix] = result;
                        return result;
                    }
                }
            }
            catch { } 

            _ouiCache[prefix] = "Unknown";
            return "Unknown";
        }

        private string GetLocalIpAddress()
        {
            try
            {
                using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint?.Address.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
