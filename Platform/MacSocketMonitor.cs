using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgilicoDiagMac.Core;

namespace AgilicoDiagMac.Platform
{
    public class SocketInfoItem
    {
        public string Protocol { get; set; } = "TCP";
        public string LocalAddress { get; set; } = string.Empty;
        public int LocalPort { get; set; }
        public string RemoteAddress { get; set; } = string.Empty;
        public int RemotePort { get; set; }
        public string State { get; set; } = string.Empty;
        public string ProcessName { get; set; } = "Unknown";
        public string Pid { get; set; } = "-";

        public string LocalEndpoint => $"{LocalAddress}:{LocalPort}";
        public string RemoteEndpoint => RemotePort > 0 ? $"{RemoteAddress}:{RemotePort}" : "*:*";
    }

    public static class MacSocketMonitor
    {
        public static async Task<List<SocketInfoItem>> GetActiveSocketsAsync()
        {
            var results = new List<SocketInfoItem>();

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // lsof -i -P -n
                    string output = await MacSystemTools.RunBashCommandAsync("lsof -i -P -n");
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = Regex.Split(line.Trim(), @"\s+");
                        if (parts.Length >= 9)
                        {
                            string proc = parts[0];
                            string pid = parts[1];
                            string proto = parts[4].ToUpperInvariant();
                            string nameField = parts[8];
                            string state = parts.Length > 9 ? parts[9].Trim('(', ')') : (proto.Contains("UDP") ? "STATELESS" : "ESTABLISHED");

                            string local = "";
                            int localPort = 0;
                            string remote = "*";
                            int remotePort = 0;

                            if (nameField.Contains("->"))
                            {
                                var endpoints = nameField.Split("->");
                                ParseEndpoint(endpoints[0], out local, out localPort);
                                ParseEndpoint(endpoints[1], out remote, out remotePort);
                            }
                            else
                            {
                                ParseEndpoint(nameField, out local, out localPort);
                            }

                            results.Add(new SocketInfoItem
                            {
                                Protocol = proto,
                                LocalAddress = local,
                                LocalPort = localPort,
                                RemoteAddress = remote,
                                RemotePort = remotePort,
                                State = state,
                                ProcessName = proc,
                                Pid = pid
                            });
                        }
                    }
                }
                else
                {
                    // Fallback to IPGlobalProperties
                    var ipProps = IPGlobalProperties.GetIPGlobalProperties();
                    var tcpConns = ipProps.GetActiveTcpConnections();
                    foreach (var c in tcpConns)
                    {
                        results.Add(new SocketInfoItem
                        {
                            Protocol = "TCP",
                            LocalAddress = c.LocalEndPoint.Address.ToString(),
                            LocalPort = c.LocalEndPoint.Port,
                            RemoteAddress = c.RemoteEndPoint.Address.ToString(),
                            RemotePort = c.RemoteEndPoint.Port,
                            State = c.State.ToString(),
                            ProcessName = "System / .NET",
                            Pid = "-"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GetActiveSockets failed: {ex.Message}", ex, "MacSocketMonitor");
            }

            return results.OrderBy(s => s.LocalPort).ToList();
        }

        private static void ParseEndpoint(string raw, out string address, out int port)
        {
            address = "*";
            port = 0;
            int colon = raw.LastIndexOf(':');
            if (colon >= 0)
            {
                address = raw.Substring(0, colon);
                int.TryParse(raw.Substring(colon + 1), out port);
            }
            else
            {
                address = raw;
            }
        }
    }
}
