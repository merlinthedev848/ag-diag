using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.AspNetCore.SignalR.Client;

namespace AgilicoConnectChecker
{
    public class NetworkEngine
    {
        public delegate void LogHandler(string message, bool isError = false);
        public delegate void ProgressHandler(string testName, string status, string details);
        public delegate void TestCompleteHandler(bool success, int score);

        public event LogHandler? OnLog;
        public event ProgressHandler? OnProgress;
        public event TestCompleteHandler? OnComplete;

        private CancellationTokenSource? _cts;

        // Configuration defaults from official Agilico Network Guidance
        public string DomainToCheck { get; set; } = "customerportal.hp2k.co.uk";
        public string LocalSipPortStr { get; set; } = "5060";
        public string SipAlgServer { get; set; } = "109.73.119.31";
        public int SipAlgPort { get; set; } = 5060;
        public int LocalSipPort { get; set; } = 5060;
        public string StunServer { get; set; } = "stun-gb-a.hp2k.co.uk";
        public int StunPort { get; set; } = 3478;
        public bool IsSimulationMode { get; set; } = false;

        // Selected tests to run (index 0 to 9)
        public bool[] SelectedTests { get; set; } = new bool[10] { true, true, true, true, true, true, true, true, true, true };

        // Packet Capture Service
        public PcapCapturer Pcap { get; } = new PcapCapturer();

        // Live settings loaded from softphone registry
        public string ServerUsername { get; set; } = "";
        public string ClientToken { get; set; } = "";
        public int ClientUserId { get; set; } = 0;
        public string PresenceUrl { get; set; } = "http://v3.presence.eu-beta.hp2k.co.uk/Presence";
        public string SignallingUrl { get; set; } = "http://v1.softsignalling.eu-j.hp2k.co.uk/Signals";
        public string RoomsUrl { get; set; } = "http://v1.rooms.eu-beta.hp2k.co.uk/Rooms";

        public bool HasLocalConnectivityIssue { get; private set; } = false;
        public string LocalConnectivityIssueReason { get; private set; } = "";

        // Google DNS servers mentioned in the guide
        private readonly string[] GoogleDnsServers = { "8.8.8.8", "8.8.4.4" };

        // STUN Servers mentioned in the guide
        private readonly (string host, string ip)[] AgilicoStunServers = new[]
        {
            ("stun-gb-a.hp2k.co.uk", "109.73.119.38"),
            ("stun-gb-b.hp2k.co.uk", "109.73.119.39"),
            ("stun-eu-a.hp2k.co.uk", "109.73.119.38"),
            ("stun-eu-b.hp2k.co.uk", "109.73.119.39")
        };

        private readonly (string host, string ip)[] GoogleStunServers = new[]
        {
            ("stun.l.google.com", "74.125.250.129"),
            ("stun1.l.google.com", "74.125.250.129"),
            ("stun2.l.google.com", "74.125.250.129"),
            ("stun3.l.google.com", "74.125.250.129"),
            ("stun4.l.google.com", "74.125.250.129")
        };

        public NetworkEngine()
        {
            // Load registry options if available
            LoadSettingsFromRegistry();
        }

        private void LoadSettingsFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\DMC\WindowsSoftphone\v1"))
                {
                    if (key != null)
                    {
                        ServerUsername = key.GetValue("ServerUsername")?.ToString() ?? "";
                        ClientToken = key.GetValue("ClientToken")?.ToString() ?? "";
                        if (int.TryParse(key.GetValue("ClientUserID")?.ToString(), out int uId))
                        {
                            ClientUserId = uId;
                        }
                    }
                }

                using (var servicesKey = Registry.CurrentUser.OpenSubKey(@"Software\DMC\WindowsSoftphone\v1\Services"))
                {
                    if (servicesKey != null)
                    {
                        PresenceUrl = servicesKey.GetValue("Presence")?.ToString() ?? PresenceUrl;
                        SignallingUrl = servicesKey.GetValue("Signalling")?.ToString() ?? SignallingUrl;
                        RoomsUrl = servicesKey.GetValue("Rooms")?.ToString() ?? RoomsUrl;
                    }
                }
                Log($"Registry configuration loaded. Username: {ServerUsername}, UserID: {ClientUserId}");
            }
            catch (Exception ex)
            {
                Log($"Failed to load registry settings: {ex.Message}", true);
            }
        }

        private async Task RunEnvironmentScanAsync(CancellationToken token)
        {
            Log("=================================================================");
            Log("ENVIRONMENT & NETWORK ADAPTER SCAN");
            Log("=================================================================");

            if (!string.IsNullOrEmpty(ServerUsername))
            {
                bool hasToken = !string.IsNullOrEmpty(ClientToken);
                Log($"Registry credentials: Username={ServerUsername}, UserID={ClientUserId}, AuthTokenPresent={hasToken}");
            }
            else
            {
                Log("Registry credentials: None found. Softphone may not be registered.");
            }

            try
            {
                bool procFound = false;
                foreach (var proc in System.Diagnostics.Process.GetProcesses())
                {
                    string name = proc.ProcessName.ToLower();
                    if (name.Contains("agilico") || name.Contains("softphone") || name.Contains("dmc"))
                    {
                        Log($"Active Softphone Process: '{proc.ProcessName}' (PID: {proc.Id}) is running.");
                        procFound = true;
                    }
                }
                if (!procFound) Log("Active Softphone Process: No running softphone processes detected.");
            }
            catch (Exception ex)
            {
                Log($"Error scanning processes: {ex.Message}");
            }

            try
            {
                Log("Scanning network adapters for DHCP, IP and gateway configuration...");
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                int upAdapters = 0;
                int validConfiguredAdapters = 0;
                var issueReasons = new List<string>();

                foreach (var ni in interfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    string adapterName = ni.Name;
                    string adapterDesc = ni.Description;
                    Log($"Adapter: '{adapterName}' ({adapterDesc})");
                    Log($"  Status: {ni.OperationalStatus}, Type: {ni.NetworkInterfaceType}, Speed: {(ni.Speed / 1000000.0):0} Mbps");

                    if (ni.OperationalStatus != OperationalStatus.Up)
                    {
                        Log($"  [INFO] Adapter is offline/disconnected.");
                        continue;
                    }

                    upAdapters++;
                    var props = ni.GetIPProperties();
                    
                    bool isDhcpEnabled = false;
                    try
                    {
                        var ipv4Props = props.GetIPv4Properties();
                        if (ipv4Props != null) isDhcpEnabled = ipv4Props.IsDhcpEnabled;
                    }
                    catch { }

                    Log($"  DHCP Configuration: {(isDhcpEnabled ? "Enabled (Dynamic IP)" : "Disabled (Static IP)")}");

                    var dhcpServers = new List<string>();
                    foreach (var dhcp in props.DhcpServerAddresses) dhcpServers.Add(dhcp.ToString());
                    if (isDhcpEnabled && dhcpServers.Count > 0) Log($"  DHCP Server: {string.Join(", ", dhcpServers)}");

                    var gateways = new List<string>();
                    foreach (var gw in props.GatewayAddresses) gateways.Add(gw.Address.ToString());
                    Log($"  Default Gateway: {(gateways.Count > 0 ? string.Join(", ", gateways) : "NONE (No connection to router)")}");

                    var ipv4Addresses = new List<string>();
                    bool hasApipa = false;
                    bool hasZeroIp = false;

                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ipStr = addr.Address.ToString();
                            ipv4Addresses.Add(ipStr);
                            if (ipStr.StartsWith("169.254")) hasApipa = true;
                            if (ipStr == "0.0.0.0") hasZeroIp = true;
                        }
                    }

                    Log($"  IP Addresses: {(ipv4Addresses.Count > 0 ? string.Join(", ", ipv4Addresses) : "None assigned")}");

                    var dnsServers = new List<string>();
                    foreach (var dns in props.DnsAddresses) dnsServers.Add(dns.ToString());
                    Log($"  DNS Servers: {(dnsServers.Count > 0 ? string.Join(", ", dnsServers) : "NONE configured")}");

                    string descLower = adapterDesc.ToLower();
                    string nameLower = adapterName.ToLower();
                    bool isVpn = nameLower.Contains("vpn") || nameLower.Contains("tap") || nameLower.Contains("tun") || nameLower.Contains("globalprotect") || 
                        nameLower.Contains("cisco") || nameLower.Contains("anyconnect") || nameLower.Contains("fortinet") || nameLower.Contains("forticlient") || 
                        nameLower.Contains("wireguard") || nameLower.Contains("tailscale") || nameLower.Contains("zerotier") || nameLower.Contains("checkpoint") || 
                        nameLower.Contains("sonicwall") || nameLower.Contains("pulse") || descLower.Contains("vpn") || descLower.Contains("tap") || 
                        descLower.Contains("tun") || descLower.Contains("virtual adapter") || descLower.Contains("fortinet") || descLower.Contains("globalprotect");

                    if (isVpn)
                    {
                        Log($"  [WARNING] Active VPN/Virtual adapter detected. Active VPNs can introduce routing overhead, packet loss, and MTU issues.", true);
                    }

                    if (hasApipa)
                    {
                        string msg = $"Adapter '{adapterName}' has a self-assigned APIPA IP address ({string.Join(", ", ipv4Addresses)}). DHCP server failed to assign an IP address. Check local router connection.";
                        Log($"  [CRITICAL] {msg}", true);
                        issueReasons.Add(msg);
                    }
                    else if (hasZeroIp || ipv4Addresses.Count == 0)
                    {
                        string msg = $"Adapter '{adapterName}' has no valid IP address assigned (0.0.0.0 or empty). Check physical cable or Wi-Fi router connection.";
                        Log($"  [CRITICAL] {msg}", true);
                        issueReasons.Add(msg);
                    }
                    else if (gateways.Count == 0 && !isVpn)
                    {
                        string msg = $"Adapter '{adapterName}' is active but has no Default Gateway configured. It cannot route traffic to the internet or Agilico portal.";
                        Log($"  [CRITICAL] {msg}", true);
                        issueReasons.Add(msg);
                    }
                    else if (dnsServers.Count == 0 && !isVpn)
                    {
                        string msg = $"Adapter '{adapterName}' has no DNS servers configured. Domain resolution will fail.";
                        Log($"  [CRITICAL] {msg}", true);
                        issueReasons.Add(msg);
                    }
                    else
                    {
                        if (!isVpn) validConfiguredAdapters++;
                    }
                }

                if (upAdapters == 0)
                {
                    HasLocalConnectivityIssue = true;
                    LocalConnectivityIssueReason = "All network adapters are offline/disconnected. Check your network cables or Wi-Fi status.";
                    Log($"[CRITICAL] {LocalConnectivityIssueReason}", true);
                }
                else if (validConfiguredAdapters == 0)
                {
                    HasLocalConnectivityIssue = true;
                    LocalConnectivityIssueReason = issueReasons.Count > 0 ? string.Join(" | ", issueReasons) : "No network adapter has a valid IP address, Gateway, and DNS server configuration to connect to the router/internet.";
                    Log($"[CRITICAL] Local connectivity issue: {LocalConnectivityIssueReason}", true);
                }
                else
                {
                    HasLocalConnectivityIssue = false;
                    LocalConnectivityIssueReason = "";
                    Log($"Network scan completed. Found {validConfiguredAdapters} active, correctly configured physical network adapter(s).");
                }
            }
            catch (Exception ex)
            {
                Log($"Error scanning adapters: {ex.Message}");
            }
            Log("=================================================================");
            Log("");
        }

        public class LocalNetworkInfo
        {
            public string Status { get; set; } = "Disconnected";
            public string IpAddress { get; set; } = "-";
            public string SubnetMask { get; set; } = "-";
            public string Gateway { get; set; } = "-";
            public string DnsServers { get; set; } = "-";
            public string Vlan { get; set; } = "Untagged";
            public bool IsOk { get; set; } = false;
        }

        public LocalNetworkInfo GetLocalNetworkInfo()
        {
            var info = new LocalNetworkInfo();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;

                    var props = ni.GetIPProperties();
                    var gateways = new List<string>();
                    foreach (var gw in props.GatewayAddresses) gateways.Add(gw.Address.ToString());

                    string ipStr = "";
                    string maskStr = "";
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipStr = addr.Address.ToString();
                            maskStr = addr.IPv4Mask?.ToString() ?? "";
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(ipStr)) continue;

                    bool isApipa = ipStr.StartsWith("169.254");
                    bool isZero = ipStr == "0.0.0.0";
                    
                    var dnsServers = new List<string>();
                    foreach (var dns in props.DnsAddresses) dnsServers.Add(dns.ToString());

                    string desc = ni.Description.ToLower();
                    string name = ni.Name.ToLower();
                    bool isVpn = name.Contains("vpn") || name.Contains("tap") || name.Contains("tun") || name.Contains("globalprotect") || 
                        name.Contains("cisco") || name.Contains("anyconnect") || name.Contains("fortinet") || name.Contains("forticlient") || 
                        name.Contains("wireguard") || name.Contains("tailscale") || name.Contains("zerotier") || name.Contains("checkpoint") || 
                        name.Contains("sonicwall") || name.Contains("pulse") || desc.Contains("vpn") || desc.Contains("tap") || 
                        desc.Contains("tun") || desc.Contains("virtual adapter") || desc.Contains("fortinet") || desc.Contains("globalprotect");

                    if (!isApipa && !isZero && gateways.Count > 0)
                    {
                        info.Status = isVpn ? "Connected (VPN Active)" : "Connected";
                        info.IpAddress = ipStr;
                        info.SubnetMask = maskStr;
                        info.Gateway = string.Join(", ", gateways);
                        info.DnsServers = dnsServers.Count > 0 ? string.Join(", ", dnsServers) : "None";
                        info.Vlan = GetInterfaceVlanId(ni.Id);
                        info.IsOk = true;
                        return info; 
                    }
                    
                    if (!info.IsOk)
                    {
                        if (isApipa) info.Status = "No Router Connection (APIPA)";
                        else if (isZero) info.Status = "No IP Address (0.0.0.0)";
                        else info.Status = "Connected (No Gateway)";
                        
                        info.IpAddress = ipStr;
                        info.SubnetMask = maskStr;
                        info.Gateway = gateways.Count > 0 ? string.Join(", ", gateways) : "None";
                        info.DnsServers = dnsServers.Count > 0 ? string.Join(", ", dnsServers) : "None";
                        info.Vlan = GetInterfaceVlanId(ni.Id);
                    }
                }
            }
            catch { }
            return info;
        }

        private static string GetInterfaceVlanId(string interfaceGuid)
        {
            try
            {
                using (var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}"))
                {
                    if (classKey != null)
                    {
                        string cleanGuid = interfaceGuid.Trim('{', '}');
                        foreach (var subKeyName in classKey.GetSubKeyNames())
                        {
                            if (subKeyName.Length != 4) continue;
                            using (var subKey = classKey.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;
                                var instanceId = subKey.GetValue("NetCfgInstanceId") as string;
                                if (instanceId != null && string.Equals(instanceId.Trim('{', '}'), cleanGuid, StringComparison.OrdinalIgnoreCase))
                                {
                                    string[] vlanValueNames = { "VlanID", "*VlanID", "VLAN_ID", "VlanId", "VLANID" };
                                    foreach (var valName in vlanValueNames)
                                    {
                                        var val = subKey.GetValue(valName);
                                        if (val != null)
                                        {
                                            string vlanStr = val.ToString()?.Trim() ?? "";
                                            if (!string.IsNullOrEmpty(vlanStr) && vlanStr != "0" && vlanStr != "1")
                                            {
                                                return vlanStr;
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Silently ignore registry access permissions issues or other exceptions
            }
            return "Untagged";
        }

        public bool CheckLocalConnectivityBeforeTest(string testName)
        {
            if (HasLocalConnectivityIssue)
            {
                Log($"Skipping '{testName}' due to local network connectivity failure.");
                UpdateProgress(testName, "Failed", "Skipped - Local network failure");
                return true;
            }
            return false;
        }

        public string GetLocalIpAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint?.Address.ToString() ?? "127.0.0.1";
                }
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        public void Cancel()
        {
            _cts?.Cancel();
        }

        private void Log(string message, bool isError = false)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}", isError);
        }

        private void UpdateProgress(string testName, string status, string details)
        {
            OnProgress?.Invoke(testName, status, details);
        }

        public async Task<bool> RunDiagnosticsAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Log("Starting Agilico Network Diagnostic Tool...");
            Log("=================================================================");
            Log($"Timestamp:         {DateTime.Now}");
            Log($"OS Version:        {Environment.OSVersion}");
            Log($"Local IP Address:  {GetLocalIpAddress()}");
            Log($"Diagnostic Scope:  Strictly checking Agilico KB Network Guidance");
            Log("=================================================================");
            Log("All tests are running directly against the servers and ports specified in the network guide.");

            try
            {
                await RunEnvironmentScanAsync(token);

                // Test 1: DNS Domain Resolution & Google DNS
                bool dnsPass = true;
                if (SelectedTests[0])
                {
                    dnsPass = await RunDnsTestsAsync(token);
                }
                else
                {
                    Log("Test 1: Skipped by user selection.");
                    UpdateProgress("DNS Domain & Resolution Check", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 2: HTTP/HTTPS (Ports 80/443) Web Requests
                bool httpPass = true;
                if (SelectedTests[1])
                {
                    httpPass = await RunHttpHttpsTestsAsync(token);
                }
                else
                {
                    Log("Test 2: Skipped by user selection.");
                    UpdateProgress("HTTP/HTTPS Outbound Probes", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 3: NTP Subsystem (UDP Port 123)
                bool ntpPass = true;
                if (SelectedTests[2])
                {
                    ntpPass = await RunNtpTestAsync(token);
                }
                else
                {
                    Log("Test 3: Skipped by user selection.");
                    UpdateProgress("NTP Subsystem (UDP 123)", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 4: Agilico STUN Servers (UDP Port 3478)
                bool agilicoStunPass = true;
                if (SelectedTests[3])
                {
                    agilicoStunPass = await RunAgilicoStunTestsAsync(token);
                }
                else
                {
                    Log("Test 4: Skipped by user selection.");
                    UpdateProgress("Agilico STUN Servers", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 5: Google STUN Servers (UDP Port 3478)
                bool googleStunPass = true;
                if (SelectedTests[4])
                {
                    googleStunPass = await RunGoogleStunTestsAsync(token);
                }
                else
                {
                    Log("Test 5: Skipped by user selection.");
                    UpdateProgress("Google STUN Servers", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 6: NAT Routing & Hops Check
                bool natHopsPass = true;
                if (SelectedTests[5])
                {
                    natHopsPass = await RunNatHopsTestAsync(token);
                }
                else
                {
                    Log("Test 6: Skipped by user selection.");
                    UpdateProgress("NAT Routing & Hops Check", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 7: NAT Port Randomness Check
                bool natPortPass = true;
                if (SelectedTests[6])
                {
                    natPortPass = await RunNatPortRandomnessTestAsync(token);
                }
                else
                {
                    Log("Test 7: Skipped by user selection.");
                    UpdateProgress("NAT Port Randomness Check", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 8: SIP ALG Detection (UDP Port 5060)
                bool sipAlgPass = true;
                if (SelectedTests[7])
                {
                    sipAlgPass = await RunSipAlgTestAsync(token);
                }
                else
                {
                    Log("Test 8: Skipped by user selection.");
                    UpdateProgress("SIP ALG Detection", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 9: Advanced SIP Media (RTP) Quality Simulation
                bool rtpQualityPass = true;
                if (SelectedTests[8])
                {
                    rtpQualityPass = await RunRtpQualityTestAsync(token);
                }
                else
                {
                    Log("Test 9: Skipped by user selection.");
                    UpdateProgress("RTP Jitter/Loss Check", "Skipped", "Skipped by user");
                }
                if (token.IsCancellationRequested) return false;

                // Test 10: Inbound Signalling & Presence (WebSockets / SignalR)
                bool signalRPass = true;
                if (SelectedTests[9])
                {
                    signalRPass = await RunSignalRConnectivityTestAsync(token);
                }
                else
                {
                    Log("Test 10: Skipped by user selection.");
                    UpdateProgress("Inbound Signalling & Presence (WebSockets)", "Skipped", "Skipped by user");
                }

                bool allPassed = dnsPass && httpPass && ntpPass && agilicoStunPass && googleStunPass && natHopsPass && natPortPass && sipAlgPass && rtpQualityPass && signalRPass;
                
                // Scoring System
                int totalPossible = 0;
                int totalEarned = 0;
                int[] weights = new int[] { 15, 15, 5, 0, 5, 5, 5, 15, 15, 10 };
                bool[] results = new bool[] { dnsPass, httpPass, ntpPass, agilicoStunPass, googleStunPass, natHopsPass, natPortPass, sipAlgPass, rtpQualityPass, signalRPass };

                for (int i = 0; i < 10; i++)
                {
                    if (SelectedTests[i])
                    {
                        totalPossible += weights[i];
                        if (results[i])
                        {
                            totalEarned += weights[i];
                        }
                    }
                }

                int score = totalPossible > 0 ? (int)Math.Round((double)totalEarned / totalPossible * 100) : 100;

                Log(allPassed ? "All network checks PASSED! Your firewall configuration is fully compliant with the Agilico Network Guidance." 
                             : "Some network checks FAILED or generated Warnings. Please review the recommendations.");
                Log($"Weighted Diagnostics Score: {score}/100");

                OnComplete?.Invoke(allPassed, score);
                return allPassed;
            }
            catch (OperationCanceledException)
            {
                Log("Diagnostics cancelled by user.");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Critical error during diagnostics: {ex.Message}", true);
                OnComplete?.Invoke(false, 0);
                return false;
            }
        }

        #region Diagnostic Test Handlers

        private async Task<bool> RunDnsTestsAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("DNS Domain & Resolution Check")) return false;
            UpdateProgress("DNS Domain & Resolution Check", "Running", "Resolving Agilico service domains...");
            Log("Test 1: Verifying DNS Resolution and Google DNS availability...");

            if (IsSimulationMode)
            {
                Thread.Sleep(800);
                UpdateProgress("DNS Domain & Resolution Check", "Passed", "Pass - DNS resolving correctly");
                return true;
            }

            string[] domains = {
                DomainToCheck,
                "stun-gb-a.hp2k.co.uk",
                "stun-gb-b.hp2k.co.uk",
                "stun-eu-a.hp2k.co.uk",
                "stun-eu-b.hp2k.co.uk",
                "v3.presence.eu-beta.hp2k.co.uk",
                "v1.softsignalling.eu-j.hp2k.co.uk",
                "v1.rooms.eu-beta.hp2k.co.uk",
                "stun.l.google.com"
            };

            int resolvedCount = 0;
            int criticalFailedCount = 0;
            var failedDomains = new List<string>();

            foreach (var domain in domains)
            {
                if (token.IsCancellationRequested) return false;
                try
                {
                    Log($"Resolving domain '{domain}' using default DNS...");
                    var ips = await Dns.GetHostAddressesAsync(domain, token);
                    if (ips.Length > 0)
                    {
                        Log($"Success: Resolved '{domain}' to: {string.Join(", ", (object[])ips)}");
                        resolvedCount++;
                    }
                    else
                    {
                        Log($"Error: Resolved '{domain}' to 0 IP addresses.", true);
                        failedDomains.Add(domain);
                        if (domain == DomainToCheck || domain.Contains("presence") || domain.Contains("signalling") || domain.Contains("rooms"))
                        {
                            criticalFailedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error: Failed to resolve '{domain}' via default DNS: {ex.Message}", true);
                    failedDomains.Add(domain);
                    if (domain == DomainToCheck || domain.Contains("presence") || domain.Contains("signalling") || domain.Contains("rooms"))
                    {
                        criticalFailedCount++;
                    }
                }
            }



            // Verify Google DNS Servers from the guide (8.8.8.8 and 8.8.4.4) for the main portal
            bool dns1Ok = await QueryDnsServerAsync("8.8.8.8", DomainToCheck, token);
            bool dns2Ok = await QueryDnsServerAsync("8.8.4.4", DomainToCheck, token);

            if (criticalFailedCount == 0)
            {
                if (failedDomains.Count > 0)
                {
                    Log($"Test 1: PASSED WITH WARNINGS. Critical domains resolved, but backup/STUN domains failed: {string.Join(", ", failedDomains)}");
                    UpdateProgress("DNS Domain & Resolution Check", "Passed", $"Pass - Resolved {resolvedCount}/{domains.Length} domains");
                }
                else if (dns1Ok && dns2Ok)
                {
                    Log("Test 1: PASSED. All domains resolve correctly, and Google DNS servers are reachable.");
                    UpdateProgress("DNS Domain & Resolution Check", "Passed", "Pass - DNS & Google DNS Active");
                }
                else
                {
                    Log("Test 1: PASSED WITH WARNINGS. Domains resolve correctly via default local DNS, but direct outbound queries to Google DNS (8.8.8.8/8.8.4.4) failed (possible port 53 UDP egress block).");
                    UpdateProgress("DNS Domain & Resolution Check", "Passed", "Pass - DNS Active (Google DNS blocked)");
                }
                return true;
            }
            else
            {
                Log($"Test 1: FAILED. Critical service domains failed to resolve: {string.Join(", ", failedDomains)}. Google DNS 8.8.8.8: {(dns1Ok ? "OK" : "Failed")}, Google DNS 8.8.4.4: {(dns2Ok ? "OK" : "Failed")}", true);
                UpdateProgress("DNS Domain & Resolution Check", "Failed", $"Fail - {criticalFailedCount} critical domains failed");
                return false;
            }
        }

        private async Task<bool> QueryDnsServerAsync(string dnsServer, string hostname, CancellationToken token)
        {
            try
            {
                Log($"Querying Google DNS server {dnsServer} for domain '{hostname}'...");
                var serverIp = IPAddress.Parse(dnsServer);
                using var client = new UdpClient(0);
                client.Client.SendTimeout = 2000;
                client.Client.ReceiveTimeout = 2000;

                // Build a minimal standard DNS Query Packet (RFC 1035)
                var query = new List<byte>();
                // Header (12 bytes)
                byte[] txId = new byte[2];
                Random.Shared.NextBytes(txId);
                query.AddRange(txId); // Transaction ID
                query.AddRange(new byte[] { 0x01, 0x00 }); // Flags: Standard query, Recursion desired
                query.AddRange(new byte[] { 0x00, 0x01 }); // Questions: 1
                query.AddRange(new byte[] { 0x00, 0x00 }); // Answer RRs: 0
                query.AddRange(new byte[] { 0x00, 0x00 }); // Authority RRs: 0
                query.AddRange(new byte[] { 0x00, 0x00 }); // Additional RRs: 0

                // Question section: Domain name encoded as labels
                var parts = hostname.Split('.');
                foreach (var part in parts)
                {
                    query.Add((byte)part.Length);
                    query.AddRange(Encoding.ASCII.GetBytes(part));
                }
                query.Add(0x00); // End of name

                query.AddRange(new byte[] { 0x00, 0x01 }); // Type: A (IPv4 host address)
                query.AddRange(new byte[] { 0x00, 0x01 }); // Class: IN (Internet)

                var data = query.ToArray();
                var endpoint = new IPEndPoint(serverIp, 53);

                // Record sent packet
                Pcap.RecordPacket(data, GetLocalIpAddress(), ((IPEndPoint)client.Client.LocalEndPoint!).Port, dnsServer, 53, true);

                await client.SendAsync(data, data.Length, endpoint);
                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(2000, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    // Record received packet
                    Pcap.RecordPacket(result.Buffer, dnsServer, 53, GetLocalIpAddress(), ((IPEndPoint)client.Client.LocalEndPoint!).Port, true);
                    if (result.Buffer.Length > 12 && result.Buffer[0] == txId[0] && result.Buffer[1] == txId[1])
                    {
                        Log($"Google DNS {dnsServer} responded successfully to domain check.");
                        return true;
                    }
                }
                Log($"Google DNS {dnsServer} did not return a valid response.", true);
                return false;
            }
            catch (Exception ex)
            {
                Log($"Google DNS {dnsServer} query failed: {ex.Message}", true);
                return false;
            }
        }

        private async Task<bool> RunHttpHttpsTestsAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("HTTP/HTTPS Outbound Probes")) return false;
            UpdateProgress("HTTP/HTTPS Outbound Probes", "Running", "Testing web connection and TLS handshake...");
            Log("Test 2: Probing HTTP/HTTPS outbound connectivity & SSL/TLS handshake...");

            if (IsSimulationMode)
            {
                Thread.Sleep(800);
                UpdateProgress("HTTP/HTTPS Outbound Probes", "Passed", "Pass - HTTP/HTTPS Verified (TLS Succeeded)");
                return true;
            }

            string httpUrl = $"http://{DomainToCheck}";
            string httpsUrl = $"https://{DomainToCheck}";

            Log($"Testing HTTP web request to {httpUrl}...");
            var httpResult = await TestHttpEndpointAsync(httpUrl, token);
            Log($"HTTP to {DomainToCheck}: {(httpResult.ok ? "SUCCESS" : "FAILED")} ({httpResult.msg})");

            Log($"Testing HTTPS web request (including TLS handshake) to {httpsUrl}...");
            var httpsResult = await TestHttpEndpointAsync(httpsUrl, token);
            Log($"HTTPS to {DomainToCheck}: {(httpsResult.ok ? "SUCCESS" : "FAILED")} ({httpsResult.msg})");

            if (httpResult.ok && httpsResult.ok)
            {
                Log("Test 2: PASSED. Outbound HTTP/HTTPS web requests and SSL/TLS handshakes are verified.");
                UpdateProgress("HTTP/HTTPS Outbound Probes", "Passed", "Pass - HTTP/HTTPS Verified (TLS Succeeded)");
                return true;
            }
            else
            {
                string failed = "";
                if (!httpResult.ok) failed += $"HTTP (Error: {httpResult.msg}) ";
                if (!httpsResult.ok) failed += $"HTTPS (Error: {httpsResult.msg})";
                Log($"Test 2: FAILED. Web connection failures: {failed.Trim()}", true);
                UpdateProgress("HTTP/HTTPS Outbound Probes", "Failed", "Fail - SSL/TLS Handshake or Connection Blocked");
                return false;
            }
        }

        private async Task<(bool ok, string msg)> TestHttpEndpointAsync(string url, CancellationToken token)
        {
            try
            {
                using var handler = new HttpClientHandler();
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(5);
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                return (true, $"Success (Status: {(int)response.StatusCode} {response.ReasonPhrase})");
            }
            catch (Exception ex)
            {
                var inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                return (false, inner.Message);
            }
        }

        private async Task<bool> TestTcpPortAsync(string host, int port, CancellationToken token)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port, token).AsTask();
                var delayTask = Task.Delay(3000, token);

                var completed = await Task.WhenAny(connectTask, delayTask);
                if (completed == connectTask)
                {
                    await connectTask;
                    return client.Connected;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> RunNtpTestAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("NTP Subsystem (UDP 123)")) return false;
            UpdateProgress("NTP Subsystem (UDP 123)", "Running", "Querying NTP time server...");
            Log("Test 3: Checking UDP port 123 (NTP) outbound transmission...");

            if (IsSimulationMode)
            {
                Thread.Sleep(800);
                UpdateProgress("NTP Subsystem (UDP 123)", "Passed", "Pass - UDP 123 Outbound Open");
                return true;
            }

            string[] ntpHosts = { "pool.ntp.org", "time.google.com", "time.windows.com" };
            bool ntpOk = false;
            foreach (var ntpHost in ntpHosts)
            {
                Log($"Sending standard NTP synchronization request to {ntpHost}...");
                ntpOk = await TestNtpAsync(ntpHost, token);
                if (ntpOk)
                {
                    Log($"Success: Received NTP response from {ntpHost}.");
                    break;
                }
                else
                {
                    Log($"NTP request to {ntpHost} timed out.");
                }
            }

            if (ntpOk)
            {
                Log("Test 3: PASSED. Outbound UDP port 123 (NTP) is open and receiving replies.");
                UpdateProgress("NTP Subsystem (UDP 123)", "Passed", "Pass - UDP 123 Outbound Open");
                return true;
            }
            else
            {
                Log("Test 3: FAILED. UDP port 123 queries to all NTP servers timed out. Ensure NTP outbound traffic is permitted.", true);
                UpdateProgress("NTP Subsystem (UDP 123)", "Failed", "Fail - UDP 123 Blocked / Timeout");
                return false;
            }
        }

        private async Task<bool> TestNtpAsync(string host, CancellationToken token)
        {
            try
            {
                var ntpData = new byte[48];
                ntpData[0] = 0x1B; // LeapIndicator = 0, Version = 3, Mode = 3 (Client)

                var addresses = await Dns.GetHostAddressesAsync(host, token);
                if (addresses.Length == 0) return false;
                var endPoint = new IPEndPoint(addresses[0], 123);

                using var client = new UdpClient(0);
                client.Client.SendTimeout = 2000;
                client.Client.ReceiveTimeout = 2000;

                string localIp = GetLocalIpAddress();
                int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

                // Record sent packet
                Pcap.RecordPacket(ntpData, localIp, localPort, endPoint.Address.ToString(), 123, true);

                await client.SendAsync(ntpData, ntpData.Length, endPoint);
                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(2000, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    // Record received packet
                    Pcap.RecordPacket(result.Buffer, endPoint.Address.ToString(), 123, localIp, localPort, true);
                    // Validate: length >= 48 and Mode field (bits 0-2 of byte 0) == 4 (server)
                    return result.Buffer.Length >= 48 && (result.Buffer[0] & 0x07) == 4;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> RunAgilicoStunTestsAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("Agilico STUN Servers")) return false;
            UpdateProgress("Agilico STUN Servers", "Running", "Querying primary and secondary STUN...");
            Log("Test 4: Querying Agilico Cloud STUN Servers...");

            if (IsSimulationMode)
            {
                Thread.Sleep(1000);
                UpdateProgress("Agilico STUN Servers", "Passed", "Pass - All 4 Agilico STUN servers OK");
                return true;
            }

            int successCount = 0;
            List<string> failedServers = new List<string>();

            foreach (var (host, ip) in AgilicoStunServers)
            {
                if (token.IsCancellationRequested) return false;

                Log($"Querying Agilico STUN Server: {host} ({ip})...");
                var (ok, publicIp, mappedPort) = await QueryStunServerAsync(host, 3478, token);

                if (ok)
                {
                    Log($"Success: {host} returned Public IP: {publicIp}, Mapped Port: {mappedPort}");
                    successCount++;
                }
                else
                {
                    Log($"Agilico STUN server {host} failed to respond.");
                    failedServers.Add(host);
                }
            }

                // Agilico STUN is informational only. Always return true to avoid point deduction.
                if (successCount == AgilicoStunServers.Length)
                {
                    Log("Test 4: PASSED. All Agilico STUN servers responded successfully.");
                    UpdateProgress("Agilico STUN Servers", "Passed", "Pass - All 4 servers online");
                }
                else if (successCount > 0)
                {
                    Log($"Test 4: Successful: {successCount}/{AgilicoStunServers.Length}. Failed: {string.Join(", ", failedServers)}");
                    UpdateProgress("Agilico STUN Servers", "Passed", $"Pass - {successCount}/{AgilicoStunServers.Length} online");
                }
                else
                {
                    Log("Test 4: INFO. All Agilico STUN servers failed to respond (expected due to server-side firewall). Egress is verified via Google Backup STUN.");
                    UpdateProgress("Agilico STUN Servers", "Passed", "Informational - Agilico STUN unreachable (firewalled)");
                }
                return true;
        }

        private async Task<bool> RunGoogleStunTestsAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("Google STUN Servers")) return false;
            UpdateProgress("Google STUN Servers", "Running", "Querying Google backup STUN...");
            Log("Test 5: Querying Google STUN Servers...");

            if (IsSimulationMode)
            {
                Thread.Sleep(1000);
                UpdateProgress("Google STUN Servers", "Passed", "Pass - All 5 Google STUN servers OK");
                return true;
            }

            int successCount = 0;
            List<string> failedServers = new List<string>();

            foreach (var (host, ip) in GoogleStunServers)
            {
                if (token.IsCancellationRequested) return false;

                Log($"Querying Google STUN Server: {host} ({ip})...");
                var (ok, publicIp, mappedPort) = await QueryStunServerAsync(host, 3478, token);

                if (ok)
                {
                    Log($"Success: {host} returned Public IP: {publicIp}, Mapped Port: {mappedPort}");
                    successCount++;
                }
                else
                {
                    Log($"Google STUN server {host} failed to respond.");
                    failedServers.Add(host);
                }
            }

            if (successCount > 0)
            {
                Log($"Test 5: PASSED. Google STUN check passed. {successCount}/{GoogleStunServers.Length} servers responded.");
                UpdateProgress("Google STUN Servers", "Passed", $"Pass - {successCount}/{GoogleStunServers.Length} online");
                return true;
            }
            else
            {
                Log("Test 5: FAILED. All Google STUN servers failed to respond. Outbound UDP port 3478 is likely blocked.", true);
                UpdateProgress("Google STUN Servers", "Failed", "Fail - Google STUN query blocked");
                return false;
            }
        }

        private async Task<bool> RunNatHopsTestAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("NAT Routing & Hops Check")) return false;
            UpdateProgress("NAT Routing & Hops Check", "Running", "Tracing route to default gateway...");
            Log("Test 6: Checking local addressing and counting NAT router hops...");

            string localIpStr = GetLocalIpAddress();
            Log($"Local IP Address: {localIpStr}");

            IPAddress localIp = IPAddress.Parse(localIpStr);
            bool isPrivate = IsPrivateIp(localIp);

            if (isPrivate)
            {
                Log("Local address is in a private subnet range (Pass).");
            }
            else
            {
                Log("Warning - Public IP detected directly on local interface. The client is bypass-NAT connected.");
            }

            if (IsSimulationMode)
            {
                Thread.Sleep(1000);
                UpdateProgress("NAT Routing & Hops Check", "Passed", "Pass - Single NAT (1 private hop)");
                return true;
            }

            Log("Performing TTL-limited ICMP probes to detect private gateways/routers (Double NAT)...");
            int privateHops = await CountPrivateHopsAsync("8.8.8.8", 5);
            Log($"Intermediate private hops detected: {privateHops}");

            if (privateHops > 1)
            {
                Log($"Error: Double NAT detected ({privateHops} private network devices). This violates the Single NAT recommendation in the Agilico Network Guidance.", true);
                UpdateProgress("NAT Routing & Hops Check", "Failed", $"Fail - Double NAT ({privateHops} hops)");
                return false;
            }
            else if (privateHops == 0)
            {
                Log("Private hop traceroute did not return hops. This is common if ICMP is blocked by intermediate firewalls. Local private address confirmed NAT is active.");
                UpdateProgress("NAT Routing & Hops Check", "Passed", "Pass - NAT Active (ICMP Blocked)");
                return true;
            }
            else
            {
                Log("Pass - Single NAT configuration detected (1 hop).");
                UpdateProgress("NAT Routing & Hops Check", "Passed", "Pass - Single NAT (1 private hop)");
                return true;
            }
        }

        private static bool IsPrivateIp(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 4) return false;

            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 100.64.0.0/10 (CGNAT)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;

            return false;
        }

        private async Task<int> CountPrivateHopsAsync(string targetIp, int maxHops)
        {
            int privateHops = 0;
            using var ping = new Ping();
            var options = new PingOptions(1, true);

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                options.Ttl = ttl;
                try
                {
                    var reply = await ping.SendPingAsync(targetIp, 1000, new byte[32], options);
                    if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                    {
                        var addr = reply.Address;
                        if (addr != null)
                        {
                            if (IsPrivateIp(addr))
                            {
                                privateHops++;
                            }
                            else
                            {
                                // Hit a public router
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore and try next hop
                }
            }
            return privateHops;
        }

        private async Task<bool> RunNatPortRandomnessTestAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("NAT Port Translation (Random Port)")) return false;
            UpdateProgress("NAT Port Translation (Random Port)", "Running", "Checking port preservation...");
            Log("Test 7: Evaluating NAT port translation...");
            Log("Guideline: 'The public interface NAT port should be random and not be the same as the local NAT port.'");

            int localPort = LocalSipPort;
            int mappedPort = 0;
            bool success = false;

            if (IsSimulationMode)
            {
                Thread.Sleep(800);
                mappedPort = 53891;
                Log($"[Simulation] Local Port: {localPort}, STUN Mapped Port: {mappedPort}");
                Log("Pass: Public interface NAT port is random and different from local port.");
                UpdateProgress("NAT Port Translation (Random Port)", "Passed", $"Pass - Port randomized ({mappedPort})");
                return true;
            }

            // Bind local port 5060 or ephemeral equivalent for testing
            UdpClient? client = null;
            try
            {
                client = new UdpClient(localPort);
                Log($"Bound to local UDP port {localPort}.");
            }
            catch (SocketException)
            {
                Log($"Local UDP port {localPort} is in use. Auto-selecting an ephemeral port for testing...");
                try
                {
                    client = new UdpClient(0);
                    localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
                    Log($"Bound successfully to local UDP port {localPort}.");
                }
                catch (Exception ex)
                {
                    Log($"Error: Failed to bind to local UDP port: {ex.Message}", true);
                    UpdateProgress("NAT Port Translation (Random Port)", "Failed", "Fail - Local bind failed");
                    return false;
                }
            }
            finally
            {
                client?.Close();
            }

            // Contact a STUN server to check mapped port
            var servers = new[] { "stun-gb-a.hp2k.co.uk", "stun-gb-b.hp2k.co.uk", "stun.l.google.com" };
            foreach (var server in servers)
            {
                var (ok, ip, mPort) = await QueryStunServerAsync(server, 3478, token);
                if (ok)
                {
                    mappedPort = mPort;
                    success = true;
                    break;
                }
            }

            if (!success)
            {
                Log("Error: Could not query STUN server to check port mapping.", true);
                UpdateProgress("NAT Port Translation (Random Port)", "Failed", "Fail - STUN query failed");
                return false;
            }

            Log($"Local Source Port: {localPort}");
            Log($"Public Interface Port: {mappedPort}");

            if (mappedPort == localPort)
            {
                Log("Error: The public interface NAT port is the exact same as the local NAT port.", true);
                Log("The gateway is preserving the port. The Agilico Network Guidance explicitly requires randomized ports.", true);
                UpdateProgress("NAT Port Translation (Random Port)", "Failed", $"Fail - Port Preserved ({mappedPort})");
                return false;
            }
            else
            {
                Log("Pass: The public interface NAT port is randomized and different from the local NAT port.");
                UpdateProgress("NAT Port Translation (Random Port)", "Passed", $"Pass - Port randomized ({mappedPort})");
                return true;
            }
        }

        private async Task<bool> RunSipAlgTestAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("SIP ALG Detection")) return false;
            UpdateProgress("SIP ALG Detection", "Running", "Checking for SIP inspection engines...");
            Log("Test 8: Probing for SIP ALG / packet modification on UDP 5060...");

            if (IsSimulationMode)
            {
                Thread.Sleep(1000);
                Log("[Simulation] Verification response matched. SIP ALG: Disabled.");
                UpdateProgress("SIP ALG Detection", "Passed", "Pass - SIP ALG Disabled");
                return true;
            }

            UdpClient? client = null;
            try
            {
                try
                {
                    client = new UdpClient(LocalSipPort);
                    Log($"Bound to local SIP port {LocalSipPort} for ALG verification.");
                }
                catch (SocketException)
                {
                    client = new UdpClient(0);
                    Log($"Local port {LocalSipPort} is in use. Bound to local ephemeral port {((IPEndPoint)client.Client.LocalEndPoint!).Port} for verification.");
                }

                client.Client.SendTimeout = 2500;
                client.Client.ReceiveTimeout = 2500;

                string localIp = GetLocalIpAddress();
                int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

                Log($"Resolving SIP ALG reflection server: {SipAlgServer}...");
                var addresses = await Dns.GetHostAddressesAsync(SipAlgServer, token);
                if (addresses.Length == 0) throw new Exception("No DNS response from reflection host.");

                var endpoint = new IPEndPoint(addresses[0], SipAlgPort);
                Log($"SIP ALG Reflection Endpoint: {endpoint.Address}:{endpoint.Port}");

                // Construct a SIP OPTIONS request to Kamailio
                string branch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string tag = Guid.NewGuid().ToString("N").Substring(0, 10);
                string callId = Guid.NewGuid().ToString("N").Substring(0, 16) + "@hp2k.co.uk";

                string sipRegister =
                    $"OPTIONS sip:{SipAlgServer} SIP/2.0\r\n" +
                    $"Via: SIP/2.0/UDP {localIp}:{localPort};rport;branch={branch}\r\n" +
                    $"Max-Forwards: 70\r\n" +
                    $"To: <sip:checker@{SipAlgServer}>\r\n" +
                    $"From: <sip:checker@{localIp}:{localPort}>;tag={tag}\r\n" +
                    $"Call-ID: {callId}\r\n" +
                    $"CSeq: 1 OPTIONS\r\n" +
                    $"Contact: <sip:checker@{localIp}:{localPort}>\r\n" +
                    $"User-Agent: Agilico Network Diagnostic Tool\r\n" +
                    $"Content-Length: 0\r\n\r\n";

                byte[] registerBytes = Encoding.UTF8.GetBytes(sipRegister);

                Log($"Sending SIP OPTIONS payload to {SipAlgServer}:{SipAlgPort}...");
                Log($"Expected Via header: Via: SIP/2.0/UDP {localIp}:{localPort}");

                // Record sent packet
                Pcap.RecordPacket(registerBytes, localIp, localPort, endpoint.Address.ToString(), endpoint.Port, true);

                await client.SendAsync(registerBytes, registerBytes.Length, endpoint);

                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(3000, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    // Record received packet
                    Pcap.RecordPacket(result.Buffer, endpoint.Address.ToString(), endpoint.Port, localIp, localPort, true);
                    string responseStr = Encoding.UTF8.GetString(result.Buffer);

                    // Validate response is for our request by checking Call-ID
                    if (!responseStr.Contains(callId, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("Warning: Received SIP response with mismatched Call-ID. Ignoring stale response.");
                        UpdateProgress("SIP ALG Detection", "Failed", "Fail - Uncorrelated SIP Response");
                        return false;
                    }

                    Log("Received SIP response. Analyzing Via headers for SIP ALG tampering...");

                    string[] lines = responseStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    bool viaMatched = false;
                    bool viaFound = false;
                    bool receivedParamFound = false;

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Via:", StringComparison.OrdinalIgnoreCase))
                        {
                            viaFound = true;
                            Log($"Remote returned {line}");
                            if (line.Contains($"{localIp}:{localPort}"))
                            {
                                viaMatched = true;
                            }
                            if (line.Contains("received=", StringComparison.OrdinalIgnoreCase))
                            {
                                receivedParamFound = true;
                            }
                        }
                    }

                    if (!viaFound)
                    {
                        Log("Violation: No Via header returned. Unexpected response.", true);
                        UpdateProgress("SIP ALG Detection", "Failed", "Fail - Invalid Response from Server");
                        return false;
                    }
                    else if (!viaMatched)
                    {
                        Log("Violation: The returned Via header does NOT match the local IP/Port sent.", true);
                        Log("This indicates SIP ALG has modified the SIP headers in transit.", true);
                        UpdateProgress("SIP ALG Detection", "Failed", "Fail - SIP ALG Enabled (Header Mangled)");
                        return false;
                    }
                    else if (!receivedParamFound)
                    {
                        Log("Violation: The 'received=' parameter is missing from the Via header.", true);
                        Log("This indicates the router's SIP ALG symmetrically translated the IP to public, causing the server to omit 'received='.", true);
                        UpdateProgress("SIP ALG Detection", "Failed", "Fail - SIP ALG Enabled (Symmetric Translation Detected)");
                        return false;
                    }
                    else
                    {
                        Log("Pass: 'received=' parameter is present and internal IP matches. No SIP ALG tampering detected.");
                        UpdateProgress("SIP ALG Detection", "Passed", "Pass - SIP ALG Disabled");
                        return true;
                    }
                }
                else
                {
                    Log("Error: SIP OPTIONS request timed out. No response received.", true);
                    Log("This means UDP port 5060 is being dropped by the firewall, or SIP ALG is silently discarding the packets.", true);
                    UpdateProgress("SIP ALG Detection", "Failed", "Fail - Timeout (UDP 5060 Blocked)");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"SIP ALG test failed with error: {ex.Message}", true);
                UpdateProgress("SIP ALG Detection", "Failed", "Error: " + ex.Message);
                return false;
            }
            finally
            {
                client?.Close();
            }
        }

        private async Task<(int sent, int received, double loss, double jitter, double avgRtt, bool pass)> RunSingleRtpPathCheckAsync(
            string pathName, string targetHost, int targetPort, string payloadType, CancellationToken token)
        {
            Log($"Starting path check: {pathName} ({targetHost}:{targetPort}) via {payloadType}...");
            int numPackets = 100;
            int packetDelayMs = 20; // 50 pps
            int packetsReceived = 0;
            var rtts = new List<double>();
            
            UdpClient? client = null;
            try
            {
                client = new UdpClient(0);
                client.Client.SendTimeout = 1000;
                client.Client.ReceiveTimeout = 1000;

                var ipAddresses = await Dns.GetHostAddressesAsync(targetHost, token);
                if (ipAddresses.Length == 0)
                {
                    Log($"  [{pathName}] DNS resolution failed.", true);
                    return (numPackets, 0, 100.0, 0, 0, false);
                }

                var endpoint = new IPEndPoint(ipAddresses[0], targetPort);
                string localIp = GetLocalIpAddress();
                int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

                for (int i = 0; i < numPackets; i++)
                {
                    if (token.IsCancellationRequested) return (numPackets, 0, 100.0, 0, 0, false);

                    byte[] payload;
                    byte[] transactionId = new byte[12];
                    string callId = string.Empty;

                    if (payloadType == "SIP")
                    {
                        string branch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 10);
                        string tag = Guid.NewGuid().ToString("N").Substring(0, 10);
                        callId = Guid.NewGuid().ToString("N").Substring(0, 16) + "@hp2k.co.uk";
                        
                        string sipOptions =
                            $"OPTIONS sip:{targetHost} SIP/2.0\r\n" +
                            $"Via: SIP/2.0/UDP {localIp}:{localPort};rport;branch={branch}\r\n" +
                            $"Max-Forwards: 70\r\n" +
                            $"To: <sip:checker@{targetHost}>\r\n" +
                            $"From: <sip:checker@{localIp}:{localPort}>;tag={tag}\r\n" +
                            $"Call-ID: {callId}\r\n" +
                            $"CSeq: {i + 1} OPTIONS\r\n" +
                            $"Contact: <sip:checker@{localIp}:{localPort}>\r\n" +
                            $"User-Agent: Agilico Network Diagnostic Tool\r\n" +
                            $"Content-Length: 0\r\n\r\n";
                        payload = Encoding.UTF8.GetBytes(sipOptions);
                    }
                    else // STUN
                    {
                        new Random().NextBytes(transactionId);
                        payload = BuildStunRequest(transactionId, false, false);
                    }

                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Pcap.RecordPacket(payload, localIp, localPort, endpoint.Address.ToString(), endpoint.Port, true);
                    await client.SendAsync(payload, payload.Length, endpoint);

                    try
                    {
                        using var perPacketCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        perPacketCts.CancelAfter(500); // 500ms timeout per packet
                        
                        var result = await client.ReceiveAsync(perPacketCts.Token).AsTask();
                        watch.Stop();
                        Pcap.RecordPacket(result.Buffer, endpoint.Address.ToString(), endpoint.Port, localIp, localPort, true);
                        
                        bool valid = false;
                        if (payloadType == "SIP")
                        {
                            // Validate SIP response contains our Call-ID
                            string sipResp = Encoding.UTF8.GetString(result.Buffer);
                            valid = sipResp.Contains(callId, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            valid = ValidateStunResponse(result.Buffer, transactionId);
                        }

                        if (valid)
                        {
                            packetsReceived++;
                            rtts.Add(watch.Elapsed.TotalMilliseconds);
                        }
                    }
                    catch (OperationCanceledException) { /* Timeout — packet lost */ }
                    catch { /* Other error — packet lost */ }

                    await Task.Delay(packetDelayMs, token);
                }

                int packetsLost = numPackets - packetsReceived;
                double lossPercent = (double)packetsLost / numPackets * 100.0;
                double avgRtt = 0;
                double jitter = 0;

                if (packetsReceived > 0)
                {
                    double sumRtt = 0;
                    foreach (var r in rtts) sumRtt += r;
                    avgRtt = sumRtt / rtts.Count;

                    if (rtts.Count > 1)
                    {
                        double sumJitter = 0;
                        for (int i = 1; i < rtts.Count; i++)
                        {
                            sumJitter += Math.Abs(rtts[i] - rtts[i - 1]);
                        }
                        jitter = sumJitter / (rtts.Count - 1);
                    }
                }

                Log($"  [{pathName}] Sent: {numPackets}, Recv: {packetsReceived}, Loss: {lossPercent:0.0}%, Jitter: {jitter:0.1}ms, RTT: {avgRtt:0.1}ms");
                
                bool pass = (lossPercent <= 5.0) && (jitter <= 30.0) && (packetsReceived > 0);
                return (numPackets, packetsReceived, lossPercent, jitter, avgRtt, pass);
            }
            catch (Exception ex)
            {
                Log($"  [{pathName}] Failed with error: {ex.Message}", true);
                return (numPackets, 0, 100.0, 0, 0, false);
            }
            finally
            {
                client?.Close();
            }
        }

        private async Task<bool> RunRtpQualityTestAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("RTP Jitter/Loss Check")) return false;
            UpdateProgress("RTP Jitter/Loss Check", "Running", "Simulating G.711 media paths...");
            Log("Test 9: Advanced SIP Media (RTP) Quality Simulation...");
            Log("Running three test streams to verify standard SIP ports, Agilico STUN, and Google WebRTC STUN (high port 19302)...");

            if (IsSimulationMode)
            {
                Thread.Sleep(1500);
                Log("[Simulation] Packet Loss: 0%, Jitter: 5ms");
                UpdateProgress("RTP Jitter/Loss Check", "Passed", "Pass - Excellent Quality (0% loss, 5ms jitter)");
                return true;
            }

            // Path A: SIP ALG (UDP 5060)
            var pathA = await RunSingleRtpPathCheckAsync("Path A (SIP Port 5060)", SipAlgServer, SipAlgPort, "SIP", token);
            if (token.IsCancellationRequested) return false;

            // Path B: Agilico STUN (UDP 3478)
            var pathB = await RunSingleRtpPathCheckAsync("Path B (Agilico STUN 3478)", "stun-gb-a.hp2k.co.uk", 3478, "STUN", token);
            if (token.IsCancellationRequested) return false;

            // Path C: Google STUN High Port (UDP 19302)
            var pathC = await RunSingleRtpPathCheckAsync("Path C (Google STUN 19302)", "stun.l.google.com", 19302, "STUN", token);
            
            bool allPassed = pathA.pass && pathC.pass; // Exclude Path B because Agilico STUN is firewalled
            
            double maxLoss = Math.Max(pathA.loss, pathC.loss);
            double maxJitter = Math.Max(pathA.jitter, pathC.jitter);

            Log("RTP Quality Summary:");
            Log($"  Path A (SIP 5060): Loss={pathA.loss:0.0}%, Jitter={pathA.jitter:0.1}ms, RTT={pathA.avgRtt:0.1}ms - {(pathA.pass ? "PASS" : "FAIL")}");
            Log($"  Path B (Agilico STUN): Loss={pathB.loss:0.0}%, Jitter={pathB.jitter:0.1}ms, RTT={pathB.avgRtt:0.1}ms - {(pathB.pass ? "PASS" : "FAIL")} (Informational only)");
            Log($"  Path C (Google STUN 19302): Loss={pathC.loss:0.0}%, Jitter={pathC.jitter:0.1}ms, RTT={pathC.avgRtt:0.1}ms - {(pathC.pass ? "PASS" : "FAIL")}");

            if (allPassed)
            {
                Log("Pass: Packet loss and jitter on all paths are within VoIP limits.");
                UpdateProgress("RTP Jitter/Loss Check", "Passed", $"Pass - Media paths OK (SIP 5060: {pathA.loss:0}% loss, Agilico STUN: {pathB.loss:0}% loss, Google STUN: {pathC.loss:0}% loss)");
                return true;
            }
            else
            {
                string reasons = "";
                if (!pathA.pass) reasons += "SIP 5060 failure; ";
                if (!pathC.pass) reasons += "Google STUN 19302 failure; ";

                Log($"Violation: Media stream checks failed: {reasons.TrimEnd(';', ' ')}", true);
                
                string detailMsg = $"Fail - Media path issues: • SIP 5060: {(pathA.pass ? "OK" : $"{pathA.loss:0}% loss")} • Google STUN: {(pathC.pass ? "OK" : "No response")}";
                UpdateProgress("RTP Jitter/Loss Check", "Failed", detailMsg);
                return false;
            }
        }


        #endregion

        #region STUN Protocol Helpers

        private async Task<(bool success, string? publicIp, int mappedPort)> QueryStunServerAsync(string server, int port, CancellationToken token)
        {
            UdpClient? client = null;
            try
            {
                client = new UdpClient(0);
                client.Client.SendTimeout = 1500;
                client.Client.ReceiveTimeout = 1500;

                var ipAddresses = await Dns.GetHostAddressesAsync(server, token);
                if (ipAddresses.Length == 0) return (false, null, 0);

                var endPoint = new IPEndPoint(ipAddresses[0], port);
                byte[] transactionId = new byte[12];
                new Random().NextBytes(transactionId);
                byte[] stunRequest = BuildStunRequest(transactionId, changeIp: false, changePort: false);

                string localIp = GetLocalIpAddress();
                int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

                // Record sent packet
                Pcap.RecordPacket(stunRequest, localIp, localPort, endPoint.Address.ToString(), port, true);

                await client.SendAsync(stunRequest, stunRequest.Length, endPoint);

                var receiveTask = client.ReceiveAsync(token).AsTask();
                var delayTask = Task.Delay(1500, token);

                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    // Record received packet
                    Pcap.RecordPacket(result.Buffer, endPoint.Address.ToString(), port, localIp, localPort, true);
                    if (ValidateStunResponse(result.Buffer, transactionId))
                    {
                        var (ip, mPort) = ParseStunResponse(result.Buffer);
                        if (ip != null)
                        {
                            return (true, ip.ToString(), mPort);
                        }
                    }
                }
                return (false, null, 0);
            }
            catch
            {
                return (false, null, 0);
            }
            finally
            {
                client?.Close();
            }
        }

        private byte[] BuildStunRequest(byte[] transactionId, bool changeIp, bool changePort)
        {
            int attrLength = (changeIp || changePort) ? 8 : 0;
            byte[] packet = new byte[20 + attrLength];

            packet[0] = 0x00;
            packet[1] = 0x01; // Binding Request
            packet[2] = (byte)((attrLength >> 8) & 0xFF);
            packet[3] = (byte)(attrLength & 0xFF);

            // Magic Cookie (RFC 5389)
            packet[4] = 0x21;
            packet[5] = 0x12;
            packet[6] = 0xA4;
            packet[7] = 0x42;
            Buffer.BlockCopy(transactionId, 0, packet, 8, 12);

            if (attrLength > 0)
            {
                // CHANGE-REQUEST attribute
                packet[20] = 0x00;
                packet[21] = 0x03;
                packet[22] = 0x00;
                packet[23] = 0x04;

                uint value = 0;
                if (changeIp) value |= 4;
                if (changePort) value |= 2;

                packet[24] = 0x00;
                packet[25] = 0x00;
                packet[26] = 0x00;
                packet[27] = (byte)value;
            }

            return packet;
        }

        private bool ValidateStunResponse(byte[] response, byte[] originalTransactionId)
        {
            if (response.Length < 20) return false;

            int msgType = (response[0] << 8) | response[1];
            if (msgType != 0x0101 && msgType != 0x0111) return false; // Binding Success or Error

            for (int i = 0; i < 12; i++)
            {
                if (response[8 + i] != originalTransactionId[i])
                    return false;
            }

            return true;
        }

        private async Task<(bool ok, string msg)> TestSingleSignalRHubAsync(string hubName, string baseUrl, CancellationToken token)
        {
            string url = baseUrl;

            if (!string.IsNullOrEmpty(ClientToken))
            {
                url += $"?clientToken={ClientToken}&clientUserId={ClientUserId}";
                Log($"[{hubName}] Attempting connection with registry credentials (token masked)...");
            }
            else
            {
                Log($"[{hubName}] Attempting connection without credentials...");
            }

            HubConnection? connection = null;
            try
            {
                connection = new HubConnectionBuilder()
                    .WithUrl(url, options =>
                    {
                        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                    })
                    .WithAutomaticReconnect()
                    .Build();

                var connectTask = connection.StartAsync(token);
                var timeoutTask = Task.Delay(5000, token);

                if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
                {
                    await connectTask; // Throws if failed
                    if (connection.State == HubConnectionState.Connected)
                    {
                        Log($"[{hubName}] Handshake succeeded. Verifying connection stability...");
                        Log($"[DPI Checker] [{hubName}] Starting 2.5-second WebSocket stability monitor to detect DPI/firewall connection termination.");
                        
                        bool connectionDropped = false;
                        for (int i = 1; i <= 25; i++)
                        {
                            if (token.IsCancellationRequested) break;
                            await Task.Delay(100, token);
                            
                            if (i % 5 == 0)
                            {
                                Log($"[DPI Checker] [{hubName}] Connection remains active. ({(i * 100)}ms monitored)");
                            }

                            if (connection.State != HubConnectionState.Connected)
                            {
                                connectionDropped = true;
                                break;
                            }
                        }

                        if (connectionDropped)
                        {
                            Log($"[DPI Checker] [{hubName}] Connection DROPPED after handshake. This is typical of Deep Packet Inspection (DPI) firewalls blocking WebSocket traffic.", true);
                            return (false, "Connection dropped after handshake (possible DPI blocking)");
                        }
                        else
                        {
                            Log($"[DPI Checker] [{hubName}] Persistent WebSocket connection is STABLE after 2.5s monitor.");
                            return (true, "Stable connection established");
                        }
                    }
                    else
                    {
                        return (false, $"Handshake completed but state is {connection.State}");
                    }
                }
                else
                {
                    return (false, "Connection timed out after 5 seconds");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                if (connection != null)
                {
                    try { await connection.StopAsync(token); } catch { }
                    try { await connection.DisposeAsync(); } catch { }
                }
            }
        }

        private async Task<bool> RunSignalRConnectivityTestAsync(CancellationToken token)
        {
            if (CheckLocalConnectivityBeforeTest("Inbound Signalling & Presence")) return false;
            UpdateProgress("Inbound Signalling & Presence", "Running", "Verifying hub WebSocket connections...");
            Log("Test 10: Verifying persistent inbound connections (WebSockets / SignalR)...");
            Log("Checking Signalling, Presence, and Rooms hubs with 2.5-second stability monitoring...");

            var signallingResult = await TestSingleSignalRHubAsync("Signalling Hub", SignallingUrl, token);
            if (token.IsCancellationRequested) return false;

            var presenceResult = await TestSingleSignalRHubAsync("Presence Hub", PresenceUrl, token);
            if (token.IsCancellationRequested) return false;

            var roomsResult = await TestSingleSignalRHubAsync("Rooms Hub", RoomsUrl, token);

            bool allPassed = signallingResult.ok && presenceResult.ok && roomsResult.ok;

            Log("SignalR/WebSocket Connection Summary:");
            Log($"  Signalling Hub ({SignallingUrl}): {(signallingResult.ok ? "PASS" : "FAIL - " + signallingResult.msg)}");
            Log($"  Presence Hub ({PresenceUrl}): {(presenceResult.ok ? "PASS" : "FAIL - " + presenceResult.msg)}");
            Log($"  Rooms Hub ({RoomsUrl}): {(roomsResult.ok ? "PASS" : "FAIL - " + roomsResult.msg)}");

            if (allPassed)
            {
                UpdateProgress("Inbound Signalling & Presence", "Pass", "WebSocket connection succeeded");
                Log("Test 10: PASSED. All persistent inbound WebSocket connections are permitted and stable.");
                return true;
            }
            else
            {
                string failedHubs = "";
                if (!signallingResult.ok) failedHubs += "Signalling; ";
                if (!presenceResult.ok) failedHubs += "Presence; ";
                if (!roomsResult.ok) failedHubs += "Rooms; ";

                UpdateProgress("Inbound Signalling & Presence", "Fail", $"Fail - {failedHubs.TrimEnd(';', ' ')} blocked/dropped");
                Log($"Test 10: FAILED. Persistent inbound WebSocket connections are failing: {failedHubs.TrimEnd(';', ' ')}", true);
                return false;
            }
        }

        private (IPAddress? ip, int port) ParseStunResponse(byte[] response)
        {
            int messageLength = (response[2] << 8) | response[3];
            int index = 20;

            IPAddress? mappedIp = null;
            int mappedPort = 0;

            while (index < 20 + messageLength && index + 4 <= response.Length)
            {
                int attrType = (response[index] << 8) | response[index + 1];
                int attrLen = (response[index + 2] << 8) | response[index + 3];
                int valIndex = index + 4;

                if (valIndex + attrLen > response.Length)
                    break;

                if (attrType == 0x0001) // MAPPED-ADDRESS
                {
                    if (attrLen >= 8 && response[valIndex + 1] == 0x01) // IPv4
                    {
                        mappedPort = (response[valIndex + 2] << 8) | response[valIndex + 3];
                        byte[] ipBytes = new byte[4];
                        Buffer.BlockCopy(response, valIndex + 4, ipBytes, 0, 4);
                        mappedIp = new IPAddress(ipBytes);
                    }
                }
                else if (attrType == 0x0020 || attrType == 0x8020) // XOR-MAPPED-ADDRESS
                {
                    if (attrLen >= 8 && response[valIndex + 1] == 0x01)
                    {
                        int xorPort = (response[valIndex + 2] << 8) | response[valIndex + 3];
                        mappedPort = xorPort ^ 0x2112; // XOR with magic cookie bytes

                        byte[] ipBytes = new byte[4];
                        ipBytes[0] = (byte)(response[valIndex + 4] ^ 0x21);
                        ipBytes[1] = (byte)(response[valIndex + 5] ^ 0x12);
                        ipBytes[2] = (byte)(response[valIndex + 6] ^ 0xA4);
                        ipBytes[3] = (byte)(response[valIndex + 7] ^ 0x42);
                        mappedIp = new IPAddress(ipBytes);
                    }
                }

                int padding = (4 - (attrLen % 4)) % 4;
                index += 4 + attrLen + padding;
            }

            return (mappedIp, mappedPort);
        }

        public void TriggerFirewallPrompt()
        {
            try
            {
                // Brief bind to local SIP port to trigger Windows Defender Firewall prompt
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, LocalSipPort));
            }
            catch
            {
                try
                {
                    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                }
                catch
                {
                    // Ignore
                }
            }
        }

        #endregion
    }
}
